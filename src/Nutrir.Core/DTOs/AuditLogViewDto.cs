using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AuditLogViewDto(
    int Id,
    DateTime Timestamp,
    string UserId,
    string UserDisplayName,
    string Action,
    string EntityType,
    string? EntityId,
    string? Details,
    string? IpAddress,
    AuditSource Source);
