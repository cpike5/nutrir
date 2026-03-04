using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class PractitionerTimeBlock
{
    public int Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    public TimeBlockType BlockType { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
