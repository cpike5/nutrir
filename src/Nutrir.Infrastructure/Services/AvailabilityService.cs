using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AvailabilityService : IAvailabilityService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AvailabilityService> _logger;

    public AvailabilityService(
        AppDbContext dbContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<AvailabilityService> logger)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(string practitionerId, DateOnly date, int durationMinutes)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var dayOfWeek = date.DayOfWeek;

        // 1. Load schedule for the given day
        var schedule = await db.PractitionerSchedules
            .FirstOrDefaultAsync(s => s.UserId == practitionerId && s.DayOfWeek == dayOfWeek);

        if (schedule is null || !schedule.IsAvailable)
            return [];

        // 2. Load buffer time
        var user = await db.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == practitionerId);
        var bufferMinutes = user?.BufferTimeMinutes ?? 15;

        // 3. Load existing appointments for that date
        var dateStart = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var dateEnd = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var appointments = await db.Appointments
            .Where(a => a.NutritionistId == practitionerId
                        && a.StartTime >= dateStart
                        && a.StartTime <= dateEnd
                        && a.Status != AppointmentStatus.Cancelled
                        && a.Status != AppointmentStatus.LateCancellation)
            .Select(a => new { a.StartTime, a.DurationMinutes })
            .ToListAsync();

        // 4. Load time blocks for that date
        var timeBlocks = await db.PractitionerTimeBlocks
            .Where(tb => tb.UserId == practitionerId && tb.Date == date)
            .Select(tb => new { tb.StartTime, tb.EndTime })
            .ToListAsync();

        // 5. Build blocked intervals (appointments + buffer + time blocks)
        var blockedIntervals = new List<(TimeOnly Start, TimeOnly End)>();

        foreach (var appt in appointments)
        {
            var apptStart = TimeOnly.FromDateTime(appt.StartTime);
            var apptEnd = apptStart.AddMinutes(appt.DurationMinutes);

            // Add buffer on both sides
            var bufferedStart = apptStart.AddMinutes(-bufferMinutes);
            var bufferedEnd = apptEnd.AddMinutes(bufferMinutes);
            blockedIntervals.Add((bufferedStart, bufferedEnd));
        }

        foreach (var block in timeBlocks)
        {
            blockedIntervals.Add((block.StartTime, block.EndTime));
        }

        // 6. Generate candidate slots at 15-min intervals
        var slots = new List<AvailableSlotDto>();
        var current = schedule.StartTime;
        var scheduleEnd = schedule.EndTime;

        while (current.AddMinutes(durationMinutes) <= scheduleEnd)
        {
            var slotEnd = current.AddMinutes(durationMinutes);

            // Check if slot overlaps any blocked interval
            var isBlocked = blockedIntervals.Any(b =>
                current < b.End && slotEnd > b.Start);

            if (!isBlocked)
            {
                slots.Add(new AvailableSlotDto(current, slotEnd));
            }

            current = current.AddMinutes(15);
        }

        return slots;
    }

    public async Task<List<PractitionerScheduleDto>> GetWeeklyScheduleAsync(string practitionerId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var schedules = await db.PractitionerSchedules
            .Where(s => s.UserId == practitionerId)
            .OrderBy(s => s.DayOfWeek)
            .ToListAsync();

        return schedules.Select(s => new PractitionerScheduleDto(
            s.Id, s.UserId, s.DayOfWeek, s.StartTime, s.EndTime, s.IsAvailable)).ToList();
    }

    public async Task SetWeeklyScheduleAsync(string practitionerId, List<SetScheduleEntryDto> entries, string userId)
    {
        // Soft-delete existing schedule entries
        var existing = await _dbContext.PractitionerSchedules
            .Where(s => s.UserId == practitionerId)
            .ToListAsync();

        foreach (var entry in existing)
        {
            entry.IsDeleted = true;
            entry.DeletedAt = DateTime.UtcNow;
            entry.DeletedBy = userId;
        }

        // Create new entries
        foreach (var entry in entries)
        {
            _dbContext.PractitionerSchedules.Add(new PractitionerSchedule
            {
                UserId = practitionerId,
                DayOfWeek = entry.DayOfWeek,
                StartTime = entry.StartTime,
                EndTime = entry.EndTime,
                IsAvailable = entry.IsAvailable,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Weekly schedule updated for practitioner {PractitionerId} by {UserId}", practitionerId, userId);

        await _auditLogService.LogAsync(
            userId,
            "WeeklyScheduleUpdated",
            "PractitionerSchedule",
            practitionerId,
            $"Updated weekly schedule with {entries.Count} entries");
    }

    public async Task<List<PractitionerTimeBlockDto>> GetTimeBlocksAsync(string practitionerId, DateOnly? fromDate = null, DateOnly? toDate = null)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var query = db.PractitionerTimeBlocks
            .Where(tb => tb.UserId == practitionerId);

        if (fromDate.HasValue)
            query = query.Where(tb => tb.Date >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(tb => tb.Date <= toDate.Value);

        var blocks = await query
            .OrderBy(tb => tb.Date)
            .ThenBy(tb => tb.StartTime)
            .ToListAsync();

        return blocks.Select(MapToTimeBlockDto).ToList();
    }

    public async Task<PractitionerTimeBlockDto> AddTimeBlockAsync(CreateTimeBlockDto dto, string userId)
    {
        var entity = new PractitionerTimeBlock
        {
            UserId = dto.UserId,
            Date = dto.Date,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            BlockType = dto.BlockType,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PractitionerTimeBlocks.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Time block added: {TimeBlockId} for {PractitionerId} by {UserId}", entity.Id, dto.UserId, userId);

        await _auditLogService.LogAsync(
            userId,
            "TimeBlockAdded",
            "PractitionerTimeBlock",
            entity.Id.ToString(),
            $"Added {dto.BlockType} block on {dto.Date} from {dto.StartTime} to {dto.EndTime}");

        return MapToTimeBlockDto(entity);
    }

    public async Task<bool> RemoveTimeBlockAsync(int timeBlockId, string userId)
    {
        var entity = await _dbContext.PractitionerTimeBlocks.FindAsync(timeBlockId);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Time block removed: {TimeBlockId} by {UserId}", timeBlockId, userId);

        await _auditLogService.LogAsync(
            userId,
            "TimeBlockRemoved",
            "PractitionerTimeBlock",
            timeBlockId.ToString(),
            $"Removed time block {timeBlockId}");

        return true;
    }

    public async Task<int> GetBufferTimeMinutesAsync(string practitionerId)
    {
        var user = await _dbContext.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == practitionerId);
        return user?.BufferTimeMinutes ?? 15;
    }

    public async Task SetBufferTimeMinutesAsync(string practitionerId, int minutes, string userId)
    {
        var user = await _dbContext.Users.OfType<ApplicationUser>().FirstOrDefaultAsync(u => u.Id == practitionerId);
        if (user is null) return;

        user.BufferTimeMinutes = minutes;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Buffer time set to {Minutes} minutes for {PractitionerId} by {UserId}", minutes, practitionerId, userId);

        await _auditLogService.LogAsync(
            userId,
            "BufferTimeUpdated",
            "ApplicationUser",
            practitionerId,
            $"Buffer time set to {minutes} minutes");
    }

    public async Task<(bool IsWithin, string? Reason)> IsSlotWithinScheduleAsync(
        string practitionerId, DateTime startTimeUtc, int durationMinutes)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var date = DateOnly.FromDateTime(startTimeUtc);
        var dayOfWeek = startTimeUtc.DayOfWeek;

        // 1. Check practitioner schedule for the day of week
        var schedule = await db.PractitionerSchedules
            .FirstOrDefaultAsync(s => s.UserId == practitionerId && s.DayOfWeek == dayOfWeek);

        if (schedule is null || !schedule.IsAvailable)
            return (false, $"Practitioner is not available on {dayOfWeek}");

        // 2. Check if appointment falls within working hours
        var slotStart = TimeOnly.FromDateTime(startTimeUtc);
        var slotEnd = slotStart.AddMinutes(durationMinutes);

        if (slotStart < schedule.StartTime || slotEnd > schedule.EndTime)
            return (false, $"Appointment falls outside working hours ({schedule.StartTime}–{schedule.EndTime})");

        // 3. Check for time blocks on the specific date
        var conflictingBlock = await db.PractitionerTimeBlocks
            .FirstOrDefaultAsync(tb => tb.UserId == practitionerId
                                       && tb.Date == date
                                       && tb.StartTime < slotEnd
                                       && tb.EndTime > slotStart);

        if (conflictingBlock is not null)
            return (false, $"Time block conflict: {conflictingBlock.BlockType}");

        return (true, null);
    }

    private static PractitionerTimeBlockDto MapToTimeBlockDto(PractitionerTimeBlock entity) =>
        new(entity.Id, entity.UserId, entity.Date, entity.StartTime, entity.EndTime, entity.BlockType, entity.Notes);
}
