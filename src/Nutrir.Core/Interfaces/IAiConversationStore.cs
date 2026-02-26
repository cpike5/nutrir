namespace Nutrir.Core.Interfaces;

public record ConversationSnapshot(List<object> History, List<ChatDisplayMessage> DisplayMessages);

public record ChatDisplayMessage(string Role, string? Text);

public interface IAiConversationStore
{
    Task<ConversationSnapshot?> LoadActiveSessionAsync(string userId);
    Task SaveMessagesAsync(string userId, List<object> newMessages, List<string?> displayTexts);
    Task ClearHistoryAsync(string userId);
}
