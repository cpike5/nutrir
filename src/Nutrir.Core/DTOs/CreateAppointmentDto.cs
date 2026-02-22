using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record CreateAppointmentDto(
    int ClientId,
    AppointmentType Type,
    DateTime StartTime,
    int DurationMinutes,
    AppointmentLocation Location,
    string? VirtualMeetingUrl,
    string? LocationNotes,
    string? Notes);
