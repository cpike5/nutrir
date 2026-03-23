using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Security;

namespace Nutrir.Infrastructure.Services;

/// <summary>
/// One-time startup service that encrypts existing plaintext values in the database.
/// Runs once at startup and stops. Idempotent — already-encrypted rows are skipped.
/// </summary>
public class FieldEncryptionMigrationService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FieldEncryptionMigrationService> _logger;
    private readonly EncryptionOptions _options;

    public FieldEncryptionMigrationService(
        IServiceScopeFactory scopeFactory,
        ILogger<FieldEncryptionMigrationService> logger,
        IOptions<EncryptionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrEmpty(_options.Key))
        {
            _logger.LogInformation("Field encryption is disabled or no key configured — skipping migration");
            return;
        }

        if (AesGcmFieldEncryptor.Instance is null)
        {
            _logger.LogError("AesGcmFieldEncryptor.Instance is null — cannot run encryption migration");
            return;
        }

        // Wait for app startup to complete
        await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        _logger.LogInformation("Starting field encryption migration for existing plaintext data");

        var totalEncrypted = 0;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();

            // Client — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<Client>(
                dbFactory, "Clients", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "Client", cancellationToken);

            // Appointment — Notes, PrepNotes
            totalEncrypted += await EncryptEntityFieldsAsync<Appointment>(
                dbFactory, "Appointments", ["Notes", "PrepNotes"],
                (db, entity) =>
                {
                    db.Entry(entity).Property(e => e.Notes).IsModified = true;
                    db.Entry(entity).Property(e => e.PrepNotes).IsModified = true;
                },
                "Appointment", cancellationToken);

            // MealPlan — Description, Notes
            totalEncrypted += await EncryptEntityFieldsAsync<MealPlan>(
                dbFactory, "MealPlans", ["Description", "Notes"],
                (db, entity) =>
                {
                    db.Entry(entity).Property(e => e.Description).IsModified = true;
                    db.Entry(entity).Property(e => e.Notes).IsModified = true;
                },
                "MealPlan", cancellationToken);

            // MealPlanDay — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<MealPlanDay>(
                dbFactory, "MealPlanDays", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "MealPlanDay", cancellationToken);

            // MealSlot — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<MealSlot>(
                dbFactory, "MealSlots", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "MealSlot", cancellationToken);

            // MealItem — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<MealItem>(
                dbFactory, "MealItems", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "MealItem", cancellationToken);

            // ProgressGoal — Description
            totalEncrypted += await EncryptEntityFieldsAsync<ProgressGoal>(
                dbFactory, "ProgressGoals", ["Description"],
                (db, entity) => db.Entry(entity).Property(e => e.Description).IsModified = true,
                "ProgressGoal", cancellationToken);

            // ProgressEntry — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<ProgressEntry>(
                dbFactory, "ProgressEntries", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "ProgressEntry", cancellationToken);

            // ConsentEvent — Notes (no soft-delete)
            totalEncrypted += await EncryptEntityFieldsAsync<ConsentEvent>(
                dbFactory, "ConsentEvents", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "ConsentEvent", cancellationToken);

            // ConsentForm — Notes (no soft-delete)
            totalEncrypted += await EncryptEntityFieldsAsync<ConsentForm>(
                dbFactory, "ConsentForms", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "ConsentForm", cancellationToken);

            // ClientCondition — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<ClientCondition>(
                dbFactory, "ClientConditions", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "ClientCondition", cancellationToken);

            // ClientDietaryRestriction — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<ClientDietaryRestriction>(
                dbFactory, "ClientDietaryRestrictions", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "ClientDietaryRestriction", cancellationToken);

            // PractitionerTimeBlock — Notes
            totalEncrypted += await EncryptEntityFieldsAsync<PractitionerTimeBlock>(
                dbFactory, "PractitionerTimeBlocks", ["Notes"],
                (db, entity) => db.Entry(entity).Property(e => e.Notes).IsModified = true,
                "PractitionerTimeBlock", cancellationToken);

            // SessionNote — Notes, MeasurementsTaken, PlanAdjustments, FollowUpActions
            totalEncrypted += await EncryptEntityFieldsAsync<SessionNote>(
                dbFactory, "SessionNotes", ["Notes", "MeasurementsTaken", "PlanAdjustments", "FollowUpActions"],
                (db, entity) =>
                {
                    db.Entry(entity).Property(e => e.Notes).IsModified = true;
                    db.Entry(entity).Property(e => e.MeasurementsTaken).IsModified = true;
                    db.Entry(entity).Property(e => e.PlanAdjustments).IsModified = true;
                    db.Entry(entity).Property(e => e.FollowUpActions).IsModified = true;
                },
                "SessionNote", cancellationToken);

            _logger.LogInformation("Field encryption migration complete. Total rows encrypted: {TotalRows}", totalEncrypted);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Field encryption migration was cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Field encryption migration failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Finds rows with plaintext values (not yet encrypted) using raw SQL,
    /// then loads them via EF Core and marks encrypted fields as modified so
    /// the value converter encrypts them on save.
    /// </summary>
    private async Task<int> EncryptEntityFieldsAsync<TEntity>(
        IDbContextFactory<AppDbContext> dbFactory,
        string tableName,
        string[] fieldNames,
        Action<AppDbContext, TEntity> markFieldsModified,
        string entityName,
        CancellationToken ct) where TEntity : class
    {
        // Build WHERE clause: any field is non-null, non-empty, and not already encrypted
        var conditions = fieldNames.Select(f =>
            $"(\"{f}\" IS NOT NULL AND \"{f}\" != '' AND \"{f}\" NOT LIKE 'v%:%')");
        var whereClause = string.Join(" OR ", conditions);
        var sql = $"SELECT \"Id\" FROM \"{tableName}\" WHERE {whereClause}";

        // Use a separate context for the raw SQL query to get IDs
        await using var queryDb = await dbFactory.CreateDbContextAsync(ct);
        var ids = await queryDb.Database
            .SqlQueryRaw<int>(sql)
            .ToListAsync(ct);

        if (ids.Count == 0)
        {
            _logger.LogInformation("No plaintext {EntityName} records found — skipping", entityName);
            return 0;
        }

        _logger.LogInformation("Found {Count} plaintext {EntityName} records to encrypt", ids.Count, entityName);

        var encrypted = 0;

        foreach (var batch in ids.Chunk(100))
        {
            // Use a fresh context per batch to avoid tracking overhead
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var entities = await db.Set<TEntity>()
                .IgnoreQueryFilters()
                .Where(e => batch.Contains(EF.Property<int>(e, "Id")))
                .ToListAsync(ct);

            foreach (var entity in entities)
            {
                markFieldsModified(db, entity);
            }

            await db.SaveChangesAsync(ct);
            encrypted += entities.Count;

            _logger.LogInformation(
                "Encrypted {Count}/{Total} {EntityName} records",
                encrypted, ids.Count, entityName);
        }

        return encrypted;
    }
}
