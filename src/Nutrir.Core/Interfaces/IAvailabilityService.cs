using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IAvailabilityService
{
    Task<List<AvailableSlotDto>> GetAvailableSlotsAsync(string practitionerId, DateOnly date, int durationMinutes);

    Task<List<PractitionerScheduleDto>> GetWeeklyScheduleAsync(string practitionerId);

    Task SetWeeklyScheduleAsync(string practitionerId, List<SetScheduleEntryDto> entries, string userId);

    Task<List<PractitionerTimeBlockDto>> GetTimeBlocksAsync(string practitionerId, DateOnly? fromDate = null, DateOnly? toDate = null);

    Task<PractitionerTimeBlockDto> AddTimeBlockAsync(CreateTimeBlockDto dto, string userId);

    Task<bool> RemoveTimeBlockAsync(int timeBlockId, string userId);

    Task<int> GetBufferTimeMinutesAsync(string practitionerId);

    Task SetBufferTimeMinutesAsync(string practitionerId, int minutes, string userId);
}
