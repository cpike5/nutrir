namespace Nutrir.Core.Entities;

public class SessionNote
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int ClientId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool IsDraft { get; set; } = true;

    // Structured sections
    public string? Notes { get; set; }
    public int? AdherenceScore { get; set; } // 0-100
    public string? MeasurementsTaken { get; set; }
    public string? PlanAdjustments { get; set; }
    public string? FollowUpActions { get; set; }

    // Soft-delete
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
