using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IFoodService
{
    Task<List<FoodSearchResultDto>> SearchAsync(string query, int limit = 15);
    Task<FoodDto?> GetByIdAsync(int id);
    Task<List<FoodDto>> GetAllAsync();
    Task<FoodDto> CreateAsync(CreateFoodDto dto, string userId);
    Task<bool> UpdateAsync(int id, UpdateFoodDto dto, string userId);
    Task<bool> SoftDeleteAsync(int id, string userId);
}
