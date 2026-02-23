using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record MealPlanDetailDto(
    int Id,
    string Title,
    string? Description,
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
    string? Notes,
    string? Instructions,
    List<MealPlanDayDto> Days,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record MealPlanDayDto(
    int Id,
    int DayNumber,
    string? Label,
    string? Notes,
    List<MealSlotDto> MealSlots,
    decimal TotalCalories,
    decimal TotalProtein,
    decimal TotalCarbs,
    decimal TotalFat);

public record MealSlotDto(
    int Id,
    MealType MealType,
    string? CustomName,
    int SortOrder,
    string? Notes,
    List<MealItemDto> Items,
    decimal TotalCalories,
    decimal TotalProtein,
    decimal TotalCarbs,
    decimal TotalFat);

public record MealItemDto(
    int Id,
    string FoodName,
    decimal Quantity,
    string Unit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string? Notes,
    int SortOrder);
