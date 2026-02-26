namespace Nutrir.Core.Entities;

public class AiUsageLog
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int ToolCallCount { get; set; }

    public int DurationMs { get; set; }

    public string? Model { get; set; }
}
