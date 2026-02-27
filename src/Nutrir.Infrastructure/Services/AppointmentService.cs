using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        INotificationDispatcher notificationDispatcher,
        ILogger<AppointmentService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    public async Task<AppointmentDto?> GetByIdAsync(int id)
    {
        var entity = await _dbContext.Appointments
            .FirstOrDefaultAsync(a => a.Id == id);

        if (entity is null) return null;

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var nutritionistName = await GetNutritionistNameAsync(entity.NutritionistId);

        return MapToDto(entity, client?.FirstName ?? "", client?.LastName ?? "", nutritionistName);
    }

    public async Task<List<AppointmentDto>> GetListAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? clientId = null,
        AppointmentStatus? status = null)
    {
        var query = _dbContext.Appointments.AsQueryable();

        if (fromDate.HasValue)
            query = query.Where(a => a.StartTime >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(a => a.StartTime <= toDate.Value);

        if (clientId.HasValue)
            query = query.Where(a => a.ClientId == clientId.Value);

        if (status.HasValue)
            query = query.Where(a => a.Status == status.Value);

        var entities = await query
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        return await MapToDtoListAsync(entities);
    }

    public async Task<AppointmentDto> CreateAsync(CreateAppointmentDto dto, string userId)
    {
        var entity = new Appointment
        {
            ClientId = dto.ClientId,
            NutritionistId = userId,
            Type = dto.Type,
            Status = AppointmentStatus.Scheduled,
            StartTime = dto.StartTime,
            DurationMinutes = dto.DurationMinutes,
            Location = dto.Location,
            VirtualMeetingUrl = dto.VirtualMeetingUrl,
            LocationNotes = dto.LocationNotes,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Appointment created: {AppointmentId} by {UserId}", entity.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "AppointmentCreated",
            "Appointment",
            entity.Id.ToString(),
            $"Created {entity.Type} appointment for client {entity.ClientId}");

        await TryDispatchAsync("Appointment", entity.Id, EntityChangeType.Created, userId);

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var nutritionistName = await GetNutritionistNameAsync(entity.NutritionistId);

        return MapToDto(entity, client?.FirstName ?? "", client?.LastName ?? "", nutritionistName);
    }

    public async Task<bool> UpdateAsync(UpdateAppointmentDto dto, string userId)
    {
        var entity = await _dbContext.Appointments.FindAsync(dto.Id);

        if (entity is null) return false;

        entity.Type = dto.Type;
        entity.Status = dto.Status;
        entity.StartTime = dto.StartTime;
        entity.DurationMinutes = dto.DurationMinutes;
        entity.Location = dto.Location;
        entity.VirtualMeetingUrl = dto.VirtualMeetingUrl;
        entity.LocationNotes = dto.LocationNotes;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Appointment updated: {AppointmentId} by {UserId}", dto.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "AppointmentUpdated",
            "Appointment",
            dto.Id.ToString(),
            $"Updated appointment {dto.Id}");

        await TryDispatchAsync("Appointment", dto.Id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> UpdateStatusAsync(int id, AppointmentStatus newStatus, string userId, string? cancellationReason = null)
    {
        var entity = await _dbContext.Appointments.FindAsync(id);

        if (entity is null) return false;

        var oldStatus = entity.Status;
        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;

        if (newStatus is AppointmentStatus.Cancelled or AppointmentStatus.LateCancellation)
        {
            entity.CancelledAt = DateTime.UtcNow;
            entity.CancellationReason = cancellationReason;
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Appointment status changed: {AppointmentId} {OldStatus} -> {NewStatus} by {UserId}",
            id, oldStatus, newStatus, userId);

        await _auditLogService.LogAsync(
            userId,
            "AppointmentStatusChanged",
            "Appointment",
            id.ToString(),
            $"Status changed from {oldStatus} to {newStatus}");

        await TryDispatchAsync("Appointment", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id, string userId)
    {
        var entity = await _dbContext.Appointments.FindAsync(id);

        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Appointment soft-deleted: {AppointmentId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "AppointmentSoftDeleted",
            "Appointment",
            id.ToString(),
            $"Soft-deleted appointment {id}");

        await TryDispatchAsync("Appointment", id, EntityChangeType.Deleted, userId);

        return true;
    }

    public async Task<List<AppointmentDto>> GetTodaysAppointmentsAsync(string nutritionistId)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var entities = await _dbContext.Appointments
            .Where(a => a.NutritionistId == nutritionistId
                        && a.StartTime >= today
                        && a.StartTime < tomorrow)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        return await MapToDtoListAsync(entities);
    }

    public async Task<List<AppointmentDto>> GetUpcomingByClientAsync(int clientId, int count = 5)
    {
        var now = DateTime.UtcNow;

        var entities = await _dbContext.Appointments
            .Where(a => a.ClientId == clientId && a.StartTime >= now)
            .OrderBy(a => a.StartTime)
            .Take(count)
            .ToListAsync();

        return await MapToDtoListAsync(entities);
    }

    public async Task<int> GetWeekCountAsync(string nutritionistId)
    {
        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);

        return await _dbContext.Appointments
            .CountAsync(a => a.NutritionistId == nutritionistId
                             && a.StartTime >= startOfWeek
                             && a.StartTime < endOfWeek);
    }

    private async Task<List<AppointmentDto>> MapToDtoListAsync(List<Appointment> entities)
    {
        if (entities.Count == 0) return [];

        var clientIds = entities.Select(a => a.ClientId).Distinct().ToList();
        var clients = await _dbContext.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        var nutritionistIds = entities.Select(a => a.NutritionistId).Distinct().ToList();
        var nutritionists = await _dbContext.Users
            .Where(u => nutritionistIds.Contains(u.Id))
            .OfType<ApplicationUser>()
            .ToDictionaryAsync(u => u.Id, u =>
                !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : $"{u.FirstName} {u.LastName}".Trim());

        return entities.Select(e =>
        {
            var client = clients.GetValueOrDefault(e.ClientId);
            var nutritionistName = nutritionists.GetValueOrDefault(e.NutritionistId);
            return MapToDto(e, client?.FirstName ?? "", client?.LastName ?? "", nutritionistName);
        }).ToList();
    }

    private async Task TryDispatchAsync(string entityType, int entityId, EntityChangeType changeType, string practitionerUserId)
    {
        try
        {
            await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
                entityType, entityId, changeType, practitionerUserId, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
                changeType, entityType, entityId);
        }
    }

    private async Task<string?> GetNutritionistNameAsync(string userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is ApplicationUser appUser)
            return !string.IsNullOrEmpty(appUser.DisplayName)
                ? appUser.DisplayName
                : $"{appUser.FirstName} {appUser.LastName}".Trim();
        return null;
    }

    private static AppointmentDto MapToDto(Appointment entity, string clientFirstName, string clientLastName, string? nutritionistName)
    {
        return new AppointmentDto(
            entity.Id,
            entity.ClientId,
            clientFirstName,
            clientLastName,
            entity.NutritionistId,
            nutritionistName,
            entity.Type,
            entity.Status,
            entity.StartTime,
            entity.DurationMinutes,
            entity.EndTime,
            entity.Location,
            entity.VirtualMeetingUrl,
            entity.LocationNotes,
            entity.Notes,
            entity.CancellationReason,
            entity.CancelledAt,
            entity.CreatedAt,
            entity.UpdatedAt);
    }
}
