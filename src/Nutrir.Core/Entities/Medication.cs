namespace Nutrir.Core.Entities;

public class Medication
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? GenericName { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
