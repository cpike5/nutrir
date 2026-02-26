namespace Nutrir.Core.Entities;

public class AiConversation
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    public List<AiConversationMessage> Messages { get; set; } = [];
}
