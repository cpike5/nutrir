using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class AppointmentReminder
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public ReminderType ReminderType { get; set; }

    /// <summary>
    /// The appointment StartTime at the time the reminder was sent.
    /// Used with (AppointmentId, ReminderType) for idempotency — if rescheduled, new reminders are sent.
    /// </summary>
    public DateTime ScheduledFor { get; set; }

    public ReminderStatus Status { get; set; }

    public DateTime? SentAt { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsDeleted { get; set; }

    public DateTime? DeletedAt { get; set; }
}
