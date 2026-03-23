using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Exceptions;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ProgressService : IProgressService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly IRetentionTracker _retentionTracker;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<ProgressService> _logger;

    public ProgressService(
        AppDbContext dbContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        IRetentionTracker retentionTracker,
        INotificationDispatcher notificationDispatcher,
        ILogger<ProgressService> logger)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _retentionTracker = retentionTracker;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    // ── Entries ────────────────────────────────────────────

    public async Task<ProgressEntryDetailDto?> GetEntryByIdAsync(int id)
    {
        var entity = await _dbContext.ProgressEntries
            .Include(e => e.Measurements)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity is null) return null;

        var client = await _dbContext.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == entity.ClientId);
        var createdByName = await GetUserNameAsync(entity.CreatedByUserId);

        return MapToEntryDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<List<ProgressEntrySummaryDto>> GetEntriesByClientAsync(int clientId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entities = await db.ProgressEntries
            .Include(e => e.Measurements)
            .Where(e => e.ClientId == clientId)
            .OrderByDescending(e => e.EntryDate)
            .ToListAsync();

        return entities.Select(MapToEntrySummaryDto).ToList();
    }

    public async Task<ProgressEntryDetailDto> CreateEntryAsync(CreateProgressEntryDto dto, string userId)
    {
        var entity = new ProgressEntry
        {
            ClientId = dto.ClientId,
            CreatedByUserId = userId,
            EntryDate = dto.EntryDate,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var m in dto.Measurements)
        {
            entity.Measurements.Add(new ProgressMeasurement
            {
                MetricType = m.MetricType,
                CustomMetricName = m.CustomMetricName,
                Value = m.Value,
                Unit = m.Unit
            });
        }

        _dbContext.ProgressEntries.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress entry created: {EntryId} by {UserId}", entity.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressEntryCreated",
            "ProgressEntry",
            entity.Id.ToString(),
            $"Created progress entry for client {entity.ClientId} on {entity.EntryDate}");

        await _retentionTracker.UpdateLastInteractionAsync(entity.ClientId);
        await TryDispatchAsync("ProgressEntry", entity.Id, EntityChangeType.Created, userId, entity.ClientId);

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var createdByName = await GetUserNameAsync(userId);

        await CheckGoalAchievementsAsync(entity.ClientId, entity.Measurements, userId);

        return MapToEntryDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<bool> UpdateEntryAsync(int id, UpdateProgressEntryDto dto, string userId)
    {
        var entity = await _dbContext.ProgressEntries
            .Include(e => e.Measurements)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (entity is null) return false;

        entity.EntryDate = dto.EntryDate;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        // Replace-children pattern: remove old measurements, add new
        _dbContext.ProgressMeasurements.RemoveRange(entity.Measurements);

        foreach (var m in dto.Measurements)
        {
            entity.Measurements.Add(new ProgressMeasurement
            {
                ProgressEntryId = entity.Id,
                MetricType = m.MetricType,
                CustomMetricName = m.CustomMetricName,
                Value = m.Value,
                Unit = m.Unit
            });
        }

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrentEditException("ProgressEntry", id);
        }

        _logger.LogInformation("Progress entry updated: {EntryId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressEntryUpdated",
            "ProgressEntry",
            id.ToString(),
            $"Updated progress entry for {entity.EntryDate}");

        await _retentionTracker.UpdateLastInteractionAsync(entity.ClientId);
        await TryDispatchAsync("ProgressEntry", entity.Id, EntityChangeType.Updated, userId, entity.ClientId);

        await CheckGoalAchievementsAsync(entity.ClientId, entity.Measurements, userId);

        return true;
    }

    public async Task<bool> SoftDeleteEntryAsync(int id, string userId)
    {
        var entity = await _dbContext.ProgressEntries.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress entry soft-deleted: {EntryId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressEntrySoftDeleted",
            "ProgressEntry",
            id.ToString(),
            $"Soft-deleted progress entry for {entity.EntryDate}");

        await TryDispatchAsync("ProgressEntry", id, EntityChangeType.Deleted, userId, entity.ClientId);

        return true;
    }

    // ── Goals ──────────────────────────────────────────────

    public async Task<ProgressGoalDetailDto?> GetGoalByIdAsync(int id)
    {
        var entity = await _dbContext.ProgressGoals.FindAsync(id);
        if (entity is null) return null;

        var client = await _dbContext.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == entity.ClientId);
        var createdByName = await GetUserNameAsync(entity.CreatedByUserId);

        return MapToGoalDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<List<ProgressGoalSummaryDto>> GetGoalsByClientAsync(int clientId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entities = await db.ProgressGoals
            .Where(g => g.ClientId == clientId)
            .OrderByDescending(g => g.CreatedAt)
            .ToListAsync();

        return entities.Select(MapToGoalSummaryDto).ToList();
    }

    public async Task<ProgressGoalDetailDto> CreateGoalAsync(CreateProgressGoalDto dto, string userId)
    {
        var entity = new ProgressGoal
        {
            ClientId = dto.ClientId,
            CreatedByUserId = userId,
            Title = dto.Title,
            Description = dto.Description,
            GoalType = dto.GoalType,
            TargetValue = dto.TargetValue,
            TargetUnit = dto.TargetUnit,
            TargetDate = dto.TargetDate,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProgressGoals.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress goal created: {GoalId} by {UserId}", entity.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressGoalCreated",
            "ProgressGoal",
            entity.Id.ToString(),
            $"Created progress goal for client {entity.ClientId}");

        await _retentionTracker.UpdateLastInteractionAsync(entity.ClientId);
        await TryDispatchAsync("ProgressGoal", entity.Id, EntityChangeType.Created, userId, entity.ClientId);

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var createdByName = await GetUserNameAsync(userId);

        return MapToGoalDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<bool> UpdateGoalAsync(int id, UpdateProgressGoalDto dto, string userId)
    {
        var entity = await _dbContext.ProgressGoals.FindAsync(id);
        if (entity is null) return false;

        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.GoalType = dto.GoalType;
        entity.TargetValue = dto.TargetValue;
        entity.TargetUnit = dto.TargetUnit;
        entity.TargetDate = dto.TargetDate;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress goal updated: {GoalId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressGoalUpdated",
            "ProgressGoal",
            id.ToString(),
            "Updated progress goal");

        await _retentionTracker.UpdateLastInteractionAsync(entity.ClientId);
        await TryDispatchAsync("ProgressGoal", id, EntityChangeType.Updated, userId, entity.ClientId);

        return true;
    }

    public async Task<bool> UpdateGoalStatusAsync(int id, GoalStatus newStatus, string userId)
    {
        var entity = await _dbContext.ProgressGoals.FindAsync(id);
        if (entity is null) return false;

        var oldStatus = entity.Status;
        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Progress goal status changed: {GoalId} {OldStatus} -> {NewStatus} by {UserId}",
            id, oldStatus, newStatus, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressGoalStatusChanged",
            "ProgressGoal",
            id.ToString(),
            $"Status changed from {oldStatus} to {newStatus}");

        await TryDispatchAsync("ProgressGoal", id, EntityChangeType.Updated, userId, entity.ClientId);

        return true;
    }

    public async Task<bool> SoftDeleteGoalAsync(int id, string userId)
    {
        var entity = await _dbContext.ProgressGoals.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress goal soft-deleted: {GoalId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressGoalSoftDeleted",
            "ProgressGoal",
            id.ToString(),
            "Soft-deleted progress goal");

        await TryDispatchAsync("ProgressGoal", id, EntityChangeType.Deleted, userId, entity.ClientId);

        return true;
    }

    // ── Chart ──────────────────────────────────────────────

    public async Task<ProgressChartDataDto?> GetChartDataAsync(int clientId, MetricType metricType)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entries = await db.ProgressEntries
            .Include(e => e.Measurements)
            .Where(e => e.ClientId == clientId)
            .OrderBy(e => e.EntryDate)
            .ToListAsync();

        var points = entries
            .SelectMany(e => e.Measurements
                .Where(m => m.MetricType == metricType)
                .Select(m => new ProgressChartPointDto(e.EntryDate, m.Value)))
            .ToList();

        if (points.Count == 0) return null;

        var label = FormatMetricLabel(metricType);
        var unit = points.FirstOrDefault() is not null
            ? entries.SelectMany(e => e.Measurements)
                .FirstOrDefault(m => m.MetricType == metricType)?.Unit
            : null;

        return new ProgressChartDataDto(metricType, label, unit, points);
    }

    // ── Dashboard ──────────────────────────────────────────

    public async Task<List<ProgressEntrySummaryDto>> GetRecentByClientAsync(int clientId, int count = 3)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entities = await db.ProgressEntries
            .Include(e => e.Measurements)
            .Where(e => e.ClientId == clientId)
            .OrderByDescending(e => e.EntryDate)
            .Take(count)
            .ToListAsync();

        return entities.Select(MapToEntrySummaryDto).ToList();
    }

    // ── Goal Achievement Detection ─────────────────────────

    private async Task CheckGoalAchievementsAsync(int clientId, IEnumerable<ProgressMeasurement> measurements, string userId)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();

            var activeGoals = await db.ProgressGoals
                .Where(g => g.ClientId == clientId && g.Status == GoalStatus.Active && g.TargetValue.HasValue)
                .ToListAsync();

            if (activeGoals.Count == 0) return;

            foreach (var measurement in measurements)
            {
                var goalType = MapMetricToGoalType(measurement.MetricType);
                if (goalType is null) continue;

                var matchingGoals = activeGoals.Where(g => g.GoalType == goalType.Value).ToList();

                foreach (var goal in matchingGoals)
                {
                    var isAchieved = IsGoalAchieved(measurement.MetricType, measurement.Value, goal.TargetValue!.Value);

                    if (isAchieved)
                    {
                        _logger.LogInformation(
                            "Goal achievement suggested: Goal {GoalId} '{GoalTitle}' for client {ClientId} — {MetricType} value {Value} meets target {Target}",
                            goal.Id, goal.Title, clientId, measurement.MetricType, measurement.Value, goal.TargetValue);

                        await _auditLogService.LogAsync(
                            userId,
                            "GoalAchievementSuggested",
                            "ProgressGoal",
                            goal.Id.ToString(),
                            $"Measurement {measurement.MetricType} = {measurement.Value} meets target {goal.TargetValue} for goal '{goal.Title}'");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking goal achievements for client {ClientId}", clientId);
        }
    }

    private static GoalType? MapMetricToGoalType(MetricType metricType) => metricType switch
    {
        MetricType.Weight => GoalType.Weight,
        MetricType.BMI => GoalType.Weight,
        MetricType.BodyFatPercentage => GoalType.BodyComposition,
        MetricType.WaistCircumference => GoalType.BodyComposition,
        MetricType.HipCircumference => GoalType.BodyComposition,
        _ => null
    };

    private static bool IsGoalAchieved(MetricType metricType, decimal value, decimal target)
    {
        // Weight, body fat, waist, hip, BMI: lower is better (value <= target)
        // All other mapped metrics also use lower-is-better
        return metricType switch
        {
            MetricType.Weight or MetricType.BodyFatPercentage or MetricType.WaistCircumference
                or MetricType.HipCircumference or MetricType.BMI => value <= target,
            _ => value >= target
        };
    }

    // ── Helpers ────────────────────────────────────────────

    private async Task<string?> GetUserNameAsync(string userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is ApplicationUser appUser)
            return !string.IsNullOrEmpty(appUser.DisplayName)
                ? appUser.DisplayName
                : $"{appUser.FirstName} {appUser.LastName}".Trim();
        return null;
    }

    private static ProgressEntrySummaryDto MapToEntrySummaryDto(ProgressEntry entity)
    {
        var notePreview = entity.Notes is not null && entity.Notes.Length > 80
            ? entity.Notes[..80] + "..."
            : entity.Notes;

        return new ProgressEntrySummaryDto(
            entity.Id,
            entity.ClientId,
            entity.EntryDate,
            entity.Measurements.Count,
            notePreview,
            entity.CreatedAt);
    }

    private static ProgressEntryDetailDto MapToEntryDetailDto(
        ProgressEntry entity, string clientFirstName, string clientLastName, string? createdByName)
    {
        var measurements = entity.Measurements
            .Select(m => new ProgressMeasurementDto(
                m.Id, m.MetricType, m.CustomMetricName, m.Value, m.Unit))
            .ToList();

        return new ProgressEntryDetailDto(
            entity.Id,
            entity.ClientId,
            clientFirstName,
            clientLastName,
            entity.CreatedByUserId,
            createdByName,
            entity.EntryDate,
            entity.Notes,
            measurements,
            entity.CreatedAt,
            entity.UpdatedAt);
    }

    private static ProgressGoalSummaryDto MapToGoalSummaryDto(ProgressGoal entity)
    {
        return new ProgressGoalSummaryDto(
            entity.Id,
            entity.ClientId,
            entity.Title,
            entity.GoalType,
            entity.Status,
            entity.TargetValue,
            entity.TargetUnit,
            entity.TargetDate,
            entity.CreatedAt);
    }

    private static ProgressGoalDetailDto MapToGoalDetailDto(
        ProgressGoal entity, string clientFirstName, string clientLastName, string? createdByName)
    {
        return new ProgressGoalDetailDto(
            entity.Id,
            entity.ClientId,
            clientFirstName,
            clientLastName,
            entity.CreatedByUserId,
            createdByName,
            entity.Title,
            entity.Description,
            entity.GoalType,
            entity.Status,
            entity.TargetValue,
            entity.TargetUnit,
            entity.TargetDate,
            entity.CreatedAt,
            entity.UpdatedAt);
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

    private static string FormatMetricLabel(MetricType type) => type switch
    {
        MetricType.Weight => "Weight",
        MetricType.BodyFatPercentage => "Body Fat %",
        MetricType.WaistCircumference => "Waist",
        MetricType.HipCircumference => "Hip",
        MetricType.BMI => "BMI",
        MetricType.BloodPressureSystolic => "BP Systolic",
        MetricType.BloodPressureDiastolic => "BP Diastolic",
        MetricType.RestingHeartRate => "Resting HR",
        MetricType.Custom => "Custom",
        _ => type.ToString()
    };
}
