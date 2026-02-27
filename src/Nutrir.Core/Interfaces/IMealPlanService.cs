using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IMealPlanService
{
    Task<MealPlanDetailDto?> GetByIdAsync(int id);
    Task<List<MealPlanSummaryDto>> GetListAsync(int? clientId = null, MealPlanStatus? status = null);
    Task<PagedResult<MealPlanSummaryDto>> GetPagedAsync(MealPlanListQuery query);
    Task<MealPlanDetailDto> CreateAsync(CreateMealPlanDto dto, string userId);
    Task<bool> UpdateMetadataAsync(int id, CreateMealPlanDto dto, string userId);
    Task<bool> SaveContentAsync(SaveMealPlanContentDto dto, string userId);
    Task<bool> UpdateStatusAsync(int id, MealPlanStatus newStatus, string userId);
    Task<bool> DuplicateAsync(int id, string userId);
    Task<bool> SoftDeleteAsync(int id, string userId);
    Task<List<MealPlanSummaryDto>> GetByClientAsync(int clientId, int count = 5);
    Task<int> GetActiveCountAsync();
}
