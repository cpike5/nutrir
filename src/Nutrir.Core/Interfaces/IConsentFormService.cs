using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IConsentFormService
{
    Task<byte[]> GeneratePdfAsync(int clientId, string userId);

    Task<byte[]> GenerateDocxAsync(int clientId, string userId);

    byte[] GeneratePreviewPdf(string clientName, string practitionerName);

    byte[] GeneratePreviewDocx(string clientName, string practitionerName);

    Task<ConsentFormDto> RecordDigitalSignatureAsync(int clientId, string userId);

    Task<ConsentFormDto> MarkPhysicallySignedAsync(int clientId, string userId, string? notes = null);

    Task<ConsentFormDto?> UploadScannedCopyAsync(int clientId, Stream stream, string fileName, string userId);

    Task<ConsentFormDto?> GetLatestFormAsync(int clientId);

    Task<List<ConsentFormDto>> GetFormsForClientAsync(int clientId);
}
