using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AuditLogQueryRequest(
    int Page = 1,
    int PageSize = 25,
    DateTime? From = null,
    DateTime? To = null,
    string? Action = null,
    string? EntityType = null,
    AuditSource? Source = null,
    string? SearchTerm = null);
