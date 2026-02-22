using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IDashboardService
{
    Task<DashboardMetricsDto> GetMetricsAsync();
    Task<List<ClientDto>> GetRecentClientsAsync(int count = 7);
    Task<List<ClientDto>> GetClientsMissingConsentAsync();
    Task<List<AppointmentDto>> GetTodaysAppointmentsAsync();
    Task<int> GetThisWeekAppointmentCountAsync();
}
