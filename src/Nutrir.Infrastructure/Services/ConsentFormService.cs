using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ConsentFormService : IConsentFormService
{
    private readonly AppDbContext _dbContext;
    private readonly IConsentFormTemplate _template;
    private readonly IConsentService _consentService;
    private readonly IAuditLogService _auditLogService;
    private readonly ConsentFormOptions _options;
    private readonly ILogger<ConsentFormService> _logger;

    public ConsentFormService(
        AppDbContext dbContext,
        IConsentFormTemplate template,
        IConsentService consentService,
        IAuditLogService auditLogService,
        IOptions<ConsentFormOptions> options,
        ILogger<ConsentFormService> logger)
    {
        _dbContext = dbContext;
        _template = template;
        _consentService = consentService;
        _auditLogService = auditLogService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> GeneratePdfAsync(int clientId, string userId)
    {
        var (client, practitionerName) = await GetClientAndPractitionerAsync(clientId);
        var clientName = $"{client.FirstName} {client.LastName}";
        var content = _template.Generate(clientName, practitionerName, DateTime.UtcNow);

        var pdfBytes = ConsentFormPdfRenderer.Render(content);

        await EnsureFormRecordExistsAsync(clientId, userId);

        await _auditLogService.LogAsync(
            userId, "ConsentFormPdfGenerated", "Client", clientId.ToString(),
            $"PDF consent form generated, version {_template.Version}");

        _logger.LogInformation(
            "PDF consent form generated for client {ClientId} by {UserId}",
            clientId, userId);

        return pdfBytes;
    }

    public async Task<byte[]> GenerateDocxAsync(int clientId, string userId)
    {
        var (client, practitionerName) = await GetClientAndPractitionerAsync(clientId);
        var clientName = $"{client.FirstName} {client.LastName}";
        var content = _template.Generate(clientName, practitionerName, DateTime.UtcNow);

        var docxBytes = ConsentFormDocxRenderer.Render(content, _options.DocxTemplatePath);

        await EnsureFormRecordExistsAsync(clientId, userId);

        await _auditLogService.LogAsync(
            userId, "ConsentFormDocxGenerated", "Client", clientId.ToString(),
            $"DOCX consent form generated, version {_template.Version}");

        _logger.LogInformation(
            "DOCX consent form generated for client {ClientId} by {UserId}",
            clientId, userId);

        return docxBytes;
    }

    public byte[] GeneratePreviewPdf(string clientName, string practitionerName)
    {
        var content = _template.Generate(clientName, practitionerName, DateTime.UtcNow);
        return ConsentFormPdfRenderer.Render(content);
    }

    public byte[] GeneratePreviewDocx(string clientName, string practitionerName)
    {
        var content = _template.Generate(clientName, practitionerName, DateTime.UtcNow);
        return ConsentFormDocxRenderer.Render(content, _options.DocxTemplatePath);
    }

    public async Task<ConsentFormDto> RecordDigitalSignatureAsync(int clientId, string userId)
    {
        var form = await GetOrCreateFormAsync(clientId, userId, ConsentSignatureMethod.Digital);

        form.IsSigned = true;
        form.SignedAt = DateTime.UtcNow;
        form.SignedByUserId = userId;

        await _dbContext.SaveChangesAsync();

        // Also grant consent via the existing consent service for backward compat
        await _consentService.GrantConsentAsync(
            clientId, "Treatment and care", _template.Version, userId);

        await _auditLogService.LogAsync(
            userId, "ConsentFormDigitallySigned", "Client", clientId.ToString(),
            $"Digital consent recorded, form version {form.FormVersion}");

        _logger.LogInformation(
            "Digital consent recorded for client {ClientId} by {UserId}",
            clientId, userId);

        return ToDto(form);
    }

    public async Task<ConsentFormDto> MarkPhysicallySignedAsync(int clientId, string userId, string? notes = null)
    {
        var form = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId && f.SignatureMethod == ConsentSignatureMethod.Physical)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        if (form is null)
        {
            form = await GetOrCreateFormAsync(clientId, userId, ConsentSignatureMethod.Physical);
        }

        form.IsSigned = true;
        form.SignedAt = DateTime.UtcNow;
        form.SignedByUserId = userId;
        form.Notes = notes;

        await _dbContext.SaveChangesAsync();

        // Also grant consent via the existing consent service
        await _consentService.GrantConsentAsync(
            clientId, "Treatment and care", _template.Version, userId);

        await _auditLogService.LogAsync(
            userId, "ConsentFormPhysicallySigned", "Client", clientId.ToString(),
            $"Physical consent marked as signed, form version {form.FormVersion}");

        _logger.LogInformation(
            "Physical consent marked as signed for client {ClientId} by {UserId}",
            clientId, userId);

        return ToDto(form);
    }

    public async Task<ConsentFormDto?> UploadScannedCopyAsync(int clientId, Stream stream, string fileName, string userId)
    {
        var form = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        if (form is null) return null;

        // Ensure storage directory exists
        var storagePath = _options.ScannedCopyStoragePath;
        Directory.CreateDirectory(storagePath);

        var safeFileName = $"{clientId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(fileName)}";
        var filePath = Path.Combine(storagePath, safeFileName);

        await using (var fileStream = File.Create(filePath))
        {
            await stream.CopyToAsync(fileStream);
        }

        form.ScannedCopyPath = Path.Combine(storagePath, safeFileName);

        await _dbContext.SaveChangesAsync();

        await _auditLogService.LogAsync(
            userId, "ConsentFormScanUploaded", "Client", clientId.ToString(),
            $"Scanned consent form uploaded: {safeFileName}");

        _logger.LogInformation(
            "Scanned consent form uploaded for client {ClientId} by {UserId}: {FileName}",
            clientId, userId, safeFileName);

        return ToDto(form);
    }

    public async Task<ConsentFormDto?> GetLatestFormAsync(int clientId)
    {
        var form = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        return form is null ? null : ToDto(form);
    }

    public async Task<List<ConsentFormDto>> GetFormsForClientAsync(int clientId)
    {
        var forms = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId)
            .OrderByDescending(f => f.GeneratedAt)
            .ToListAsync();

        return forms.Select(ToDto).ToList();
    }

    private async Task<(Client Client, string PractitionerName)> GetClientAndPractitionerAsync(int clientId)
    {
        var client = await _dbContext.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException($"Client with ID {clientId} not found.");

        var practitioner = await _dbContext.Users.FindAsync(client.PrimaryNutritionistId);
        var practitionerName = practitioner is not null
            ? $"{practitioner.FirstName} {practitioner.LastName}".Trim()
            : "Unknown";

        return (client, practitionerName);
    }

    private async Task<ConsentForm> GetOrCreateFormAsync(int clientId, string userId, ConsentSignatureMethod method)
    {
        var existing = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId && f.SignatureMethod == method && !f.IsSigned)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        if (existing is not null) return existing;

        var form = new ConsentForm
        {
            ClientId = clientId,
            FormVersion = _template.Version,
            GeneratedAt = DateTime.UtcNow,
            GeneratedByUserId = userId,
            SignatureMethod = method,
            IsSigned = false
        };

        _dbContext.Set<ConsentForm>().Add(form);
        await _dbContext.SaveChangesAsync();

        return form;
    }

    private async Task EnsureFormRecordExistsAsync(int clientId, string userId)
    {
        var existingForm = await _dbContext.Set<ConsentForm>()
            .Where(f => f.ClientId == clientId)
            .OrderByDescending(f => f.GeneratedAt)
            .FirstOrDefaultAsync();

        if (existingForm is not null) return;

        var form = new ConsentForm
        {
            ClientId = clientId,
            FormVersion = _template.Version,
            GeneratedAt = DateTime.UtcNow,
            GeneratedByUserId = userId,
            SignatureMethod = ConsentSignatureMethod.Physical,
            IsSigned = false
        };

        _dbContext.Set<ConsentForm>().Add(form);
        await _dbContext.SaveChangesAsync();
    }

    private static ConsentFormDto ToDto(ConsentForm form) => new(
        Id: form.Id,
        ClientId: form.ClientId,
        FormVersion: form.FormVersion,
        GeneratedAt: form.GeneratedAt,
        GeneratedByUserId: form.GeneratedByUserId,
        SignatureMethod: form.SignatureMethod,
        IsSigned: form.IsSigned,
        SignedAt: form.SignedAt,
        SignedByUserId: form.SignedByUserId,
        ScannedCopyPath: form.ScannedCopyPath,
        Notes: form.Notes);
}
