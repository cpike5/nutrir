using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ConsentService : IConsentService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ConsentService> _logger;

    public ConsentService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        ILogger<ConsentService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task GrantConsentAsync(int clientId, string purpose, string policyVersion, string userId)
    {
        var client = await _dbContext.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException($"Client with ID {clientId} not found.");

        var consentEvent = new ConsentEvent
        {
            ClientId = clientId,
            EventType = ConsentEventType.ConsentGiven,
            ConsentPurpose = purpose,
            PolicyVersion = policyVersion,
            Timestamp = DateTime.UtcNow,
            RecordedByUserId = userId
        };

        _dbContext.ConsentEvents.Add(consentEvent);

        client.ConsentGiven = true;
        client.ConsentTimestamp = consentEvent.Timestamp;
        client.ConsentPolicyVersion = policyVersion;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Consent granted for client {ClientId} by {UserId}, policy version {PolicyVersion}",
            clientId, userId, policyVersion);

        await _auditLogService.LogAsync(
            userId,
            "ConsentGranted",
            "Client",
            clientId.ToString(),
            $"Consent granted for purpose '{purpose}', policy v{policyVersion}");
    }

    public async Task WithdrawConsentAsync(int clientId, string userId, string? reason = null)
    {
        var client = await _dbContext.Clients.FindAsync(clientId)
            ?? throw new InvalidOperationException($"Client with ID {clientId} not found.");

        var consentEvent = new ConsentEvent
        {
            ClientId = clientId,
            EventType = ConsentEventType.ConsentWithdrawn,
            ConsentPurpose = "Treatment and care",
            PolicyVersion = client.ConsentPolicyVersion ?? string.Empty,
            Timestamp = DateTime.UtcNow,
            RecordedByUserId = userId,
            Notes = reason
        };

        _dbContext.ConsentEvents.Add(consentEvent);

        client.ConsentGiven = false;
        client.ConsentTimestamp = consentEvent.Timestamp;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Consent withdrawn for client {ClientId} by {UserId}",
            clientId, userId);

        await _auditLogService.LogAsync(
            userId,
            "ConsentWithdrawn",
            "Client",
            clientId.ToString(),
            reason is not null ? $"Consent withdrawn. Reason: {reason}" : "Consent withdrawn");
    }
}
