using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IAllergenCheckService
{
    Task<List<AllergenWarningDto>> CheckAsync(int mealPlanId);
    Task AcknowledgeAsync(int mealPlanId, string foodName, AllergenCategory? category, string note, string userId);
    Task RemoveAcknowledgementAsync(int mealPlanId, string foodName, AllergenCategory? category, string userId);
    Task<bool> CanActivateAsync(int mealPlanId);
}
