using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IAllergenService
{
    Task<List<AllergenDto>> SearchAsync(string query, int limit = 10);
    Task<AllergenDto> GetOrCreateAsync(string name, string? category, string userId);
}
