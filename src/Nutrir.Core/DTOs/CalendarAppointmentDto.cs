using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record CalendarAppointmentDto(
    int Id,
    string Title,
    DateTime StartTime,
    DateTime EndTime,
    AppointmentType Type,
    AppointmentStatus Status);
