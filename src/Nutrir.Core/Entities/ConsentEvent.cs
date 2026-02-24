using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ConsentEvent
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public ConsentEventType EventType { get; set; }

    public string ConsentPurpose { get; set; } = string.Empty;

    public string PolicyVersion { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string RecordedByUserId { get; set; } = string.Empty;

    public string? Notes { get; set; }
}
