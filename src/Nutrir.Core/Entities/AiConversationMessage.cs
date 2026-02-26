namespace Nutrir.Core.Entities;

public class AiConversationMessage
{
    public int Id { get; set; }

    public int ConversationId { get; set; }

    public string Role { get; set; } = string.Empty;

    public string ContentJson { get; set; } = string.Empty;

    public string? DisplayText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
