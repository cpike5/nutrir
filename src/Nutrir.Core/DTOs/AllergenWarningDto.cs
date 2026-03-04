using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AllergenWarningDto(
    int MealItemId,
    string FoodName,
    int DayNumber,
    string? DayLabel,
    MealType MealType,
    AllergenCategory? AllergenCategory,
    string MatchedAllergyName,
    AllergySeverity Severity,
    bool IsOverridden,
    string? OverrideNote,
    string? AcknowledgedByUserId,
    DateTime? AcknowledgedAt);
