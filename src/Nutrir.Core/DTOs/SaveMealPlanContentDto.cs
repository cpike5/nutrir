using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record SaveMealPlanContentDto(
    int MealPlanId,
    List<SaveMealPlanDayDto> Days);

public record SaveMealPlanDayDto(
    int? Id,
    int DayNumber,
    string? Label,
    string? Notes,
    List<SaveMealSlotDto> MealSlots);

public record SaveMealSlotDto(
    int? Id,
    MealType MealType,
    string? CustomName,
    int SortOrder,
    string? Notes,
    List<SaveMealItemDto> Items);

public record SaveMealItemDto(
    int? Id,
    string FoodName,
    decimal Quantity,
    string Unit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string? Notes,
    int SortOrder);
