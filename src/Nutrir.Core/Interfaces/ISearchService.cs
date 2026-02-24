using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface ISearchService
{
    Task<SearchResultDto> SearchAsync(string query, string userId, int maxPerGroup = 3);
}
