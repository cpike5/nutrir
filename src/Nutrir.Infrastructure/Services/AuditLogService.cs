using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AuditLogService : IAuditLogService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditSourceProvider _auditSourceProvider;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(IDbContextFactory<AppDbContext> dbContextFactory, IAuditSourceProvider auditSourceProvider, ILogger<AuditLogService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditSourceProvider = auditSourceProvider;
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
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entry = new AuditLogEntry
        {
            Timestamp = DateTime.UtcNow,
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details,
            IpAddress = ipAddress,
            Source = _auditSourceProvider.CurrentSource
        };

        db.AuditLogEntries.Add(entry);
        await db.SaveChangesAsync();

        _logger.LogDebug(
            "Audit log recorded: {Action} on {EntityType} {EntityId} by {UserId}",
            action, entityType, entityId, userId);
    }

    public async Task<List<AuditLogDto>> GetRecentAsync(int count = 10)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        return await db.AuditLogEntries
            .OrderByDescending(e => e.Timestamp)
            .Take(count)
            .Select(e => new AuditLogDto(
                e.Id,
                e.Timestamp,
                e.UserId,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.Details,
                e.Source))
            .ToListAsync();
    }
}
