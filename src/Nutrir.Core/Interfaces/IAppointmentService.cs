using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IAppointmentService
{
    Task<AppointmentDto?> GetByIdAsync(int id);

    Task<List<AppointmentDto>> GetListAsync(
        DateTime? fromDate = null,
        DateTime? toDate = null,
        int? clientId = null,
        AppointmentStatus? status = null);

    Task<AppointmentDto> CreateAsync(CreateAppointmentDto dto, string userId);

    Task<bool> UpdateAsync(UpdateAppointmentDto dto, string userId);

    Task<bool> UpdateStatusAsync(int id, AppointmentStatus newStatus, string userId, string? cancellationReason = null);

    Task<bool> SoftDeleteAsync(int id, string userId);

    Task<List<AppointmentDto>> GetTodaysAppointmentsAsync(string nutritionistId);

    Task<List<AppointmentDto>> GetUpcomingByClientAsync(int clientId, int count = 5);

    Task<int> GetWeekCountAsync(string nutritionistId);
}
