using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IProgressService
{
    // Entries
    Task<ProgressEntryDetailDto?> GetEntryByIdAsync(int id);
    Task<List<ProgressEntrySummaryDto>> GetEntriesByClientAsync(int clientId);
    Task<ProgressEntryDetailDto> CreateEntryAsync(CreateProgressEntryDto dto, string userId);
    Task<bool> UpdateEntryAsync(int id, UpdateProgressEntryDto dto, string userId);
    Task<bool> SoftDeleteEntryAsync(int id, string userId);

    // Goals
    Task<ProgressGoalDetailDto?> GetGoalByIdAsync(int id);
    Task<List<ProgressGoalSummaryDto>> GetGoalsByClientAsync(int clientId);
    Task<ProgressGoalDetailDto> CreateGoalAsync(CreateProgressGoalDto dto, string userId);
    Task<bool> UpdateGoalAsync(int id, UpdateProgressGoalDto dto, string userId);
    Task<bool> UpdateGoalStatusAsync(int id, GoalStatus newStatus, string userId);
    Task<bool> SoftDeleteGoalAsync(int id, string userId);

    // Chart
    Task<ProgressChartDataDto?> GetChartDataAsync(int clientId, MetricType metricType);

    // Dashboard
    Task<List<ProgressEntrySummaryDto>> GetRecentByClientAsync(int clientId, int count = 3);
}
