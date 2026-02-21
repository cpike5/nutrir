using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IClientService
{
    Task<ClientDto> CreateAsync(ClientDto dto, string createdByUserId);

    Task<ClientDto?> GetByIdAsync(int id);

    Task<List<ClientDto>> GetListAsync(string? searchTerm = null);

    Task<bool> UpdateAsync(int id, ClientDto dto, string updatedByUserId);

    Task<bool> SoftDeleteAsync(int id, string deletedByUserId);
}
