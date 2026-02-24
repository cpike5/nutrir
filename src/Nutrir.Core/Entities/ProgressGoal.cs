using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ProgressGoal
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public GoalType GoalType { get; set; }

    public decimal? TargetValue { get; set; }

    public string? TargetUnit { get; set; }

    public DateOnly? TargetDate { get; set; }

    public GoalStatus Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
