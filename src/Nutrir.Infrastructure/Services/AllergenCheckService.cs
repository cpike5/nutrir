using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Allergens;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AllergenCheckService(
    IDbContextFactory<AppDbContext> contextFactory,
    IAuditLogService auditLogService) : IAllergenCheckService
{
    public async Task<List<AllergenWarningDto>> CheckAsync(int mealPlanId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var plan = await db.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .Include(mp => mp.AllergenWarningOverrides)
            .AsNoTracking()
            .FirstOrDefaultAsync(mp => mp.Id == mealPlanId);

        if (plan is null)
            return [];

        var allergies = await db.ClientAllergies
            .Where(a => a.ClientId == plan.ClientId && a.AllergyType == AllergyType.Food)
            .AsNoTracking()
            .ToListAsync();

        if (allergies.Count == 0)
            return [];

        var warnings = new List<AllergenWarningDto>();

        foreach (var day in plan.Days)
        {
            foreach (var slot in day.MealSlots)
            {
                foreach (var item in slot.Items)
                {
                    foreach (var allergy in allergies)
                    {
                        var category = AllergenKeywordMap.MapAllergyNameToCategory(allergy.Name);
                        bool isMatch;

                        if (category.HasValue)
                        {
                            var foodCategories = AllergenKeywordMap.MatchFood(item.FoodName);
                            isMatch = foodCategories.Contains(category.Value);
                        }
                        else
                        {
                            isMatch = AllergenKeywordMap.DirectMatch(item.FoodName, allergy.Name);
                        }

                        if (!isMatch)
                            continue;

                        var matchOverride = plan.AllergenWarningOverrides.FirstOrDefault(o =>
                            o.FoodName.Equals(item.FoodName, StringComparison.OrdinalIgnoreCase) &&
                            o.AllergenCategory == category);

                        warnings.Add(new AllergenWarningDto(
                            MealItemId: item.Id,
                            FoodName: item.FoodName,
                            DayNumber: day.DayNumber,
                            DayLabel: day.Label,
                            MealType: slot.MealType,
                            AllergenCategory: category,
                            MatchedAllergyName: allergy.Name,
                            Severity: allergy.Severity,
                            IsOverridden: matchOverride is not null,
                            OverrideNote: matchOverride?.OverrideNote,
                            AcknowledgedByUserId: matchOverride?.AcknowledgedByUserId,
                            AcknowledgedAt: matchOverride?.AcknowledgedAt));
                    }
                }
            }
        }

        return warnings;
    }

    public async Task AcknowledgeAsync(int mealPlanId, string foodName, AllergenCategory? category, string note, string userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var existing = await db.AllergenWarningOverrides
            .FirstOrDefaultAsync(o =>
                o.MealPlanId == mealPlanId &&
                o.FoodName == foodName &&
                o.AllergenCategory == category);

        if (existing is not null)
        {
            existing.OverrideNote = note;
            existing.AcknowledgedByUserId = userId;
            existing.AcknowledgedAt = DateTime.UtcNow;
        }
        else
        {
            db.AllergenWarningOverrides.Add(new AllergenWarningOverride
            {
                MealPlanId = mealPlanId,
                FoodName = foodName,
                AllergenCategory = category,
                OverrideNote = note,
                AcknowledgedByUserId = userId,
                AcknowledgedAt = DateTime.UtcNow
            });
        }

        await db.SaveChangesAsync();

        await auditLogService.LogAsync(userId, "AllergenWarningAcknowledged", "MealPlan", mealPlanId.ToString(),
            $"Acknowledged allergen warning for '{foodName}' (category: {category?.ToString() ?? "direct match"}). Note: {note}");
    }

    public async Task RemoveAcknowledgementAsync(int mealPlanId, string foodName, AllergenCategory? category, string userId)
    {
        await using var db = await contextFactory.CreateDbContextAsync();

        var existing = await db.AllergenWarningOverrides
            .FirstOrDefaultAsync(o =>
                o.MealPlanId == mealPlanId &&
                o.FoodName == foodName &&
                o.AllergenCategory == category);

        if (existing is null)
            return;

        db.AllergenWarningOverrides.Remove(existing);
        await db.SaveChangesAsync();

        await auditLogService.LogAsync(userId, "AllergenWarningAcknowledgementRemoved", "MealPlan", mealPlanId.ToString(),
            $"Removed allergen acknowledgement for '{foodName}' (category: {category?.ToString() ?? "direct match"})");
    }

    public async Task<bool> CanActivateAsync(int mealPlanId)
    {
        var warnings = await CheckAsync(mealPlanId);
        return !warnings.Any(w => w.Severity == AllergySeverity.Severe && !w.IsOverridden);
    }
}
