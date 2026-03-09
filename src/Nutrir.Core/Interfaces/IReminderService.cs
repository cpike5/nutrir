using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IReminderService
{
    Task<List<AppointmentReminderDto>> GetRemindersForAppointmentAsync(int appointmentId);
    Task ResendReminderAsync(int appointmentId, ReminderType type, string userId);
}
