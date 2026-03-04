namespace Nutrir.Core.Interfaces;

public interface IMealPlanPdfService
{
    Task<byte[]> GeneratePdfAsync(int mealPlanId, string userId);
}
