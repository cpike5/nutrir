using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class Appointment
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string NutritionistId { get; set; } = string.Empty;

    public AppointmentType Type { get; set; }

    public AppointmentStatus Status { get; set; }

    public DateTime StartTime { get; set; }

    public int DurationMinutes { get; set; }

    public DateTime EndTime => StartTime.AddMinutes(DurationMinutes);

    public AppointmentLocation Location { get; set; }

    public string? VirtualMeetingUrl { get; set; }

    public string? LocationNotes { get; set; }

    public string? Notes { get; set; }

    public string? CancellationReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}
