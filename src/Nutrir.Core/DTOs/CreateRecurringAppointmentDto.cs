namespace Nutrir.Core.DTOs;

public record CreateRecurringAppointmentDto(
    CreateAppointmentDto Base,
    int IntervalDays,
    int Count);

public record RecurringAppointmentResultDto(
    int CreatedCount,
    int SkippedCount,
    List<int> CreatedIds,
    List<string> SkippedReasons);
