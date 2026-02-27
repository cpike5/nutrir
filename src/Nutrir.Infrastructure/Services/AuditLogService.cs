using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
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

    public async Task<AuditLogPageResult> QueryAsync(AuditLogQueryRequest request)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.AuditLogEntries.AsQueryable();

        if (request.From.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(request.From.Value.Date, DateTimeKind.Utc);
            query = query.Where(e => e.Timestamp >= fromUtc);
        }

        if (request.To.HasValue)
        {
            var endOfDayUtc = DateTime.SpecifyKind(request.To.Value.Date.AddDays(1), DateTimeKind.Utc);
            query = query.Where(e => e.Timestamp < endOfDayUtc);
        }

        if (!string.IsNullOrWhiteSpace(request.Action))
            query = query.Where(e => e.Action == request.Action);

        if (!string.IsNullOrWhiteSpace(request.EntityType))
            query = query.Where(e => e.EntityType == request.EntityType);

        if (request.Source.HasValue)
            query = query.Where(e => e.Source == request.Source.Value);

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            var term = request.SearchTerm.ToLower();
            query = query.Where(e =>
                e.UserId.ToLower().Contains(term) ||
                e.Action.ToLower().Contains(term) ||
                e.EntityType.ToLower().Contains(term) ||
                (e.Details != null && e.Details.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();

        var entries = await query
            .OrderByDescending(e => e.Timestamp)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync();

        // Batch-resolve user display names
        var userIds = entries.Select(e => e.UserId).Distinct().ToList();
        var userNames = await db.Set<ApplicationUser>()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName })
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        var items = entries.Select(e => new AuditLogViewDto(
            e.Id,
            e.Timestamp,
            e.UserId,
            userNames.GetValueOrDefault(e.UserId, e.UserId),
            e.Action,
            e.EntityType,
            e.EntityId,
            e.Details,
            e.IpAddress,
            e.Source)).ToList();

        return new AuditLogPageResult(items, totalCount, request.Page, request.PageSize);
    }

    public async Task<List<string>> GetDistinctActionsAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.AuditLogEntries
            .Select(e => e.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync();
    }

    public async Task<List<string>> GetDistinctEntityTypesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.AuditLogEntries
            .Select(e => e.EntityType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync();
    }
}
