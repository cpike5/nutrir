using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        string userId,
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null);

    Task<List<AuditLogDto>> GetRecentAsync(int count = 10);

    Task<AuditLogPageResult> QueryAsync(AuditLogQueryRequest request);

    Task<List<string>> GetDistinctActionsAsync();

    Task<List<string>> GetDistinctEntityTypesAsync();
}
