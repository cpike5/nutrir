using Microsoft.EntityFrameworkCore;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAppointmentService _appointmentService;

    public DashboardService(IDbContextFactory<AppDbContext> dbContextFactory, IAppointmentService appointmentService)
    {
        _dbContextFactory = dbContextFactory;
        _appointmentService = appointmentService;
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalActive = await db.Clients.CountAsync();
        var pendingConsent = await db.Clients.CountAsync(c => !c.ConsentGiven);
        var newThisMonth = await db.Clients.CountAsync(c => c.CreatedAt >= startOfMonth);

        return new DashboardMetricsDto(totalActive, pendingConsent, newThisMonth);
    }

    public async Task<List<ClientDto>> GetRecentClientsAsync(int count = 7)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Clients
            .OrderByDescending(c => c.CreatedAt)
            .Take(count)
            .Select(c => MapToDto(c))
            .ToListAsync();
    }

    public async Task<List<ClientDto>> GetClientsMissingConsentAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.Clients
            .Where(c => !c.ConsentGiven)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => MapToDto(c))
            .ToListAsync();
    }

    public async Task<List<AppointmentDto>> GetTodaysAppointmentsAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var entities = await db.Appointments
            .Where(a => a.StartTime >= today && a.StartTime < tomorrow)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        if (entities.Count == 0) return [];

        var clientIds = entities.Select(a => a.ClientId).Distinct().ToList();
        var clients = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        var nutritionistIds = entities.Select(a => a.NutritionistId).Distinct().ToList();
        var nutritionists = await db.Users
            .Where(u => nutritionistIds.Contains(u.Id))
            .OfType<ApplicationUser>()
            .ToDictionaryAsync(u => u.Id, u =>
                !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : $"{u.FirstName} {u.LastName}".Trim());

        return entities.Select(e =>
        {
            var client = clients.GetValueOrDefault(e.ClientId);
            var nutritionistName = nutritionists.GetValueOrDefault(e.NutritionistId);
            return new AppointmentDto(
                e.Id, e.ClientId, client?.FirstName ?? "", client?.LastName ?? "",
                e.NutritionistId, nutritionistName,
                e.Type, e.Status, e.StartTime, e.DurationMinutes, e.EndTime,
                e.Location, e.VirtualMeetingUrl, e.LocationNotes,
                e.Notes, e.CancellationReason, e.CancelledAt,
                e.CreatedAt, e.UpdatedAt);
        }).ToList();
    }

    public async Task<int> GetThisWeekAppointmentCountAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);

        return await db.Appointments
            .CountAsync(a => a.StartTime >= startOfWeek && a.StartTime < endOfWeek);
    }

    public async Task<int> GetActiveMealPlanCountAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        return await db.MealPlans.CountAsync(mp => mp.Status == MealPlanStatus.Active);
    }

    public async Task<List<MealPlanSummaryDto>> GetRecentMealPlansAsync(int count = 5)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entities = await db.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .OrderByDescending(mp => mp.CreatedAt)
            .Take(count)
            .ToListAsync();

        if (entities.Count == 0) return [];

        var clientIds = entities.Select(mp => mp.ClientId).Distinct().ToList();
        var clients = await db.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        var userIds = entities.Select(mp => mp.CreatedByUserId).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .OfType<ApplicationUser>()
            .ToDictionaryAsync(u => u.Id, u =>
                !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : $"{u.FirstName} {u.LastName}".Trim());

        return entities.Select(mp =>
        {
            var client = clients.GetValueOrDefault(mp.ClientId);
            var userName = users.GetValueOrDefault(mp.CreatedByUserId);
            var totalItems = mp.Days.SelectMany(d => d.MealSlots).SelectMany(s => s.Items).Count();

            return new MealPlanSummaryDto(
                mp.Id, mp.Title, mp.Status,
                mp.ClientId, client?.FirstName ?? "", client?.LastName ?? "",
                mp.CreatedByUserId, userName,
                mp.StartDate, mp.EndDate,
                mp.CalorieTarget, mp.ProteinTargetG,
                mp.CarbsTargetG, mp.FatTargetG,
                mp.Days.Count, totalItems,
                mp.CreatedAt, mp.UpdatedAt);
        }).ToList();
    }

    private static ClientDto MapToDto(Core.Entities.Client c) => new(
        c.Id,
        c.FirstName,
        c.LastName,
        c.Email,
        c.Phone,
        c.DateOfBirth,
        c.PrimaryNutritionistId,
        null,
        c.ConsentGiven,
        c.ConsentTimestamp,
        c.ConsentPolicyVersion,
        c.Notes,
        c.IsDeleted,
        c.CreatedAt,
        c.UpdatedAt,
        c.DeletedAt);
}
