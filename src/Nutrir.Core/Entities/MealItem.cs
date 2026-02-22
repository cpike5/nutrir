namespace Nutrir.Core.Entities;

public class MealItem
{
    public int Id { get; set; }

    public int MealSlotId { get; set; }

    public string FoodName { get; set; } = string.Empty;

    public decimal Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal CaloriesKcal { get; set; }

    public decimal ProteinG { get; set; }

    public decimal CarbsG { get; set; }

    public decimal FatG { get; set; }

    public string? Notes { get; set; }

    public int SortOrder { get; set; }
}
