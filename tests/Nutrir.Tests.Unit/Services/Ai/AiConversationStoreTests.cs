using Anthropic.Models.Messages;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services.Ai;

public class AiConversationStoreTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;
    private readonly AiConversationStore _sut;

    private const string UserId = "ai-store-test-user-001";
    private const string OtherUserId = "ai-store-test-user-002";

    public AiConversationStoreTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);
        _sut = new AiConversationStore(_dbContextFactory, NullLogger<AiConversationStore>.Instance);

        SeedApplicationUser();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection?.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedApplicationUser()
    {
        // AiConversation.UserId has a FK to ApplicationUsers.
        // Both users are seeded upfront so all tests can use either without
        // hitting FK constraint violations.
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = UserId,
            UserName = "ai-store-test@example.com",
            NormalizedUserName = "AI-STORE-TEST@EXAMPLE.COM",
            Email = "ai-store-test@example.com",
            NormalizedEmail = "AI-STORE-TEST@EXAMPLE.COM",
            FirstName = "Test",
            LastName = "User",
            DisplayName = "Test User",
            CreatedDate = DateTime.UtcNow,
        });

        _dbContext.Users.Add(new ApplicationUser
        {
            Id = OtherUserId,
            UserName = "ai-store-other@example.com",
            NormalizedUserName = "AI-STORE-OTHER@EXAMPLE.COM",
            Email = "ai-store-other@example.com",
            NormalizedEmail = "AI-STORE-OTHER@EXAMPLE.COM",
            FirstName = "Other",
            LastName = "User",
            DisplayName = "Other User",
            CreatedDate = DateTime.UtcNow,
        });

        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Seeds an active conversation (LastMessageAt within the 8-hour session window)
    /// and returns the created <see cref="AiConversation"/>.
    /// </summary>
    private AiConversation SeedActiveConversation(string userId)
    {
        var conversation = new AiConversation
        {
            UserId = userId,
            CreatedAt = DateTime.UtcNow,
            LastMessageAt = DateTime.UtcNow,
        };
        _dbContext.AiConversations.Add(conversation);
        _dbContext.SaveChanges();
        return conversation;
    }

    /// <summary>
    /// Adds a message to an existing conversation and returns the saved entity.
    /// </summary>
    private AiConversationMessage SeedMessage(
        int conversationId,
        string role,
        string contentJson,
        string? displayText = null)
    {
        var message = new AiConversationMessage
        {
            ConversationId = conversationId,
            Role = role,
            ContentJson = contentJson,
            DisplayText = displayText,
            CreatedAt = DateTime.UtcNow,
        };
        _dbContext.AiConversationMessages.Add(message);
        _dbContext.SaveChanges();
        return message;
    }

    // ---------------------------------------------------------------------------
    // LoadActiveSessionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LoadActiveSessionAsync_NoConversation_ReturnsNull()
    {
        // Arrange — database is empty for UserId (no conversations seeded)

        // Act
        var result = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadActiveSessionAsync_ExpiredConversation_ReturnsNull()
    {
        // Arrange — conversation whose LastMessageAt is outside the 8-hour window
        var conversation = new AiConversation
        {
            UserId = UserId,
            CreatedAt = DateTime.UtcNow.AddHours(-10),
            LastMessageAt = DateTime.UtcNow.AddHours(-9), // older than 8-hour expiry
        };
        _dbContext.AiConversations.Add(conversation);
        _dbContext.SaveChanges();

        SeedMessage(conversation.Id, "user", "\"Hello\"", "Hello");

        // Act
        var result = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        result.Should().BeNull(because: "the conversation is outside the 8-hour session window");
    }

    [Fact]
    public async Task LoadActiveSessionAsync_ActiveSession_ReturnsSnapshot()
    {
        // Arrange — active conversation with one user message
        var conversation = SeedActiveConversation(UserId);
        SeedMessage(conversation.Id, "user", "\"Hello\"", "Hello");

        // Act
        var result = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        result.Should().NotBeNull();
        result!.History.Should().HaveCount(1, because: "one message was seeded");
        result.DisplayMessages.Should().HaveCount(1);
        result.DisplayMessages[0].Role.Should().Be("user");
        result.DisplayMessages[0].Text.Should().Be("Hello");
    }

    [Fact]
    public async Task LoadActiveSessionAsync_ConversationWithNoMessages_ReturnsNull()
    {
        // Arrange — active conversation row exists but contains no messages
        SeedActiveConversation(UserId);

        // Act
        var result = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        result.Should().BeNull(because: "a conversation with zero messages should not produce a snapshot");
    }

    [Fact]
    public async Task LoadActiveSessionAsync_DisplayMessages_OnlyIncludesUserAndAssistantWithText()
    {
        // Arrange — three messages:
        //   1. user role with DisplayText     → included in DisplayMessages
        //   2. assistant role with DisplayText → included in DisplayMessages
        //   3. user role with null DisplayText  → excluded from DisplayMessages (tool result style)
        var conversation = SeedActiveConversation(UserId);
        SeedMessage(conversation.Id, "user", "\"What can you help with?\"", "What can you help with?");
        SeedMessage(conversation.Id, "assistant", "\"I can help with many things.\"", "I can help with many things.");
        SeedMessage(conversation.Id, "user", "\"\"", displayText: null); // tool result — no display text

        // Act
        var result = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        result.Should().NotBeNull();
        result!.History.Should().HaveCount(3, because: "all three messages deserialise into the History list");
        result.DisplayMessages.Should().HaveCount(2,
            because: "only the two messages with non-null DisplayText are shown in the UI");
        result.DisplayMessages[0].Role.Should().Be("user");
        result.DisplayMessages[1].Role.Should().Be("assistant");
    }

    // ---------------------------------------------------------------------------
    // SaveMessagesAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveMessagesAsync_EmptyList_ReturnsEarly()
    {
        // Arrange
        var emptyMessages = new List<object>();
        var emptyDisplayTexts = new List<string?>();

        // Act
        await _sut.SaveMessagesAsync(UserId, emptyMessages, emptyDisplayTexts);

        // Assert — no conversation row should have been created
        var conversations = _dbContext.AiConversations.Where(c => c.UserId == UserId).ToList();
        conversations.Should().BeEmpty(because: "SaveMessagesAsync returns early when the message list is empty");
    }

    [Fact]
    public async Task SaveMessagesAsync_CreatesConversation_WhenNoneExists()
    {
        // Arrange
        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "Hello" },
        };
        var displayTexts = new List<string?> { "Hello" };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);

        // Assert
        var conversations = _dbContext.AiConversations
            .Include(c => c.Messages)
            .Where(c => c.UserId == UserId)
            .ToList();

        conversations.Should().HaveCount(1, because: "a new conversation is created when none exists");
        conversations[0].Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task SaveMessagesAsync_AppendsToExistingConversation()
    {
        // Arrange — seed an existing active conversation with one message
        var existing = SeedActiveConversation(UserId);
        SeedMessage(existing.Id, "user", "\"First message\"", "First message");

        var newMessages = new List<object>
        {
            new MessageParam { Role = Role.Assistant, Content = "Second message" },
        };
        var displayTexts = new List<string?> { "Second message" };

        // Act
        await _sut.SaveMessagesAsync(UserId, newMessages, displayTexts);

        // Assert — original message plus the new one
        var messages = _dbContext.AiConversationMessages
            .Where(m => m.ConversationId == existing.Id)
            .ToList();

        messages.Should().HaveCount(2, because: "the new message is appended to the existing conversation");
    }

    [Fact]
    public async Task SaveMessagesAsync_UpdatesLastMessageAt()
    {
        // Arrange — seed a fresh active conversation.
        var conversation = SeedActiveConversation(UserId);
        var savedId = conversation.Id;

        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "Update me" },
        };

        // Act
        var callTime = DateTime.UtcNow;
        await _sut.SaveMessagesAsync(UserId, messages, new List<string?> { null });

        // Assert — reload from a fresh context instance to bypass the EF tracking cache.
        // LastMessageAt should be within a few seconds of when SaveMessagesAsync was called.
        // BeCloseTo is used instead of BeAfter to avoid SQLite sub-millisecond precision issues.
        await using var freshDb = await _dbContextFactory.CreateDbContextAsync();
        var updated = freshDb.AiConversations.Single(c => c.Id == savedId);
        updated.LastMessageAt.Should().BeCloseTo(callTime, TimeSpan.FromSeconds(5),
            because: "SaveMessagesAsync sets LastMessageAt to DateTime.UtcNow");
    }

    [Fact]
    public async Task SaveMessagesAsync_PairsDisplayTextsByIndex()
    {
        // Arrange — two messages each paired with their own display text
        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "User turn" },
            new MessageParam { Role = Role.Assistant, Content = "Assistant turn" },
        };
        var displayTexts = new List<string?> { "User turn display", "Assistant turn display" };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);

        // Assert
        var saved = _dbContext.AiConversationMessages
            .OrderBy(m => m.Id)
            .ToList();

        saved.Should().HaveCount(2);
        saved[0].DisplayText.Should().Be("User turn display",
            because: "display text at index 0 is paired with message at index 0");
        saved[1].DisplayText.Should().Be("Assistant turn display",
            because: "display text at index 1 is paired with message at index 1");
    }

    [Fact]
    public async Task SaveMessagesAsync_NonMessageParamObjects_AreSkipped()
    {
        // Arrange — mixed list where one item is not a MessageParam
        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "Real message" },
            "this is a plain string, not a MessageParam",
        };
        var displayTexts = new List<string?> { "Real message", null };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);

        // Assert — only the MessageParam was persisted
        var saved = _dbContext.AiConversationMessages.ToList();
        saved.Should().HaveCount(1,
            because: "non-MessageParam items in the list are skipped by the service");
        saved[0].Role.Should().Be("user");
    }

    [Fact]
    public async Task SaveMessagesAsync_UserRole_StoresRoleAsUser()
    {
        // Arrange
        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "Hi" },
        };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, new List<string?> { null });

        // Assert
        var saved = _dbContext.AiConversationMessages.Single();
        saved.Role.Should().Be("user");
    }

    [Fact]
    public async Task SaveMessagesAsync_AssistantRole_StoresRoleAsAssistant()
    {
        // Arrange
        var messages = new List<object>
        {
            new MessageParam { Role = Role.Assistant, Content = "Hi there" },
        };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, new List<string?> { null });

        // Assert
        var saved = _dbContext.AiConversationMessages.Single();
        saved.Role.Should().Be("assistant");
    }

    // ---------------------------------------------------------------------------
    // ClearHistoryAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ClearHistoryAsync_DeletesAllConversations()
    {
        // Arrange — seed two conversations for UserId
        var conv1 = SeedActiveConversation(UserId);
        var conv2 = SeedActiveConversation(UserId);
        SeedMessage(conv1.Id, "user", "\"First\"", "First");
        SeedMessage(conv2.Id, "user", "\"Second\"", "Second");

        // Act
        await _sut.ClearHistoryAsync(UserId);

        // Assert
        var remaining = _dbContext.AiConversations.Where(c => c.UserId == UserId).ToList();
        remaining.Should().BeEmpty(because: "ClearHistoryAsync should remove all conversations for the user");
    }

    [Fact]
    public async Task ClearHistoryAsync_OnlyDeletesConversationsForTargetUser()
    {
        // Arrange — one conversation for UserId and one for OtherUserId
        SeedActiveConversation(UserId);
        SeedActiveConversation(OtherUserId);

        // Act
        await _sut.ClearHistoryAsync(UserId);

        // Assert — OtherUserId's conversation is untouched
        var otherRemaining = _dbContext.AiConversations.Where(c => c.UserId == OtherUserId).ToList();
        otherRemaining.Should().HaveCount(1,
            because: "clearing history for one user must not affect other users' conversations");
    }

    [Fact]
    public async Task ClearHistoryAsync_NoConversations_DoesNotThrow()
    {
        // Arrange — no conversations seeded for UserId

        // Act
        Func<Task> act = () => _sut.ClearHistoryAsync(UserId);

        // Assert
        await act.Should().NotThrowAsync(because: "clearing an empty history is a no-op and should not throw");
    }

    // ---------------------------------------------------------------------------
    // Round-trip test
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesStringContent()
    {
        // Arrange — a user message with simple string content
        const string content = "What is the recommended daily protein intake?";
        const string displayText = content;

        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = content },
        };
        var displayTexts = new List<string?> { displayText };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);
        var snapshot = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.History.Should().HaveCount(1);

        var loadedParam = snapshot.History[0].Should().BeOfType<MessageParam>().Subject;
        // ApiEnum uses operator== for equality; .Be() uses structural equality which fails for this type
        (loadedParam.Role == Role.User).Should().BeTrue(because: "the role should round-trip as 'user'");

        // The content round-trips through JSON serialisation; verify it comes back as a string
        loadedParam.Content.TryPickString(out var loadedText).Should().BeTrue(
            because: "simple string content should deserialise back to a string");
        loadedText.Should().Be(content);

        snapshot.DisplayMessages.Should().HaveCount(1);
        snapshot.DisplayMessages[0].Text.Should().Be(displayText);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_PreservesAssistantStringContent()
    {
        // Arrange
        const string content = "You should aim for around 0.8 g of protein per kg of body weight.";

        var messages = new List<object>
        {
            new MessageParam { Role = Role.Assistant, Content = content },
        };
        var displayTexts = new List<string?> { content };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);
        var snapshot = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        snapshot.Should().NotBeNull();

        var loadedParam = snapshot!.History[0].Should().BeOfType<MessageParam>().Subject;
        (loadedParam.Role == Role.Assistant).Should().BeTrue(because: "the role should round-trip as 'assistant'");

        loadedParam.Content.TryPickString(out var loadedText).Should().BeTrue();
        loadedText.Should().Be(content);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip_MultipleMessages_PreservesOrder()
    {
        // Arrange — user then assistant exchange
        var messages = new List<object>
        {
            new MessageParam { Role = Role.User, Content = "Turn 1" },
            new MessageParam { Role = Role.Assistant, Content = "Turn 2" },
        };
        var displayTexts = new List<string?> { "Turn 1", "Turn 2" };

        // Act
        await _sut.SaveMessagesAsync(UserId, messages, displayTexts);
        var snapshot = await _sut.LoadActiveSessionAsync(UserId);

        // Assert
        snapshot.Should().NotBeNull();
        snapshot!.History.Should().HaveCount(2);

        var first = snapshot.History[0].Should().BeOfType<MessageParam>().Subject;
        (first.Role == Role.User).Should().BeTrue(because: "first message role should round-trip as 'user'");
        first.Content.TryPickString(out var firstText);
        firstText.Should().Be("Turn 1");

        var second = snapshot.History[1].Should().BeOfType<MessageParam>().Subject;
        (second.Role == Role.Assistant).Should().BeTrue(because: "second message role should round-trip as 'assistant'");
        second.Content.TryPickString(out var secondText);
        secondText.Should().Be("Turn 2");
    }
}
