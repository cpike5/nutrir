using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class AllergenWarningOverride
{
    public int Id { get; set; }

    public int MealPlanId { get; set; }

    public string FoodName { get; set; } = string.Empty;

    public AllergenCategory? AllergenCategory { get; set; }

    public string OverrideNote { get; set; } = string.Empty;

    public string AcknowledgedByUserId { get; set; } = string.Empty;

    public DateTime AcknowledgedAt { get; set; } = DateTime.UtcNow;
}
