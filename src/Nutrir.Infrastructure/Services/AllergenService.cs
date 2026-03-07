using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AllergenService : IAllergenService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AllergenService> _logger;

    public AllergenService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<AllergenService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<AllergenDto>> SearchAsync(string query, int limit = 10)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var pattern = $"%{query.Trim()}%";

        var results = await db.Allergens
            .Where(a => EF.Functions.ILike(a.Name, pattern))
            .OrderBy(a => a.Name)
            .Take(limit)
            .Select(a => new AllergenDto(a.Id, a.Name, a.Category))
            .ToListAsync();

        return results;
    }

    public async Task<AllergenDto> GetOrCreateAsync(string name, string? category, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var trimmedName = name.Trim();

        // Use IgnoreQueryFilters so soft-deleted allergens are found (prevents unique index violations)
        var existing = await db.Allergens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => EF.Functions.ILike(a.Name, trimmedName));

        if (existing is not null)
        {
            // Reactivate if soft-deleted
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            return new AllergenDto(existing.Id, existing.Name, existing.Category);
        }

        var entity = new Allergen
        {
            Name = trimmedName,
            Category = category,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            db.Allergens.Add(entity);
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Lost the race — another request inserted the same name concurrently
            db.ChangeTracker.Clear();
            var concurrent = await db.Allergens
                .IgnoreQueryFilters()
                .FirstAsync(a => EF.Functions.ILike(a.Name, trimmedName));
            return new AllergenDto(concurrent.Id, concurrent.Name, concurrent.Category);
        }

        _logger.LogInformation("Allergen auto-created: {AllergenId} '{AllergenName}' by {UserId}",
            entity.Id, entity.Name, userId);

        await _auditLogService.LogAsync(userId, "AllergenCreated", "Allergen",
            entity.Id.ToString(), $"Auto-created allergen '{entity.Name}'");

        return new AllergenDto(entity.Id, entity.Name, entity.Category);
    }
}
