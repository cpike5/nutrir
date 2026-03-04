using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nutrir.Infrastructure.Services;

public static class MealPlanPdfRenderer
{
    private const string PrimaryColor = "#2d6a4f";
    private const string TextColor = "#2a2d2b";
    private const string MutedColor = "#636865";
    private const string DayAccentColor = "#e8f5e9";
    private const string AlternateRowColor = "#f9f9f9";

    public static byte[] Render(MealPlanDetailDto plan)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(60);
                page.MarginVertical(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(TextColor));

                page.Header().Element(c => ComposeHeader(c, plan));
                page.Content().Element(c => ComposeContent(c, plan));
                page.Footer().Element(c => ComposeFooter(c));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, MealPlanDetailDto plan)
    {
        container.Column(column =>
        {
            column.Item().BorderBottom(2).BorderColor(PrimaryColor).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Text("Nutrir")
                    .FontSize(18).Bold().FontColor(PrimaryColor);
                row.RelativeItem().AlignRight().Text(plan.Title)
                    .FontSize(14).Bold();
            });

            column.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Client: ").Bold();
                    text.Span($"{plan.ClientFirstName} {plan.ClientLastName}");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Date: ").Bold();
                    text.Span(FormatDateRange(plan.StartDate, plan.EndDate));
                });
            });

            column.Item().PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Practitioner: ").Bold();
                    text.Span(plan.CreatedByName ?? "—");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Status: ").Bold();
                    text.Span(plan.Status.ToString());
                });
            });

            if (plan.CalorieTarget.HasValue || plan.ProteinTargetG.HasValue ||
                plan.CarbsTargetG.HasValue || plan.FatTargetG.HasValue)
            {
                var parts = new List<string>();
                if (plan.CalorieTarget.HasValue) parts.Add($"{plan.CalorieTarget.Value:N0} kcal");
                if (plan.ProteinTargetG.HasValue) parts.Add($"{plan.ProteinTargetG.Value:N0}g P");
                if (plan.CarbsTargetG.HasValue) parts.Add($"{plan.CarbsTargetG.Value:N0}g C");
                if (plan.FatTargetG.HasValue) parts.Add($"{plan.FatTargetG.Value:N0}g F");
                column.Item().PaddingBottom(4).AlignCenter()
                    .Text($"Targets: {string.Join(" | ", parts)}")
                    .FontSize(10).FontColor(MutedColor);
            }
        });
    }

    private static void ComposeContent(IContainer container, MealPlanDetailDto plan)
    {
        container.PaddingTop(8).Column(column =>
        {
            // Optional text sections
            if (!string.IsNullOrEmpty(plan.Description))
            {
                ComposeTextSection(column, "Description", plan.Description);
            }

            if (!string.IsNullOrEmpty(plan.Instructions))
            {
                ComposeTextSection(column, "Client Instructions", plan.Instructions);
            }

            if (!string.IsNullOrEmpty(plan.Notes))
            {
                ComposeTextSection(column, "Internal Notes", plan.Notes);
            }

            // Day panels
            foreach (var day in plan.Days)
            {
                column.Item().EnsureSpace(72).Column(dayColumn =>
                {
                    // Day header
                    dayColumn.Item().PaddingTop(12).Background(DayAccentColor).Padding(8).Row(row =>
                    {
                        row.RelativeItem().Text(day.Label ?? $"Day {day.DayNumber}")
                            .Bold().FontSize(11);
                        row.RelativeItem().AlignRight().Text(
                            $"{day.TotalCalories:N0} kcal \u00b7 {day.TotalProtein:N0}g P \u00b7 {day.TotalCarbs:N0}g C \u00b7 {day.TotalFat:N0}g F")
                            .FontSize(9).FontColor(MutedColor);
                    });

                    if (day.MealSlots.Count == 0)
                    {
                        dayColumn.Item().Padding(8).Text("No meals added for this day")
                            .FontSize(9).Italic().FontColor(MutedColor);
                    }
                    else
                    {
                        foreach (var slot in day.MealSlots)
                        {
                            ComposeSlot(dayColumn, slot);
                        }
                    }
                });
            }
        });
    }

    private static void ComposeTextSection(ColumnDescriptor column, string heading, string body)
    {
        column.Item().PaddingTop(10).Text(heading)
            .FontSize(11).Bold().FontColor(PrimaryColor);
        column.Item().PaddingTop(4).Text(body)
            .FontSize(10).LineHeight(1.4f);
    }

    private static void ComposeSlot(ColumnDescriptor dayColumn, MealSlotDto slot)
    {
        dayColumn.Item().PaddingTop(8).PaddingHorizontal(8).Column(slotColumn =>
        {
            // Slot header
            slotColumn.Item().Row(row =>
            {
                row.RelativeItem().Text(slot.CustomName ?? FormatMealType(slot.MealType))
                    .Bold().FontSize(10);
                row.RelativeItem().AlignRight().Text(
                    $"{slot.TotalCalories:N0} kcal \u00b7 {slot.TotalProtein:N0}g P \u00b7 {slot.TotalCarbs:N0}g C \u00b7 {slot.TotalFat:N0}g F")
                    .FontSize(9).FontColor(MutedColor);
            });

            if (slot.Items.Count == 0)
                return;

            // Item table
            slotColumn.Item().PaddingTop(4).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(); // Food
                    columns.ConstantColumn(60); // Qty
                    columns.ConstantColumn(45); // Cal
                    columns.ConstantColumn(40); // P
                    columns.ConstantColumn(40); // C
                    columns.ConstantColumn(40); // F
                });

                // Header row
                table.Header(header =>
                {
                    header.Cell().PaddingVertical(4).Text("Food").Bold().FontSize(9).FontColor(MutedColor);
                    header.Cell().PaddingVertical(4).AlignRight().Text("Qty").Bold().FontSize(9).FontColor(MutedColor);
                    header.Cell().PaddingVertical(4).AlignRight().Text("Cal").Bold().FontSize(9).FontColor(MutedColor);
                    header.Cell().PaddingVertical(4).AlignRight().Text("P").Bold().FontSize(9).FontColor(MutedColor);
                    header.Cell().PaddingVertical(4).AlignRight().Text("C").Bold().FontSize(9).FontColor(MutedColor);
                    header.Cell().PaddingVertical(4).AlignRight().Text("F").Bold().FontSize(9).FontColor(MutedColor);
                });

                // Data rows
                for (var i = 0; i < slot.Items.Count; i++)
                {
                    var item = slot.Items[i];
                    var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                    table.Cell().Background(bgColor).PaddingVertical(3).Column(foodCol =>
                    {
                        foodCol.Item().Text(item.FoodName).FontSize(10);
                        if (!string.IsNullOrEmpty(item.Notes))
                        {
                            foodCol.Item().Text(item.Notes).FontSize(8).Italic().FontColor(MutedColor);
                        }
                    });
                    table.Cell().Background(bgColor).PaddingVertical(3).AlignRight()
                        .Text($"{item.Quantity:G} {item.Unit}").FontSize(10);
                    table.Cell().Background(bgColor).PaddingVertical(3).AlignRight()
                        .Text($"{item.CaloriesKcal:N0}").FontSize(10);
                    table.Cell().Background(bgColor).PaddingVertical(3).AlignRight()
                        .Text($"{item.ProteinG:N0}").FontSize(10);
                    table.Cell().Background(bgColor).PaddingVertical(3).AlignRight()
                        .Text($"{item.CarbsG:N0}").FontSize(10);
                    table.Cell().Background(bgColor).PaddingVertical(3).AlignRight()
                        .Text($"{item.FatG:N0}").FontSize(10);
                }

                // Slot total row
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .Text("Total").Bold().FontSize(9);
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .AlignRight().Text("").FontSize(9);
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .AlignRight().Text($"{slot.TotalCalories:N0}").Bold().FontSize(9);
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .AlignRight().Text($"{slot.TotalProtein:N0}").Bold().FontSize(9);
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .AlignRight().Text($"{slot.TotalCarbs:N0}").Bold().FontSize(9);
                table.Cell().BorderTop(1).BorderColor("#e0e0e0").PaddingVertical(3)
                    .AlignRight().Text($"{slot.TotalFat:N0}").Bold().FontSize(9);
            });
        });
    }

    private static void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor("#e0e0e0").PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text("Nutrir \u2014 Meal Plan")
                .FontSize(8).FontColor(MutedColor);
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor(MutedColor);
                text.CurrentPageNumber().FontSize(8).FontColor(MutedColor);
                text.Span(" of ").FontSize(8).FontColor(MutedColor);
                text.TotalPages().FontSize(8).FontColor(MutedColor);
            });
        });
    }

    private static string FormatDateRange(DateOnly? start, DateOnly? end)
    {
        if (!start.HasValue && !end.HasValue) return "—";
        var s = start?.ToString("MMM d, yyyy") ?? "—";
        var e = end?.ToString("MMM d, yyyy") ?? "—";
        return $"{s} — {e}";
    }

    private static string FormatMealType(MealType type) => type switch
    {
        MealType.Breakfast => "Breakfast",
        MealType.MorningSnack => "Morning Snack",
        MealType.Lunch => "Lunch",
        MealType.AfternoonSnack => "Afternoon Snack",
        MealType.Dinner => "Dinner",
        MealType.EveningSnack => "Evening Snack",
        _ => type.ToString()
    };
}
