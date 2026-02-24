using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ProgressService : IProgressService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ProgressService> _logger;

    public ProgressService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        ILogger<ProgressService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
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
        var entities = await _dbContext.ProgressEntries
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

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var createdByName = await GetUserNameAsync(userId);

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

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress entry updated: {EntryId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressEntryUpdated",
            "ProgressEntry",
            id.ToString(),
            $"Updated progress entry for {entity.EntryDate}");

        return true;
    }

    public async Task<bool> SoftDeleteEntryAsync(int id, string userId)
    {
        var entity = await _dbContext.ProgressEntries.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress entry soft-deleted: {EntryId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressEntrySoftDeleted",
            "ProgressEntry",
            id.ToString(),
            $"Soft-deleted progress entry for {entity.EntryDate}");

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
        var entities = await _dbContext.ProgressGoals
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
            $"Created goal \"{entity.Title}\" for client {entity.ClientId}");

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
            $"Updated goal \"{entity.Title}\"");

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

        return true;
    }

    public async Task<bool> SoftDeleteGoalAsync(int id, string userId)
    {
        var entity = await _dbContext.ProgressGoals.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Progress goal soft-deleted: {GoalId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "ProgressGoalSoftDeleted",
            "ProgressGoal",
            id.ToString(),
            $"Soft-deleted goal \"{entity.Title}\"");

        return true;
    }

    // ── Chart ──────────────────────────────────────────────

    public async Task<ProgressChartDataDto?> GetChartDataAsync(int clientId, MetricType metricType)
    {
        var entries = await _dbContext.ProgressEntries
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
        var entities = await _dbContext.ProgressEntries
            .Include(e => e.Measurements)
            .Where(e => e.ClientId == clientId)
            .OrderByDescending(e => e.EntryDate)
            .Take(count)
            .ToListAsync();

        return entities.Select(MapToEntrySummaryDto).ToList();
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
