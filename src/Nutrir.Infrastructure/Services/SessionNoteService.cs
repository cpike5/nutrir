using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Exceptions;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class SessionNoteService : ISessionNoteService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<SessionNoteService> _logger;

    public SessionNoteService(
        AppDbContext dbContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        INotificationDispatcher notificationDispatcher,
        ILogger<SessionNoteService> logger)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    public async Task<SessionNoteDto?> GetByIdAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.SessionNotes.FirstOrDefaultAsync(sn => sn.Id == id);
        if (entity is null) return null;

        return await MapToDtoAsync(entity, db);
    }

    public async Task<SessionNoteDto?> GetByAppointmentIdAsync(int appointmentId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.SessionNotes.FirstOrDefaultAsync(sn => sn.AppointmentId == appointmentId);
        if (entity is null) return null;

        return await MapToDtoAsync(entity, db);
    }

    public async Task<List<SessionNoteSummaryDto>> GetByClientAsync(int clientId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entities = await db.SessionNotes
            .Where(sn => sn.ClientId == clientId)
            .OrderByDescending(sn => sn.CreatedAt)
            .ToListAsync();

        return await MapToSummaryListAsync(entities, db);
    }

    public async Task<SessionNoteDto> CreateDraftAsync(int appointmentId, int clientId, string userId)
    {
        // Check if a session note already exists for this appointment
        var existing = await _dbContext.SessionNotes.FirstOrDefaultAsync(sn => sn.AppointmentId == appointmentId);
        if (existing is not null)
        {
            _logger.LogInformation("Draft session note already exists for appointment {AppointmentId}", appointmentId);
            return (await MapToDtoAsync(existing, _dbContext))!;
        }

        var entity = new SessionNote
        {
            AppointmentId = appointmentId,
            ClientId = clientId,
            CreatedByUserId = userId,
            IsDraft = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.SessionNotes.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Draft session note created: {SessionNoteId} for appointment {AppointmentId} by {UserId}",
            entity.Id, appointmentId, userId);

        await _auditLogService.LogAsync(
            userId,
            "SessionNoteCreated",
            "SessionNote",
            entity.Id.ToString(),
            $"Created draft session note for appointment {appointmentId}");

        await TryDispatchAsync("SessionNote", entity.Id, EntityChangeType.Created, userId, entity.ClientId);

        return (await MapToDtoAsync(entity, _dbContext))!;
    }

    public async Task<bool> UpdateAsync(int id, UpdateSessionNoteDto dto, string userId)
    {
        var entity = await _dbContext.SessionNotes.FindAsync(id);
        if (entity is null) return false;

        entity.SessionType = dto.SessionType;
        entity.Notes = dto.Notes;
        entity.AdherenceScore = dto.AdherenceScore;
        entity.PractitionerAssessment = dto.PractitionerAssessment;
        entity.ContextualFactors = dto.ContextualFactors;
        entity.MeasurementsTaken = dto.MeasurementsTaken;
        entity.PlanAdjustments = dto.PlanAdjustments;
        entity.FollowUpActions = dto.FollowUpActions;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrentEditException("SessionNote", id);
        }

        _logger.LogInformation("Session note updated: {SessionNoteId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "SessionNoteUpdated",
            "SessionNote",
            id.ToString(),
            $"Updated session note {id}");

        await TryDispatchAsync("SessionNote", id, EntityChangeType.Updated, userId, entity.ClientId);

        return true;
    }

    public async Task<bool> FinalizeAsync(int id, string userId)
    {
        var entity = await _dbContext.SessionNotes.FindAsync(id);
        if (entity is null) return false;

        entity.IsDraft = false;
        entity.UpdatedAt = DateTime.UtcNow;

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrentEditException("SessionNote", id);
        }

        _logger.LogInformation("Session note finalized: {SessionNoteId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "SessionNoteFinalized",
            "SessionNote",
            id.ToString(),
            $"Finalized session note {id}");

        await TryDispatchAsync("SessionNote", id, EntityChangeType.Updated, userId, entity.ClientId);

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id, string userId)
    {
        var entity = await _dbContext.SessionNotes.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Session note soft-deleted: {SessionNoteId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "SessionNoteSoftDeleted",
            "SessionNote",
            id.ToString(),
            $"Soft-deleted session note {id}");

        await TryDispatchAsync("SessionNote", id, EntityChangeType.Deleted, userId, entity.ClientId);

        return true;
    }

    public async Task<List<SessionNoteSummaryDto>> GetMissingNotesAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Find completed appointments that have no session note
        var completedWithoutNotes = await db.Appointments
            .Where(a => a.Status == AppointmentStatus.Completed)
            .Where(a => !db.SessionNotes.Any(sn => sn.AppointmentId == a.Id))
            .OrderByDescending(a => a.StartTime)
            .Select(a => new { a.Id, a.ClientId, a.StartTime })
            .ToListAsync();

        if (completedWithoutNotes.Count == 0) return [];

        // Resolve client names
        var clientIds = completedWithoutNotes.Select(a => a.ClientId).Distinct().ToList();
        var clients = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        return completedWithoutNotes.Select(a =>
        {
            var client = clients.GetValueOrDefault(a.ClientId);
            return new SessionNoteSummaryDto(
                0, // No session note ID
                a.Id,
                a.ClientId,
                client?.FirstName ?? "",
                client?.LastName ?? "",
                true,
                null, // SessionType
                null, // AdherenceScore
                a.StartTime,
                DateTime.MinValue);
        }).ToList();
    }

    private async Task<SessionNoteDto?> MapToDtoAsync(SessionNote entity, AppDbContext db)
    {
        var client = await db.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == entity.ClientId);

        var appointment = await db.Appointments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == entity.AppointmentId);

        var user = await db.Users
            .OfType<ApplicationUser>()
            .FirstOrDefaultAsync(u => u.Id == entity.CreatedByUserId);

        var createdByName = user is not null
            ? (!string.IsNullOrEmpty(user.DisplayName) ? user.DisplayName : $"{user.FirstName} {user.LastName}".Trim())
            : null;

        return new SessionNoteDto(
            entity.Id,
            entity.AppointmentId,
            entity.ClientId,
            client?.FirstName ?? "",
            client?.LastName ?? "",
            entity.CreatedByUserId,
            createdByName,
            entity.IsDraft,
            entity.SessionType,
            entity.Notes,
            entity.AdherenceScore,
            entity.PractitionerAssessment,
            entity.ContextualFactors,
            entity.MeasurementsTaken,
            entity.PlanAdjustments,
            entity.FollowUpActions,
            appointment?.StartTime,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    private async Task<List<SessionNoteSummaryDto>> MapToSummaryListAsync(List<SessionNote> entities, AppDbContext db)
    {
        if (entities.Count == 0) return [];

        var clientIds = entities.Select(sn => sn.ClientId).Distinct().ToList();
        var clients = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        var appointmentIds = entities.Select(sn => sn.AppointmentId).Distinct().ToList();
        var appointments = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => appointmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a.StartTime);

        return entities.Select(e =>
        {
            var client = clients.GetValueOrDefault(e.ClientId);
            var appointmentDate = appointments.GetValueOrDefault(e.AppointmentId);
            return new SessionNoteSummaryDto(
                e.Id,
                e.AppointmentId,
                e.ClientId,
                client?.FirstName ?? "",
                client?.LastName ?? "",
                e.IsDraft,
                e.SessionType,
                e.AdherenceScore,
                appointmentDate == default ? null : appointmentDate,
                e.CreatedAt);
        }).ToList();
    }

    private async Task TryDispatchAsync(string entityType, int entityId, EntityChangeType changeType, string practitionerUserId, int? clientId = null)
    {
        try
        {
            await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
                entityType, entityId, changeType, practitionerUserId, DateTime.UtcNow, clientId));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
                changeType, entityType, entityId);
        }
    }
}
