using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class DataPurgeService : IDataPurgeService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<DataPurgeService> _logger;

    public DataPurgeService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<DataPurgeService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<RetentionClientDto>> GetExpiringClientsAsync(int withinDays = 90)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var cutoff = DateTime.UtcNow.AddDays(withinDays);

        var clients = await db.Clients
            .Where(c => c.RetentionExpiresAt != null
                        && c.RetentionExpiresAt <= cutoff
                        && c.RetentionExpiresAt > DateTime.UtcNow
                        && !c.IsPurged
                        && !c.IsDeleted)
            .OrderBy(c => c.RetentionExpiresAt)
            .Select(c => new RetentionClientDto(
                c.Id,
                c.FirstName,
                c.LastName,
                c.LastInteractionDate,
                c.RetentionExpiresAt,
                c.RetentionYears,
                (int)((c.RetentionExpiresAt!.Value - DateTime.UtcNow).TotalDays)))
            .ToListAsync();

        return clients;
    }

    public async Task<List<RetentionClientDto>> GetExpiredClientsAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var clients = await db.Clients
            .Where(c => c.RetentionExpiresAt != null
                        && c.RetentionExpiresAt < DateTime.UtcNow
                        && !c.IsPurged
                        && !c.IsDeleted)
            .OrderBy(c => c.RetentionExpiresAt)
            .Select(c => new RetentionClientDto(
                c.Id,
                c.FirstName,
                c.LastName,
                c.LastInteractionDate,
                c.RetentionExpiresAt,
                c.RetentionYears,
                (int)((c.RetentionExpiresAt!.Value - DateTime.UtcNow).TotalDays)))
            .ToListAsync();

        return clients;
    }

    public async Task<List<DataPurgeAuditLogDto>> GetPurgeHistoryAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var logs = await db.DataPurgeAuditLogs
            .OrderByDescending(l => l.PurgedAt)
            .ToListAsync();

        var userIds = logs.Select(l => l.PurgedByUserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        return logs.Select(l => new DataPurgeAuditLogDto(
            l.Id,
            l.PurgedAt,
            l.PurgedByUserId,
            users.GetValueOrDefault(l.PurgedByUserId, "Unknown"),
            l.ClientId,
            l.ClientIdentifier,
            l.PurgedEntities,
            l.Justification
        )).ToList();
    }

    public async Task<PurgeSummaryDto?> GetPurgeSummaryAsync(int clientId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var client = await db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client is null)
        {
            return null;
        }

        var appointmentCount = await db.Appointments
            .IgnoreQueryFilters()
            .CountAsync(a => a.ClientId == clientId);

        var mealPlanCount = await db.MealPlans
            .IgnoreQueryFilters()
            .CountAsync(mp => mp.ClientId == clientId);

        var progressEntryCount = await db.ProgressEntries
            .IgnoreQueryFilters()
            .CountAsync(pe => pe.ClientId == clientId);

        var progressGoalCount = await db.ProgressGoals
            .IgnoreQueryFilters()
            .CountAsync(pg => pg.ClientId == clientId);

        var consentEventCount = await db.ConsentEvents
            .CountAsync(ce => ce.ClientId == clientId);

        var allergyCount = await db.ClientAllergies
            .IgnoreQueryFilters()
            .CountAsync(ca => ca.ClientId == clientId);

        var medicationCount = await db.ClientMedications
            .IgnoreQueryFilters()
            .CountAsync(cm => cm.ClientId == clientId);

        var conditionCount = await db.ClientConditions
            .IgnoreQueryFilters()
            .CountAsync(cc => cc.ClientId == clientId);

        var dietaryRestrictionCount = await db.ClientDietaryRestrictions
            .IgnoreQueryFilters()
            .CountAsync(cdr => cdr.ClientId == clientId);

        var healthProfileItemCount = allergyCount + medicationCount + conditionCount + dietaryRestrictionCount;

        var intakeFormCount = await db.IntakeForms
            .IgnoreQueryFilters()
            .CountAsync(f => f.ClientId == clientId);

        var sessionNoteCount = await db.SessionNotes
            .IgnoreQueryFilters()
            .CountAsync(sn => sn.ClientId == clientId);

        return new PurgeSummaryDto(
            client.Id,
            $"{client.FirstName} {client.LastName}",
            client.LastInteractionDate,
            client.RetentionExpiresAt,
            appointmentCount,
            mealPlanCount,
            progressEntryCount,
            progressGoalCount,
            consentEventCount,
            healthProfileItemCount,
            intakeFormCount,
            sessionNoteCount);
    }

    public async Task<DataPurgeResult> ExecutePurgeAsync(int clientId, string confirmation, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var client = await db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == clientId);

        if (client is null)
        {
            _logger.LogWarning("Purge attempted for non-existent client {ClientId} by {UserId}", clientId, userId);
            return new DataPurgeResult(false, "Client not found");
        }

        var expectedConfirmation = $"PURGE {client.FirstName} {client.LastName}";
        if (confirmation != expectedConfirmation)
        {
            _logger.LogWarning(
                "Purge confirmation mismatch for client {ClientId} by {UserId}. Expected: {Expected}, Got: {Actual}",
                clientId, userId, expectedConfirmation, confirmation);
            return new DataPurgeResult(false, "Confirmation text does not match");
        }

        var summary = await GetPurgeSummaryAsync(clientId);

        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            var clientIdentifier = $"Client #{clientId} - {client.FirstName[0]}.{client.LastName[0]}.";

            var purgedEntities = JsonSerializer.Serialize(new
            {
                Appointments = summary!.AppointmentCount,
                MealPlans = summary.MealPlanCount,
                ProgressEntries = summary.ProgressEntryCount,
                ProgressGoals = summary.ProgressGoalCount,
                ConsentEvents = summary.ConsentEventCount,
                HealthProfileItems = summary.HealthProfileItemCount,
                IntakeForms = summary.IntakeFormCount,
                SessionNotes = summary.SessionNoteCount
            });

            var auditLog = new DataPurgeAuditLog
            {
                PurgedAt = DateTime.UtcNow,
                PurgedByUserId = userId,
                ClientId = clientId,
                ClientIdentifier = clientIdentifier,
                PurgedEntities = purgedEntities,
                Justification = confirmation
            };
            db.DataPurgeAuditLogs.Add(auditLog);

            // Anonymize client
            client.FirstName = "Purged";
            client.LastName = "Client";
            client.Email = null;
            client.Phone = null;
            client.DateOfBirth = null;
            client.Notes = null;
            client.IsPurged = true;
            client.UpdatedAt = DateTime.UtcNow;

            // Null out notes on Appointments
            var appointments = await db.Appointments
                .IgnoreQueryFilters()
                .Where(a => a.ClientId == clientId)
                .ToListAsync();

            foreach (var appointment in appointments)
            {
                appointment.Notes = null;
                appointment.PrepNotes = null;
            }

            // Null out notes on MealPlans
            var mealPlans = await db.MealPlans
                .IgnoreQueryFilters()
                .Where(mp => mp.ClientId == clientId)
                .ToListAsync();

            foreach (var mealPlan in mealPlans)
            {
                mealPlan.Notes = null;
                mealPlan.Description = null;
            }

            // Null out notes on ProgressEntries
            var progressEntries = await db.ProgressEntries
                .IgnoreQueryFilters()
                .Where(pe => pe.ClientId == clientId)
                .ToListAsync();

            foreach (var entry in progressEntries)
            {
                entry.Notes = null;
            }

            // Soft-delete health profile items
            var now = DateTime.UtcNow;

            var allergies = await db.ClientAllergies
                .IgnoreQueryFilters()
                .Where(ca => ca.ClientId == clientId)
                .ToListAsync();

            foreach (var allergy in allergies)
            {
                allergy.IsDeleted = true;
                allergy.DeletedAt = now;
                allergy.DeletedBy = userId;
            }

            var medications = await db.ClientMedications
                .IgnoreQueryFilters()
                .Where(cm => cm.ClientId == clientId)
                .ToListAsync();

            foreach (var medication in medications)
            {
                medication.IsDeleted = true;
                medication.DeletedAt = now;
                medication.DeletedBy = userId;
            }

            var conditions = await db.ClientConditions
                .IgnoreQueryFilters()
                .Where(cc => cc.ClientId == clientId)
                .ToListAsync();

            foreach (var condition in conditions)
            {
                condition.IsDeleted = true;
                condition.DeletedAt = now;
                condition.DeletedBy = userId;
            }

            var dietaryRestrictions = await db.ClientDietaryRestrictions
                .IgnoreQueryFilters()
                .Where(cdr => cdr.ClientId == clientId)
                .ToListAsync();

            foreach (var restriction in dietaryRestrictions)
            {
                restriction.IsDeleted = true;
                restriction.DeletedAt = now;
                restriction.DeletedBy = userId;
            }

            // Null out notes on ConsentEvents (preserve event type and timestamp)
            var consentEvents = await db.ConsentEvents
                .Where(ce => ce.ClientId == clientId)
                .ToListAsync();

            foreach (var consentEvent in consentEvents)
            {
                consentEvent.Notes = null;
            }

            // Null out notes on SessionNotes
            var sessionNotes = await db.SessionNotes
                .IgnoreQueryFilters()
                .Where(sn => sn.ClientId == clientId)
                .ToListAsync();

            foreach (var sessionNote in sessionNotes)
            {
                sessionNote.Notes = null;
                sessionNote.MeasurementsTaken = null;
                sessionNote.PlanAdjustments = null;
                sessionNote.FollowUpActions = null;
            }

            await db.SaveChangesAsync();
            await transaction.CommitAsync();

            _logger.LogInformation(
                "Client data purged: {ClientId} by {UserId}. Entities: {PurgedEntities}",
                clientId, userId, purgedEntities);

            await _auditLogService.LogAsync(
                userId,
                "ClientDataPurged",
                "Client",
                clientId.ToString(),
                $"Purged all personal data for {clientIdentifier}");

            return new DataPurgeResult(true);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            _logger.LogError(ex,
                "Failed to purge client data for {ClientId} by {UserId}",
                clientId, userId);

            return new DataPurgeResult(false, "An error occurred during the purge operation");
        }
    }
}
