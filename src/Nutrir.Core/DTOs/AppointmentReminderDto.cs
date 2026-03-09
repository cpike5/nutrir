using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AppointmentReminderDto(
    int Id,
    int AppointmentId,
    ReminderType ReminderType,
    DateTime ScheduledFor,
    ReminderStatus Status,
    DateTime? SentAt,
    string? FailureReason,
    DateTime CreatedAt);
