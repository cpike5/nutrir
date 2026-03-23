using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IDataPurgeService
{
    Task<PurgeSummaryDto?> GetPurgeSummaryAsync(int clientId);
    Task<DataPurgeResult> ExecutePurgeAsync(int clientId, string confirmation, string userId);
    Task<List<RetentionClientDto>> GetExpiringClientsAsync(int withinDays = 90);
    Task<List<RetentionClientDto>> GetExpiredClientsAsync();
    Task<List<DataPurgeAuditLogDto>> GetPurgeHistoryAsync();
}
