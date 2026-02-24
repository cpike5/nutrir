namespace Nutrir.Core.Entities;

public class ProgressEntry
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateOnly EntryDate { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public List<ProgressMeasurement> Measurements { get; set; } = [];
}
