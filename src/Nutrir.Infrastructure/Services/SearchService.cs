using Microsoft.EntityFrameworkCore;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class SearchService(AppDbContext dbContext) : ISearchService
{
    public async Task<SearchResultDto> SearchAsync(string query, string userId, int maxPerGroup = 3)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
            return new SearchResultDto([], 0);

        var term = query.Trim().ToLower();
        var groups = new List<SearchResultGroup>();

        // Search Clients
        var clientGroup = await SearchClientsAsync(term, userId, maxPerGroup);
        if (clientGroup.TotalInGroup > 0)
            groups.Add(clientGroup);

        // Search Appointments (sequential — EF Core DbContext is not thread-safe)
        var appointmentGroup = await SearchAppointmentsAsync(term, userId, maxPerGroup);
        if (appointmentGroup.TotalInGroup > 0)
            groups.Add(appointmentGroup);

        // Search Meal Plans
        var mealPlanGroup = await SearchMealPlansAsync(term, userId, maxPerGroup);
        if (mealPlanGroup.TotalInGroup > 0)
            groups.Add(mealPlanGroup);

        var totalCount = groups.Sum(g => g.TotalInGroup);
        return new SearchResultDto(groups, totalCount);
    }

    private async Task<SearchResultGroup> SearchClientsAsync(string term, string userId, int max)
    {
        var query = dbContext.Clients
            .Where(c => c.PrimaryNutritionistId == userId)
            .Where(c =>
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)) ||
                (c.Phone != null && c.Phone.ToLower().Contains(term)));

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(c => c.FirstName.ToLower().StartsWith(term) || c.LastName.ToLower().StartsWith(term) ? 0 : 1)
            .ThenByDescending(c => c.UpdatedAt ?? c.CreatedAt)
            .Take(max)
            .Select(c => new SearchResultItem(
                c.Id,
                c.FirstName + " " + c.LastName,
                c.Email,
                "Active",
                "success",
                "/clients/" + c.Id,
                (c.FirstName.Substring(0, 1) + c.LastName.Substring(0, 1)).ToUpper()))
            .ToListAsync();

        return new SearchResultGroup("Clients", items, totalCount);
    }

    private async Task<SearchResultGroup> SearchAppointmentsAsync(string term, string userId, int max)
    {
        var query = from a in dbContext.Appointments
                    join c in dbContext.Clients on a.ClientId equals c.Id
                    where a.NutritionistId == userId
                    where c.FirstName.ToLower().Contains(term) ||
                          c.LastName.ToLower().Contains(term) ||
                          a.Type.ToString().ToLower().Contains(term)
                    select new { Appointment = a, Client = c };

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(x => x.Client.FirstName.ToLower().StartsWith(term) ||
                          x.Client.LastName.ToLower().StartsWith(term) ? 0 : 1)
            .ThenByDescending(x => x.Appointment.StartTime)
            .Take(max)
            .Select(x => new SearchResultItem(
                x.Appointment.Id,
                FormatAppointmentType(x.Appointment.Type),
                x.Client.FirstName + " " + x.Client.LastName + " \u00b7 " +
                    x.Appointment.StartTime.ToString("MMM d, yyyy"),
                x.Appointment.Status.ToString(),
                GetAppointmentStatusVariant(x.Appointment.Status),
                "/appointments/" + x.Appointment.Id,
                x.Client.FirstName.Substring(0, 1) + x.Client.LastName.Substring(0, 1)))
            .ToListAsync();

        return new SearchResultGroup("Appointments", items, totalCount);
    }

    private async Task<SearchResultGroup> SearchMealPlansAsync(string term, string userId, int max)
    {
        var query = from mp in dbContext.MealPlans
                    join c in dbContext.Clients on mp.ClientId equals c.Id
                    where mp.CreatedByUserId == userId
                    where mp.Title.ToLower().Contains(term) ||
                          c.FirstName.ToLower().Contains(term) ||
                          c.LastName.ToLower().Contains(term)
                    select new { MealPlan = mp, Client = c };

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(x => x.MealPlan.Title.ToLower().StartsWith(term) ? 0 : 1)
            .ThenByDescending(x => x.MealPlan.UpdatedAt ?? x.MealPlan.CreatedAt)
            .Take(max)
            .Select(x => new SearchResultItem(
                x.MealPlan.Id,
                x.MealPlan.Title,
                x.Client.FirstName + " " + x.Client.LastName + " \u00b7 Created " +
                    x.MealPlan.CreatedAt.ToString("MMM d, yyyy"),
                x.MealPlan.Status.ToString(),
                GetMealPlanStatusVariant(x.MealPlan.Status),
                "/meal-plans/" + x.MealPlan.Id,
                x.Client.FirstName.Substring(0, 1) + x.Client.LastName.Substring(0, 1)))
            .ToListAsync();

        return new SearchResultGroup("Meal Plans", items, totalCount);
    }

    private static string FormatAppointmentType(AppointmentType type) => type switch
    {
        AppointmentType.InitialConsultation => "Initial Consultation",
        AppointmentType.FollowUp => "Follow-Up",
        AppointmentType.CheckIn => "Check-In",
        _ => type.ToString()
    };

    private static string GetAppointmentStatusVariant(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => "accent",
        AppointmentStatus.Confirmed => "success",
        AppointmentStatus.Completed => "success",
        AppointmentStatus.Cancelled => "error",
        AppointmentStatus.LateCancellation => "error",
        AppointmentStatus.NoShow => "warning",
        _ => "accent"
    };

    private static string GetMealPlanStatusVariant(MealPlanStatus status) => status switch
    {
        MealPlanStatus.Active => "primary",
        MealPlanStatus.Draft => "warning",
        MealPlanStatus.Archived => "accent",
        _ => "accent"
    };
}
