namespace Nutrir.Core.Entities;

public class Condition
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? IcdCode { get; set; }

    public string? Category { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
