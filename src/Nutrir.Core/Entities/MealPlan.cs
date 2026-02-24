using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class MealPlan
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public MealPlanStatus Status { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    public decimal? CalorieTarget { get; set; }

    public decimal? ProteinTargetG { get; set; }

    public decimal? CarbsTargetG { get; set; }

    public decimal? FatTargetG { get; set; }

    public string? Notes { get; set; }

    public string? Instructions { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public List<MealPlanDay> Days { get; set; } = [];
}
