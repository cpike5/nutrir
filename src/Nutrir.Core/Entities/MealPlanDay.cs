namespace Nutrir.Core.Entities;

public class MealPlanDay
{
    public int Id { get; set; }

    public int MealPlanId { get; set; }

    public int DayNumber { get; set; }

    public string? Label { get; set; }

    public string? Notes { get; set; }

    public List<MealSlot> MealSlots { get; set; } = [];
}
