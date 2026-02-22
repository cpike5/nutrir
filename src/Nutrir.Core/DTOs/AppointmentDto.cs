using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AppointmentDto(
    int Id,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string NutritionistId,
    string? NutritionistName,
    AppointmentType Type,
    AppointmentStatus Status,
    DateTime StartTime,
    int DurationMinutes,
    DateTime EndTime,
    AppointmentLocation Location,
    string? VirtualMeetingUrl,
    string? LocationNotes,
    string? Notes,
    string? CancellationReason,
    DateTime? CancelledAt,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
