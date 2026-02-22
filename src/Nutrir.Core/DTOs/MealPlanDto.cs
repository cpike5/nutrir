using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record MealPlanSummaryDto(
    int Id,
    string Title,
    MealPlanStatus Status,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? CalorieTarget,
    decimal? ProteinTargetG,
    decimal? CarbsTargetG,
    decimal? FatTargetG,
    int DayCount,
    int TotalItems,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
