namespace Nutrir.Core.Entities;

public class Food
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public decimal ServingSize { get; set; }

    public string ServingSizeUnit { get; set; } = string.Empty;

    public decimal CaloriesKcal { get; set; }

    public decimal ProteinG { get; set; }

    public decimal CarbsG { get; set; }

    public decimal FatG { get; set; }

    public string[] Tags { get; set; } = [];

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}
