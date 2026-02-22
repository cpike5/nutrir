using Microsoft.EntityFrameworkCore;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _dbContext;
    private readonly IAppointmentService _appointmentService;

    public DashboardService(AppDbContext dbContext, IAppointmentService appointmentService)
    {
        _dbContext = dbContext;
        _appointmentService = appointmentService;
    }

    public async Task<DashboardMetricsDto> GetMetricsAsync()
    {
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var totalActive = await _dbContext.Clients.CountAsync();
        var pendingConsent = await _dbContext.Clients.CountAsync(c => !c.ConsentGiven);
        var newThisMonth = await _dbContext.Clients.CountAsync(c => c.CreatedAt >= startOfMonth);

        return new DashboardMetricsDto(totalActive, pendingConsent, newThisMonth);
    }

    public async Task<List<ClientDto>> GetRecentClientsAsync(int count = 7)
    {
        return await _dbContext.Clients
            .OrderByDescending(c => c.CreatedAt)
            .Take(count)
            .Select(c => MapToDto(c))
            .ToListAsync();
    }

    public async Task<List<ClientDto>> GetClientsMissingConsentAsync()
    {
        return await _dbContext.Clients
            .Where(c => !c.ConsentGiven)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => MapToDto(c))
            .ToListAsync();
    }

    public async Task<List<AppointmentDto>> GetTodaysAppointmentsAsync()
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var entities = await _dbContext.Appointments
            .Where(a => a.StartTime >= today && a.StartTime < tomorrow)
            .OrderBy(a => a.StartTime)
            .ToListAsync();

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
        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var endOfWeek = startOfWeek.AddDays(7);

        return await _dbContext.Appointments
            .CountAsync(a => a.StartTime >= startOfWeek && a.StartTime < endOfWeek);
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
