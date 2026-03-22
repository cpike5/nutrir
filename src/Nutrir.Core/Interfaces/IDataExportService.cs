namespace Nutrir.Core.Interfaces;

using Nutrir.Core.DTOs;

public interface IDataExportService
{
    Task<ClientDataExportDto> CollectClientDataAsync(int clientId, string userId, string format = "json");
    Task<byte[]> ExportAsJsonAsync(int clientId, string userId);
    Task<byte[]> ExportAsPdfAsync(int clientId, string userId);
}
