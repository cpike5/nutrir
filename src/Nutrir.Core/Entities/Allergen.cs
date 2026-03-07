namespace Nutrir.Core.Entities;

public class Allergen
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Category { get; set; } // "Food", "Drug", "Environmental"

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
