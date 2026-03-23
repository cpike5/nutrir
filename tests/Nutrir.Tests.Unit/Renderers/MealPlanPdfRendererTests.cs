using FluentAssertions;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace Nutrir.Tests.Unit.Renderers;

public class MealPlanPdfRendererTests
{
    public MealPlanPdfRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ---------------------------------------------------------------------------
    // Render — fully populated plan with days, slots, and items
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithFullyPopulatedPlan_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var plan = new MealPlanDetailDto(
            Id: 1,
            Title: "7-Day Weight Loss Plan",
            Description: "A structured plan designed to support healthy weight loss.",
            Status: MealPlanStatus.Active,
            ClientId: 42,
            ClientFirstName: "Jane",
            ClientLastName: "Doe",
            CreatedByUserId: "user-001",
            CreatedByName: "Dr. Sarah Green, RD",
            StartDate: new DateOnly(2024, 6, 3),
            EndDate: new DateOnly(2024, 6, 9),
            CalorieTarget: 1800m,
            ProteinTargetG: 130m,
            CarbsTargetG: 200m,
            FatTargetG: 60m,
            Notes: "Internal notes for the practitioner.",
            Instructions: "Eat every 3-4 hours and stay well hydrated.",
            Days:
            [
                new MealPlanDayDto(
                    Id: 1,
                    DayNumber: 1,
                    Label: "Monday",
                    Notes: "Focus on high-protein breakfast.",
                    MealSlots:
                    [
                        new MealSlotDto(
                            Id: 1,
                            MealType: MealType.Breakfast,
                            CustomName: null,
                            SortOrder: 0,
                            Notes: null,
                            Items:
                            [
                                new MealItemDto(
                                    Id: 1,
                                    FoodName: "Oatmeal with berries",
                                    Quantity: 1m,
                                    Unit: "cup",
                                    CaloriesKcal: 320m,
                                    ProteinG: 12m,
                                    CarbsG: 55m,
                                    FatG: 6m,
                                    Notes: "Use rolled oats",
                                    SortOrder: 0),
                                new MealItemDto(
                                    Id: 2,
                                    FoodName: "Greek Yogurt",
                                    Quantity: 150m,
                                    Unit: "g",
                                    CaloriesKcal: 100m,
                                    ProteinG: 17m,
                                    CarbsG: 6m,
                                    FatG: 0m,
                                    Notes: null,
                                    SortOrder: 1)
                            ],
                            TotalCalories: 420m,
                            TotalProtein: 29m,
                            TotalCarbs: 61m,
                            TotalFat: 6m),
                        new MealSlotDto(
                            Id: 2,
                            MealType: MealType.Lunch,
                            CustomName: null,
                            SortOrder: 1,
                            Notes: "Largest meal of the day.",
                            Items:
                            [
                                new MealItemDto(
                                    Id: 3,
                                    FoodName: "Grilled Chicken Breast",
                                    Quantity: 150m,
                                    Unit: "g",
                                    CaloriesKcal: 248m,
                                    ProteinG: 47m,
                                    CarbsG: 0m,
                                    FatG: 5m,
                                    Notes: null,
                                    SortOrder: 0)
                            ],
                            TotalCalories: 248m,
                            TotalProtein: 47m,
                            TotalCarbs: 0m,
                            TotalFat: 5m)
                    ],
                    TotalCalories: 668m,
                    TotalProtein: 76m,
                    TotalCarbs: 61m,
                    TotalFat: 11m)
            ],
            CreatedAt: new DateTime(2024, 5, 28, 10, 0, 0, DateTimeKind.Utc),
            UpdatedAt: new DateTime(2024, 5, 30, 14, 0, 0, DateTimeKind.Utc));

        // Act
        var result = MealPlanPdfRenderer.Render(plan);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a fully-populated meal plan should produce a valid PDF");
    }

    // ---------------------------------------------------------------------------
    // Render — minimal / sparse plan (no days, no optional fields)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithMinimalPlan_ReturnsNonEmptyByteArray()
    {
        // Arrange — only required fields populated, no days or optional text
        var plan = new MealPlanDetailDto(
            Id: 2,
            Title: "Minimal Plan",
            Description: null,
            Status: MealPlanStatus.Draft,
            ClientId: 5,
            ClientFirstName: "Bob",
            ClientLastName: "Smith",
            CreatedByUserId: "user-002",
            CreatedByName: null,
            StartDate: null,
            EndDate: null,
            CalorieTarget: null,
            ProteinTargetG: null,
            CarbsTargetG: null,
            FatTargetG: null,
            Notes: null,
            Instructions: null,
            Days: [],
            CreatedAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: null);

        // Act
        var result = MealPlanPdfRenderer.Render(plan);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a plan with no days or optional data should still produce a valid PDF");
    }

    // ---------------------------------------------------------------------------
    // Render — day with no meal slots
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithDayHavingNoMealSlots_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var plan = new MealPlanDetailDto(
            Id: 3,
            Title: "Empty Day Plan",
            Description: "A plan where a day has no meal slots yet.",
            Status: MealPlanStatus.Draft,
            ClientId: 7,
            ClientFirstName: "Alice",
            ClientLastName: "Chen",
            CreatedByUserId: "user-003",
            CreatedByName: "Nutritionist A",
            StartDate: new DateOnly(2024, 8, 1),
            EndDate: new DateOnly(2024, 8, 7),
            CalorieTarget: 2000m,
            ProteinTargetG: null,
            CarbsTargetG: null,
            FatTargetG: null,
            Notes: null,
            Instructions: null,
            Days:
            [
                new MealPlanDayDto(
                    Id: 10,
                    DayNumber: 1,
                    Label: null,
                    Notes: null,
                    MealSlots: [],
                    TotalCalories: 0m,
                    TotalProtein: 0m,
                    TotalCarbs: 0m,
                    TotalFat: 0m)
            ],
            CreatedAt: new DateTime(2024, 7, 25, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: null);

        // Act
        var result = MealPlanPdfRenderer.Render(plan);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a day with no meal slots should render the 'No meals added' fallback without throwing");
    }

    // ---------------------------------------------------------------------------
    // Render — slot with no items (empty table)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithSlotHavingNoItems_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var plan = new MealPlanDetailDto(
            Id: 4,
            Title: "Empty Slot Plan",
            Description: null,
            Status: MealPlanStatus.Active,
            ClientId: 9,
            ClientFirstName: "Carol",
            ClientLastName: "Taylor",
            CreatedByUserId: "user-004",
            CreatedByName: "Nutritionist B",
            StartDate: null,
            EndDate: null,
            CalorieTarget: null,
            ProteinTargetG: null,
            CarbsTargetG: null,
            FatTargetG: null,
            Notes: null,
            Instructions: null,
            Days:
            [
                new MealPlanDayDto(
                    Id: 20,
                    DayNumber: 1,
                    Label: "Day 1",
                    Notes: null,
                    MealSlots:
                    [
                        new MealSlotDto(
                            Id: 5,
                            MealType: MealType.Dinner,
                            CustomName: "Evening Meal",
                            SortOrder: 0,
                            Notes: null,
                            Items: [],
                            TotalCalories: 0m,
                            TotalProtein: 0m,
                            TotalCarbs: 0m,
                            TotalFat: 0m)
                    ],
                    TotalCalories: 0m,
                    TotalProtein: 0m,
                    TotalCarbs: 0m,
                    TotalFat: 0m)
            ],
            CreatedAt: new DateTime(2024, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt: null);

        // Act
        var result = MealPlanPdfRenderer.Render(plan);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a meal slot with no items should render the slot header without throwing");
    }
}
