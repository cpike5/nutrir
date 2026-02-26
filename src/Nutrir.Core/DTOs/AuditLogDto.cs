using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AuditLogDto(
    int Id,
    DateTime Timestamp,
    string UserId,
    string Action,
    string EntityType,
    string? EntityId,
    string? Details,
    AuditSource Source = AuditSource.Web);
