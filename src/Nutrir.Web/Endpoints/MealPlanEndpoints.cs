using System.Security.Claims;
using Nutrir.Core.Interfaces;

namespace Nutrir.Web.Endpoints;

public static class MealPlanEndpoints
{
    public static void MapMealPlanEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/meal-plans")
            .RequireAuthorization(policy => policy.RequireRole("Admin", "Nutritionist", "Assistant"));

        group.MapGet("/{id:int}/pdf", async (int id, IMealPlanPdfService pdfService, IMealPlanService mealPlanService, HttpContext ctx) =>
        {
            var userId = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

            var plan = await mealPlanService.GetByIdAsync(id);
            if (plan is null)
                return Results.NotFound();

            var pdfBytes = await pdfService.GeneratePdfAsync(id, userId);
            var fileName = $"MealPlan-{plan.ClientLastName}-{plan.StartDate?.ToString("yyyy-MM-dd") ?? "undated"}.pdf";

            return Results.File(pdfBytes, "application/pdf", fileName);
        });
    }
}
