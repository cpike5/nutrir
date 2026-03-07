using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class MedicationService : IMedicationService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<MedicationService> _logger;

    public MedicationService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<MedicationService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<string>> SearchAsync(string query, int limit = 10)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var medications = await db.Medications
            .Where(m => string.IsNullOrEmpty(query) || EF.Functions.ILike(m.Name, $"%{query}%"))
            .OrderBy(m => m.Name)
            .Take(limit)
            .Select(m => m.Name)
            .ToListAsync();

        return medications;
    }

    public async Task<Medication> GetOrCreateAsync(string name, string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        name = name.Trim();

        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var existing = await db.Medications
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => EF.Functions.ILike(m.Name, name));

        if (existing is not null)
        {
            if (existing.IsDeleted)
            {
                existing.IsDeleted = false;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return existing;
        }

        var medication = new Medication
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        db.Medications.Add(medication);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent insert won the race — fetch the winner
            await using var retryDb = await _dbContextFactory.CreateDbContextAsync();
            return await retryDb.Medications
                .IgnoreQueryFilters()
                .FirstAsync(m => EF.Functions.ILike(m.Name, name));
        }

        _logger.LogInformation("Created medication lookup entry: {MedicationName}", medication.Name);

        await _auditLogService.LogAsync(userId, "MedicationCreated", "Medication",
            medication.Id.ToString(), $"Created medication lookup '{medication.Name}'");

        return medication;
    }
}
