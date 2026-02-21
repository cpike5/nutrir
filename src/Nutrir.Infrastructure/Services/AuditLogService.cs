using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AppDbContext dbContext, ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAsync(
        string userId,
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null)
    {
        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress
        };

        _dbContext.AuditLogEntries.Add(entry);
        await _dbContext.SaveChangesAsync();

        _logger.LogDebug(
            "Audit log recorded: {Action} on {EntityType} {EntityId} by {UserId}",
            action, entityType, entityId, userId);
    }
}
