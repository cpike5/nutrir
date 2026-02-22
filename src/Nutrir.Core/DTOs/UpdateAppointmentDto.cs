using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record UpdateAppointmentDto(
    int Id,
    AppointmentType Type,
    AppointmentStatus Status,
    DateTime StartTime,
    int DurationMinutes,
    AppointmentLocation Location,
    string? VirtualMeetingUrl,
    string? LocationNotes,
    string? Notes);
