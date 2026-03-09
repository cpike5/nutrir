using Nutrir.Core.Entities;

namespace Nutrir.Core.Interfaces;

public interface IConditionService
{
    Task<List<Condition>> SearchAsync(string query, int limit = 10);

    Task<Condition> GetOrCreateAsync(string name, string? icdCode = null, string? category = null);

    Task<Condition?> GetByIdAsync(int id);

    Task<Condition?> GetByNameAsync(string name);

    Task<List<Condition>> GetAllAsync();

    Task<bool> UpdateAsync(int id, string name, string? icdCode, string? category, string userId);

    Task<bool> DeleteAsync(int id, string userId);
}
