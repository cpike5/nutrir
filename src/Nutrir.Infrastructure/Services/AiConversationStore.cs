using System.Text.Json;
using Anthropic.Models.Messages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AiConversationStore : IAiConversationStore
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<AiConversationStore> _logger;
    private static readonly TimeSpan SessionExpiry = TimeSpan.FromHours(8);
    private const int MaxMessages = 100;

    public AiConversationStore(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<AiConversationStore> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<ConversationSnapshot?> LoadActiveSessionAsync(string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow - SessionExpiry;

        var conversation = await db.AiConversations
            .Where(c => c.UserId == userId && c.LastMessageAt > cutoff)
            .OrderByDescending(c => c.LastMessageAt)
            .Include(c => c.Messages.OrderBy(m => m.Id))
            .FirstOrDefaultAsync();

        if (conversation is null || conversation.Messages.Count == 0)
            return null;

        var history = new List<object>();
        var displayMessages = new List<ChatDisplayMessage>();

        foreach (var msg in conversation.Messages)
        {
            var messageParam = DeserializeMessage(msg);
            if (messageParam is not null)
            {
                history.Add(messageParam);
            }

            // Only add display messages for user text and assistant text (not tool results)
            if (msg.Role is "user" && msg.DisplayText is not null)
            {
                displayMessages.Add(new ChatDisplayMessage("user", msg.DisplayText));
            }
            else if (msg.Role is "assistant" && msg.DisplayText is not null)
            {
                displayMessages.Add(new ChatDisplayMessage("assistant", msg.DisplayText));
            }
        }

        _logger.LogInformation("Loaded active session with {Count} messages for user {UserId}",
            history.Count, userId);

        return new ConversationSnapshot(history, displayMessages);
    }

    public async Task SaveMessagesAsync(string userId, List<object> newMessages, List<string?> displayTexts)
    {
        if (newMessages.Count == 0)
            return;

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var cutoff = DateTime.UtcNow - SessionExpiry;

        var conversation = await db.AiConversations
            .Where(c => c.UserId == userId && c.LastMessageAt > cutoff)
            .OrderByDescending(c => c.LastMessageAt)
            .FirstOrDefaultAsync();

        if (conversation is null)
        {
            conversation = new AiConversation { UserId = userId };
            db.AiConversations.Add(conversation);
            await db.SaveChangesAsync();
        }

        conversation.LastMessageAt = DateTime.UtcNow;

        for (int i = 0; i < newMessages.Count; i++)
        {
            if (newMessages[i] is not MessageParam msg)
                continue;

            var displayText = i < displayTexts.Count ? displayTexts[i] : null;

            db.AiConversationMessages.Add(new AiConversationMessage
            {
                ConversationId = conversation.Id,
                Role = msg.Role.ToString().ToLowerInvariant(),
                ContentJson = SerializeContent(msg),
                DisplayText = displayText,
            });
        }

        await db.SaveChangesAsync();

        // Enforce max messages cap
        await TrimMessagesAsync(db, conversation.Id);
    }

    public async Task ClearHistoryAsync(string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var conversations = await db.AiConversations
            .Where(c => c.UserId == userId)
            .ToListAsync();

        if (conversations.Count > 0)
        {
            db.AiConversations.RemoveRange(conversations);
            await db.SaveChangesAsync();
        }
    }

    private async Task TrimMessagesAsync(AppDbContext db, int conversationId)
    {
        var totalMessages = await db.AiConversationMessages
            .Where(m => m.ConversationId == conversationId)
            .CountAsync();

        if (totalMessages <= MaxMessages)
            return;

        var excess = totalMessages - MaxMessages;
        var oldestIds = await db.AiConversationMessages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.Id)
            .Take(excess)
            .Select(m => m.Id)
            .ToListAsync();

        await db.AiConversationMessages
            .Where(m => oldestIds.Contains(m.Id))
            .ExecuteDeleteAsync();
    }

    private static string SerializeContent(MessageParam messageParam)
    {
        var content = messageParam.Content;

        // Content can be implicitly a string or List<ContentBlockParam>
        // Try to extract string first by checking the underlying value
        if (content.TryPickString(out var str))
        {
            return JsonSerializer.Serialize(str);
        }

        if (content.TryPickContentBlockParams(out var blocks))
        {
            var serialized = new List<StoredContentBlock>();
            foreach (var block in blocks)
            {
                if (block.TryPickText(out var textBlock))
                {
                    serialized.Add(new StoredContentBlock
                    {
                        Type = "text",
                        Text = textBlock.Text
                    });
                }
                else if (block.TryPickToolUse(out var toolUseBlock))
                {
                    serialized.Add(new StoredContentBlock
                    {
                        Type = "tool_use",
                        Id = toolUseBlock.ID,
                        Name = toolUseBlock.Name,
                        Input = JsonSerializer.Serialize(toolUseBlock.Input)
                    });
                }
                else if (block.TryPickToolResult(out var toolResultBlock))
                {
                    serialized.Add(new StoredContentBlock
                    {
                        Type = "tool_result",
                        ToolUseId = toolResultBlock.ToolUseID,
                        Content = toolResultBlock.Content?.ToString()
                    });
                }
            }
            return JsonSerializer.Serialize(serialized);
        }

        return "\"\"";
    }

    private MessageParam? DeserializeMessage(AiConversationMessage msg)
    {
        try
        {
            var role = msg.Role == "user" ? Role.User : Role.Assistant;

            // Try deserializing as a simple string first
            if (msg.ContentJson.StartsWith('"'))
            {
                var text = JsonSerializer.Deserialize<string>(msg.ContentJson);
                return new MessageParam { Role = role, Content = text ?? "" };
            }

            // Try deserializing as stored content blocks
            var blocks = JsonSerializer.Deserialize<List<StoredContentBlock>>(msg.ContentJson);
            if (blocks is null)
                return null;

            var contentBlocks = new List<ContentBlockParam>();
            foreach (var block in blocks)
            {
                switch (block.Type)
                {
                    case "text":
                        contentBlocks.Add(new TextBlockParam { Text = block.Text ?? "" });
                        break;
                    case "tool_use":
                        var input = !string.IsNullOrEmpty(block.Input)
                            ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(block.Input)
                                ?? new Dictionary<string, JsonElement>()
                            : new Dictionary<string, JsonElement>();
                        contentBlocks.Add(new ToolUseBlockParam
                        {
                            ID = block.Id ?? "",
                            Name = block.Name ?? "",
                            Input = input
                        });
                        break;
                    case "tool_result":
                        contentBlocks.Add(new ToolResultBlockParam(block.ToolUseId ?? "")
                        {
                            Content = block.Content ?? ""
                        });
                        break;
                }
            }

            return new MessageParam
            {
                Role = role,
                Content = new List<ContentBlockParam>(contentBlocks)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize conversation message {Id}", msg.Id);
            return null;
        }
    }

    private class StoredContentBlock
    {
        public string Type { get; set; } = "";
        public string? Text { get; set; }
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Input { get; set; }
        public string? ToolUseId { get; set; }
        public string? Content { get; set; }
    }
}
