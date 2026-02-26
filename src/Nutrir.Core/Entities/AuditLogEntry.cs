using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class AuditLogEntry
{
    public int Id { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public string UserId { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string? EntityId { get; set; }

    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    public AuditSource Source { get; set; } = AuditSource.Web;
}
