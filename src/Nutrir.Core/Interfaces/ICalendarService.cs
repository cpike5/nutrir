using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface ICalendarService
{
    Task<List<CalendarAppointmentDto>> GetAppointmentsByDateRangeAsync(DateTime start, DateTime end);
}
