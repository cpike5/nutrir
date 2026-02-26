namespace Nutrir.Core.DTOs;

public record AuditLogPageResult(
    List<AuditLogViewDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
