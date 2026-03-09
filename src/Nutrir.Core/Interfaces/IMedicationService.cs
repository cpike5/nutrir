using Nutrir.Core.Entities;

namespace Nutrir.Core.Interfaces;

public interface IMedicationService
{
    Task<List<string>> SearchAsync(string query, int limit = 10);
    Task<Medication> GetOrCreateAsync(string name, string userId);
}
