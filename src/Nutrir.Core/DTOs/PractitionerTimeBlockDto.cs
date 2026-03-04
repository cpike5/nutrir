using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record PractitionerTimeBlockDto(
    int Id,
    string UserId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    TimeBlockType BlockType,
    string? Notes);

public record CreateTimeBlockDto(
    string UserId,
    DateOnly Date,
    TimeOnly StartTime,
    TimeOnly EndTime,
    TimeBlockType BlockType,
    string? Notes);
