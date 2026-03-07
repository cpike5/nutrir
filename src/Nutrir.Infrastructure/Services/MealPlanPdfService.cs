using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Diagnostics;

namespace Nutrir.Infrastructure.Services;

public class MealPlanPdfService : IMealPlanPdfService
{
    private readonly IMealPlanService _mealPlanService;
    private readonly IAuditLogService _auditLogService;

    public MealPlanPdfService(IMealPlanService mealPlanService, IAuditLogService auditLogService)
    {
        _mealPlanService = mealPlanService;
        _auditLogService = auditLogService;
    }

    public async Task<byte[]> GeneratePdfAsync(int mealPlanId, string userId)
    {
        using var activity = NutrirTelemetry.DocSource.StartActivity("MealPlan PDF Generation");
        activity?.SetTag("document.type", "pdf");
        activity?.SetTag("document.entity_type", "MealPlan");
        activity?.SetTag("document.entity_id", mealPlanId);

        var plan = await _mealPlanService.GetByIdAsync(mealPlanId);
        if (plan is null)
            throw new KeyNotFoundException($"Meal plan #{mealPlanId} not found.");

        var pdfBytes = MealPlanPdfRenderer.Render(plan);
        activity?.SetTag("document.size_bytes", pdfBytes.Length);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanPdfExported",
            "MealPlan",
            mealPlanId.ToString(),
            $"Exported PDF for meal plan '{plan.Title}'");

        return pdfBytes;
    }
}
