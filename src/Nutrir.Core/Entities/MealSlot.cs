using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class MealSlot
{
    public int Id { get; set; }

    public int MealPlanDayId { get; set; }

    public MealType MealType { get; set; }

    public string? CustomName { get; set; }

    public int SortOrder { get; set; }

    public string? Notes { get; set; }

    public List<MealItem> Items { get; set; } = [];
}
