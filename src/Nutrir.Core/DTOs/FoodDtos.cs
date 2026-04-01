namespace Nutrir.Core.DTOs;

public record FoodDto(
    int Id,
    string Name,
    decimal ServingSize,
    string ServingSizeUnit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string[] Tags,
    string? Notes);

public record CreateFoodDto(
    string Name,
    decimal ServingSize,
    string ServingSizeUnit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string[] Tags,
    string? Notes);

public record UpdateFoodDto(
    string Name,
    decimal ServingSize,
    string ServingSizeUnit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string[] Tags,
    string? Notes);
