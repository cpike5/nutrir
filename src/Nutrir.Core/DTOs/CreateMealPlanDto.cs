namespace Nutrir.Core.DTOs;

public record CreateMealPlanDto(
    int ClientId,
    string Title,
    string? Description,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? CalorieTarget,
    decimal? ProteinTargetG,
    decimal? CarbsTargetG,
    decimal? FatTargetG,
    string? Notes,
    string? Instructions,
    int NumberOfDays);
