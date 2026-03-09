using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ConditionService : IConditionService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ConditionService> _logger;

    public ConditionService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<ConditionService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<Condition>> SearchAsync(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Conditions
            .Where(c => EF.Functions.ILike(c.Name, $"%{query}%"))
            .OrderBy(c => c.Name)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<Condition> GetOrCreateAsync(string name, string? icdCode = null, string? category = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Use IgnoreQueryFilters to find soft-deleted conditions too
        var existing = await db.Conditions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, name));

        if (existing is not null)
        {
            // Restore if soft-deleted
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                _logger.LogInformation("Restored soft-deleted condition: {ConditionId} '{Name}'", existing.Id, existing.Name);
            }
            return existing;
        }

        var condition = new Condition
        {
            Name = name.Trim(),
            IcdCode = icdCode,
            Category = category,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            db.Conditions.Add(condition);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
        {
            // Another request won the race; return that record
            db.ChangeTracker.Clear();
            return await db.Conditions.FirstAsync(c => EF.Functions.ILike(c.Name, name));
        }

        _logger.LogInformation("New condition created in lookup table: {ConditionId} '{Name}'", condition.Id, condition.Name);

        await _auditLogService.LogAsync("system", "ConditionLookupCreated", "Condition",
            condition.Id.ToString(), $"Auto-created condition '{condition.Name}' in lookup table");

        return condition;
    }

    public async Task<Condition?> GetByIdAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Conditions.FindAsync(id);
    }

    public async Task<Condition?> GetByNameAsync(string name)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Conditions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => EF.Functions.ILike(c.Name, name));
    }

    public async Task<List<Condition>> GetAllAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Conditions.OrderBy(c => c.Name).ToListAsync();
    }

    public async Task<bool> UpdateAsync(int id, string name, string? icdCode, string? category, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var entity = await db.Conditions.FindAsync(id);
        if (entity is null) return false;

        entity.Name = name.Trim();
        entity.IcdCode = icdCode;
        entity.Category = category;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Condition updated: {ConditionId} by {UserId}", id, userId);
        await _auditLogService.LogAsync(userId, "ConditionLookupUpdated", "Condition",
            id.ToString(), $"Updated condition '{entity.Name}'");

        return true;
    }

    public async Task<bool> DeleteAsync(int id, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var entity = await db.Conditions.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        _logger.LogInformation("Condition soft-deleted: {ConditionId} by {UserId}", id, userId);
        await _auditLogService.LogAsync(userId, "ConditionLookupDeleted", "Condition",
            id.ToString(), $"Soft-deleted condition '{entity.Name}'");

        return true;
    }
}
