namespace Nutrir.Core.DTOs;

public record PractitionerScheduleDto(
    int Id,
    string UserId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsAvailable);

public record SetScheduleEntryDto(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    bool IsAvailable);
