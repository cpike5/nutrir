using Nutrir.Core.DTOs;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nutrir.Infrastructure.Services;

public static class DataExportPdfRenderer
{
    private const string PrimaryColor = "#2d6a4f";
    private const string TextColor = "#2a2d2b";
    private const string MutedColor = "#636865";
    private const string AlternateRowColor = "#f9f9f9";

    public static byte[] Render(ClientDataExportDto data)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(60);
                page.MarginVertical(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(TextColor));

                page.Header().Element(c => ComposeHeader(c, data));
                page.Content().Element(c => ComposeContent(c, data));
                page.Footer().Element(c => ComposeFooter(c, data));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, ClientDataExportDto data)
    {
        container.Column(column =>
        {
            column.Item().BorderBottom(2).BorderColor(PrimaryColor).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text("Nutrir")
                        .FontSize(18).Bold().FontColor(PrimaryColor);
                    col.Item().Text("Client Data Export")
                        .FontSize(11).FontColor(MutedColor);
                });
            });

            column.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Client: ").Bold();
                    text.Span($"{data.ClientProfile.FirstName} {data.ClientProfile.LastName}");
                });
                row.RelativeItem().AlignRight().Text(text =>
                {
                    text.Span("Date: ").Bold();
                    text.Span(data.ExportMetadata.ExportDate.ToString("MMMM d, yyyy"));
                });
            });

            column.Item().PaddingTop(12).Text(data.ExportMetadata.PipedaNotice)
                .FontSize(9).Italic().LineHeight(1.3f).FontColor(MutedColor);

            column.Item().PaddingTop(8).Text("CONFIDENTIAL")
                .FontSize(11).Bold().FontColor("#d32f2f");
        });
    }

    private static void ComposeContent(IContainer container, ClientDataExportDto data)
    {
        container.PaddingTop(12).Column(column =>
        {
            // Client Profile Section
            ComposeClientProfileSection(column, data.ClientProfile);

            // Health Profile Section
            ComposeHealthProfileSection(column, data.HealthProfile);

            // Appointments Section
            ComposeAppointmentsSection(column, data.Appointments);

            // Meal Plans Section
            ComposeMealPlansSection(column, data.MealPlans);

            // Progress Goals Section
            ComposeProgressGoalsSection(column, data.ProgressGoals);

            // Progress Entries Section
            ComposeProgressEntriesSection(column, data.ProgressEntries);

            // Intake Forms Section
            ComposeIntakeFormsSection(column, data.IntakeForms);

            // Consent History Section
            ComposeConsentHistorySection(column, data.ConsentHistory);

            // Audit Log Section
            ComposeAuditLogSection(column, data.AuditLog);
        });
    }

    private static void ComposeClientProfileSection(ColumnDescriptor column, ClientProfileExportDto profile)
    {
        ComposeSection(column, "Client Profile", innerCol =>
        {
            var items = new[]
            {
                ("First Name", profile.FirstName),
                ("Last Name", profile.LastName),
                ("Email", profile.Email ?? "—"),
                ("Phone", profile.Phone ?? "—"),
                ("Date of Birth", profile.DateOfBirth?.ToString("MMMM d, yyyy") ?? "—"),
                ("Primary Practitioner", profile.PrimaryNutritionistName),
                ("Consent Given", profile.ConsentGiven ? "Yes" : "No"),
                ("Consent Date", profile.ConsentTimestamp?.ToString("MMMM d, yyyy") ?? "—"),
                ("Consent Policy Version", profile.ConsentPolicyVersion ?? "—"),
                ("Created", profile.CreatedAt.ToString("MMMM d, yyyy")),
                ("Last Updated", profile.UpdatedAt?.ToString("MMMM d, yyyy") ?? "—"),
            };

            foreach (var (label, value) in items)
            {
                innerCol.Item().Row(row =>
                {
                    row.ConstantItem(150).Text(label).Bold().FontSize(9);
                    row.RelativeItem().Text(value).FontSize(9);
                });
            }

            if (!string.IsNullOrEmpty(profile.Notes?.Trim()))
            {
                innerCol.Item().PaddingTop(4).Row(row =>
                {
                    row.ConstantItem(150).Text("Notes").Bold().FontSize(9);
                    row.RelativeItem().Text(profile.Notes).FontSize(9).LineHeight(1.2f);
                });
            }
        });
    }

    private static void ComposeHealthProfileSection(ColumnDescriptor column, HealthProfileExportDto profile)
    {
        ComposeSection(column, "Health Profile", innerCol =>
        {
            // Allergies
            if (profile.Allergies.Any())
            {
                innerCol.Item().PaddingTop(8).Text("Allergies")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(80);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(4).Text("Name").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Severity").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Type").Bold().FontSize(9).FontColor(MutedColor);
                    });

                    for (var i = 0; i < profile.Allergies.Count; i++)
                    {
                        var allergy = profile.Allergies[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                        var deletedSuffix = allergy.IsDeleted ? " (Deleted)" : "";

                        table.Cell().Background(bgColor).Padding(4)
                            .Text($"{allergy.Name}{deletedSuffix}").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(allergy.Severity).FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(allergy.AllergyType).FontSize(9);
                    }
                });
            }

            // Medications
            if (profile.Medications.Any())
            {
                innerCol.Item().PaddingTop(8).Text("Medications")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(120);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(80);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(4).Text("Name").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Dosage").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Frequency").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Prescribed For").Bold().FontSize(9).FontColor(MutedColor);
                    });

                    for (var i = 0; i < profile.Medications.Count; i++)
                    {
                        var med = profile.Medications[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                        var deletedSuffix = med.IsDeleted ? " (Deleted)" : "";

                        table.Cell().Background(bgColor).Padding(4)
                            .Text($"{med.Name}{deletedSuffix}").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(med.Dosage ?? "—").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(med.Frequency ?? "—").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(med.PrescribedFor ?? "—").FontSize(9);
                    }
                });
            }

            // Conditions
            if (profile.Conditions.Any())
            {
                innerCol.Item().PaddingTop(8).Text("Conditions")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(100);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(70);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(4).Text("Name").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Code").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Status").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Notes").Bold().FontSize(9).FontColor(MutedColor);
                    });

                    for (var i = 0; i < profile.Conditions.Count; i++)
                    {
                        var cond = profile.Conditions[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                        var deletedSuffix = cond.IsDeleted ? " (Deleted)" : "";

                        table.Cell().Background(bgColor).Padding(4)
                            .Text($"{cond.Name}{deletedSuffix}").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(cond.Code ?? "—").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(cond.Status).FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(cond.Notes ?? "—").FontSize(8).LineHeight(1.1f);
                    }
                });
            }

            // Dietary Restrictions
            if (profile.DietaryRestrictions.Any())
            {
                innerCol.Item().PaddingTop(8).Text("Dietary Restrictions")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(4).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(150);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(4).Text("Restriction Type").Bold().FontSize(9).FontColor(MutedColor);
                        header.Cell().Padding(4).Text("Notes").Bold().FontSize(9).FontColor(MutedColor);
                    });

                    for (var i = 0; i < profile.DietaryRestrictions.Count; i++)
                    {
                        var dr = profile.DietaryRestrictions[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                        var deletedSuffix = dr.IsDeleted ? " (Deleted)" : "";

                        table.Cell().Background(bgColor).Padding(4)
                            .Text($"{dr.RestrictionType}{deletedSuffix}").FontSize(9);
                        table.Cell().Background(bgColor).Padding(4)
                            .Text(dr.Notes ?? "—").FontSize(9);
                    }
                });
            }

            if (!profile.Allergies.Any() && !profile.Medications.Any() && !profile.Conditions.Any() && !profile.DietaryRestrictions.Any())
            {
                innerCol.Item().Padding(8).Text("No health profile information recorded.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            }
        });
    }

    private static void ComposeAppointmentsSection(ColumnDescriptor column, List<AppointmentExportDto> appointments)
    {
        if (appointments.Count == 0)
        {
            ComposeSection(column, "Appointments", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No appointments found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Appointments", innerCol =>
        {
            innerCol.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(70);
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(50);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(60);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Padding(4).Text("Type").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Date/Time").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Duration").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Status").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Practitioner").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Notes").Bold().FontSize(8).FontColor(MutedColor);
                });

                for (var i = 0; i < appointments.Count; i++)
                {
                    var appt = appointments[i];
                    var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                    var deletedSuffix = appt.IsDeleted ? " (Deleted)" : "";

                    table.Cell().Background(bgColor).Padding(4)
                        .Text($"{appt.Type}{deletedSuffix}").FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(appt.StartTime.ToString("MMM d, H:mm")).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text($"{appt.DurationMinutes}m").FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(appt.Status).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(appt.NutritionistName).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(appt.Notes ?? "—").FontSize(8);
                }
            });
        });
    }

    private static void ComposeMealPlansSection(ColumnDescriptor column, List<MealPlanExportDto> plans)
    {
        if (plans.Count == 0)
        {
            ComposeSection(column, "Meal Plans", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No meal plans found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Meal Plans", innerCol =>
        {
            foreach (var plan in plans)
            {
                var deletedSuffix = plan.IsDeleted ? " (Deleted)" : "";

                innerCol.Item().PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"{plan.Title}{deletedSuffix}")
                            .FontSize(10).Bold();

                        col.Item().PaddingTop(2).Text(text =>
                        {
                            text.Span($"{plan.StartDate?.ToString("MMM d, yyyy") ?? "—"} — {plan.EndDate?.ToString("MMM d, yyyy") ?? "—"}")
                                .FontSize(8).FontColor(MutedColor);
                        });

                        if (plan.CalorieTarget.HasValue || plan.ProteinTargetG.HasValue || plan.CarbsTargetG.HasValue || plan.FatTargetG.HasValue)
                        {
                            var parts = new List<string>();
                            if (plan.CalorieTarget.HasValue) parts.Add($"{plan.CalorieTarget.Value:N0} kcal");
                            if (plan.ProteinTargetG.HasValue) parts.Add($"{plan.ProteinTargetG.Value:N0}g P");
                            if (plan.CarbsTargetG.HasValue) parts.Add($"{plan.CarbsTargetG.Value:N0}g C");
                            if (plan.FatTargetG.HasValue) parts.Add($"{plan.FatTargetG.Value:N0}g F");

                            col.Item().PaddingTop(2).Text(string.Join(" | ", parts))
                                .FontSize(8).FontColor(MutedColor);
                        }
                    });
                    row.RelativeItem().AlignRight().Text(plan.Status)
                        .FontSize(9);
                });

                // Days and meals
                foreach (var day in plan.Days)
                {
                    innerCol.Item().PaddingTop(4).PaddingHorizontal(8).Background("#f5f5f5").Padding(4)
                        .Text(day.Label ?? $"Day {day.DayNumber}")
                        .FontSize(9).Bold();

                    if (day.MealSlots.Count == 0)
                    {
                        innerCol.Item().PaddingHorizontal(12).PaddingVertical(2).Text("No meals for this day")
                            .FontSize(8).Italic().FontColor(MutedColor);
                    }
                    else
                    {
                        foreach (var slot in day.MealSlots)
                        {
                            innerCol.Item().PaddingTop(2).PaddingHorizontal(12).Column(slotCol =>
                            {
                                slotCol.Item().Text(slot.CustomName ?? slot.MealType)
                                    .FontSize(9).Bold();

                                if (slot.Items.Count > 0)
                                {
                                    slotCol.Item().PaddingTop(2).Table(table =>
                                    {
                                        table.ColumnsDefinition(columns =>
                                        {
                                            columns.RelativeColumn();
                                            columns.ConstantColumn(50);
                                            columns.ConstantColumn(50);
                                        });

                                        table.Header(header =>
                                        {
                                            header.Cell().Padding(2).Text("Food").Bold().FontSize(7).FontColor(MutedColor);
                                            header.Cell().Padding(2).AlignRight().Text("Qty").Bold().FontSize(7).FontColor(MutedColor);
                                            header.Cell().Padding(2).AlignRight().Text("Cal").Bold().FontSize(7).FontColor(MutedColor);
                                        });

                                        for (var i = 0; i < slot.Items.Count; i++)
                                        {
                                            var item = slot.Items[i];
                                            var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                                            table.Cell().Background(bgColor).Padding(2)
                                                .Text(item.FoodName).FontSize(7);
                                            table.Cell().Background(bgColor).Padding(2).AlignRight()
                                                .Text($"{item.Quantity} {item.Unit}").FontSize(7);
                                            table.Cell().Background(bgColor).Padding(2).AlignRight()
                                                .Text($"{item.CaloriesKcal:N0}").FontSize(7);
                                        }
                                    });
                                }
                            });
                        }
                    }
                }
            }
        });
    }

    private static void ComposeProgressGoalsSection(ColumnDescriptor column, List<ProgressGoalExportDto> goals)
    {
        if (goals.Count == 0)
        {
            ComposeSection(column, "Progress Goals", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No progress goals found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Progress Goals", innerCol =>
        {
            innerCol.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(80);
                    columns.ConstantColumn(60);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Padding(4).Text("Title").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Type").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Target").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Status").Bold().FontSize(8).FontColor(MutedColor);
                    header.Cell().Padding(4).Text("Created By").Bold().FontSize(8).FontColor(MutedColor);
                });

                for (var i = 0; i < goals.Count; i++)
                {
                    var goal = goals[i];
                    var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                    var deletedSuffix = goal.IsDeleted ? " (Deleted)" : "";
                    var targetDisplay = goal.TargetValue.HasValue
                        ? $"{goal.TargetValue} {goal.TargetUnit ?? ""}"
                        : goal.TargetDate?.ToString("MMM d, yyyy") ?? "—";

                    table.Cell().Background(bgColor).Padding(4)
                        .Text($"{goal.Title}{deletedSuffix}").FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(goal.GoalType).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(targetDisplay).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(goal.Status).FontSize(8);
                    table.Cell().Background(bgColor).Padding(4)
                        .Text(goal.CreatedByName).FontSize(8);
                }
            });
        });
    }

    private static void ComposeProgressEntriesSection(ColumnDescriptor column, List<ProgressEntryExportDto> entries)
    {
        if (entries.Count == 0)
        {
            ComposeSection(column, "Progress Entries", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No progress entries found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Progress Entries", innerCol =>
        {
            foreach (var entry in entries)
            {
                var deletedSuffix = entry.IsDeleted ? " (Deleted)" : "";

                innerCol.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"{entry.EntryDate:MMM d, yyyy}{deletedSuffix}")
                            .FontSize(9).Bold();
                        if (!string.IsNullOrEmpty(entry.Notes))
                        {
                            col.Item().PaddingTop(2).Text(entry.Notes)
                                .FontSize(8).LineHeight(1.2f);
                        }
                    });
                    row.RelativeItem().AlignRight().Text(entry.CreatedByName)
                        .FontSize(8).FontColor(MutedColor);
                });

                if (entry.Measurements.Any())
                {
                    innerCol.Item().PaddingTop(2).PaddingHorizontal(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(100);
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(50);
                        });

                        for (var i = 0; i < entry.Measurements.Count; i++)
                        {
                            var m = entry.Measurements[i];
                            var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";
                            var metricName = m.CustomMetricName ?? m.MetricType;

                            table.Cell().Background(bgColor).Padding(3)
                                .Text(metricName).FontSize(8);
                            table.Cell().Background(bgColor).Padding(3).AlignRight()
                                .Text(m.Value.ToString()).FontSize(8);
                            table.Cell().Background(bgColor).Padding(3)
                                .Text(m.Unit ?? "").FontSize(8);
                        }
                    });
                }
            }
        });
    }

    private static void ComposeIntakeFormsSection(ColumnDescriptor column, List<IntakeFormExportDto> forms)
    {
        if (forms.Count == 0)
        {
            ComposeSection(column, "Intake Forms", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No intake forms found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Intake Forms", innerCol =>
        {
            foreach (var form in forms)
            {
                var deletedSuffix = form.IsDeleted ? " (Deleted)" : "";

                innerCol.Item().PaddingTop(4).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"Status: {form.Status}{deletedSuffix}")
                            .FontSize(9).Bold();
                        if (form.SubmittedAt.HasValue)
                        {
                            col.Item().PaddingTop(2).Text($"Submitted: {form.SubmittedAt.Value:MMM d, yyyy}")
                                .FontSize(8).FontColor(MutedColor);
                        }
                        if (form.ReviewedAt.HasValue)
                        {
                            col.Item().Text($"Reviewed: {form.ReviewedAt.Value:MMM d, yyyy} by {form.ReviewedByName}")
                                .FontSize(8).FontColor(MutedColor);
                        }
                    });
                });

                if (form.Responses.Any())
                {
                    innerCol.Item().PaddingTop(2).PaddingHorizontal(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(80);
                            columns.ConstantColumn(80);
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Padding(3).Text("Section").Bold().FontSize(7).FontColor(MutedColor);
                            header.Cell().Padding(3).Text("Field").Bold().FontSize(7).FontColor(MutedColor);
                            header.Cell().Padding(3).Text("Value").Bold().FontSize(7).FontColor(MutedColor);
                        });

                        for (var i = 0; i < form.Responses.Count; i++)
                        {
                            var resp = form.Responses[i];
                            var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                            table.Cell().Background(bgColor).Padding(3)
                                .Text(resp.SectionKey).FontSize(7);
                            table.Cell().Background(bgColor).Padding(3)
                                .Text(resp.FieldKey).FontSize(7);
                            table.Cell().Background(bgColor).Padding(3)
                                .Text(resp.Value).FontSize(7);
                        }
                    });
                }
            }
        });
    }

    private static void ComposeConsentHistorySection(ColumnDescriptor column, ConsentHistoryExportDto consent)
    {
        ComposeSection(column, "Consent History", innerCol =>
        {
            // Consent Events
            if (consent.Events.Any())
            {
                innerCol.Item().PaddingTop(4).Text("Consent Events")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(80);
                        columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(3).Text("Type").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Purpose").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Version").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Date").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Recorded By").Bold().FontSize(7).FontColor(MutedColor);
                    });

                    for (var i = 0; i < consent.Events.Count; i++)
                    {
                        var evt = consent.Events[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                        table.Cell().Background(bgColor).Padding(3)
                            .Text(evt.EventType).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(evt.ConsentPurpose).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(evt.PolicyVersion).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(evt.Timestamp.ToString("MMM d")).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(evt.RecordedByName).FontSize(7);
                    }
                });
            }

            // Consent Forms
            if (consent.Forms.Any())
            {
                innerCol.Item().PaddingTop(6).Text("Consent Forms")
                    .FontSize(10).Bold().FontColor(PrimaryColor);

                innerCol.Item().PaddingTop(2).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(60);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(80);
                        columns.ConstantColumn(70);
                    });

                    table.Header(header =>
                    {
                        header.Cell().Padding(3).Text("Version").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Generated").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Signature Method").Bold().FontSize(7).FontColor(MutedColor);
                        header.Cell().Padding(3).Text("Signed").Bold().FontSize(7).FontColor(MutedColor);
                    });

                    for (var i = 0; i < consent.Forms.Count; i++)
                    {
                        var form = consent.Forms[i];
                        var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                        table.Cell().Background(bgColor).Padding(3)
                            .Text(form.FormVersion).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(form.GeneratedAt.ToString("MMM d, yyyy")).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(form.SignatureMethod).FontSize(7);
                        table.Cell().Background(bgColor).Padding(3)
                            .Text(form.IsSigned ? "Yes" : "No").FontSize(7);
                    }
                });
            }

            if (!consent.Events.Any() && !consent.Forms.Any())
            {
                innerCol.Item().Padding(8).Text("No consent records found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            }
        });
    }

    private static void ComposeAuditLogSection(ColumnDescriptor column, List<AuditLogExportDto> auditLog)
    {
        if (auditLog.Count == 0)
        {
            ComposeSection(column, "Audit Log", innerCol =>
            {
                innerCol.Item().Padding(8).Text("No audit log entries found.")
                    .FontSize(9).Italic().FontColor(MutedColor);
            });
            return;
        }

        ComposeSection(column, "Audit Log", innerCol =>
        {
            innerCol.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(100);
                    columns.ConstantColumn(60);
                    columns.RelativeColumn();
                });

                table.Header(header =>
                {
                    header.Cell().Padding(3).Text("Date/Time").Bold().FontSize(7).FontColor(MutedColor);
                    header.Cell().Padding(3).Text("Action").Bold().FontSize(7).FontColor(MutedColor);
                    header.Cell().Padding(3).Text("Entity").Bold().FontSize(7).FontColor(MutedColor);
                    header.Cell().Padding(3).Text("Details").Bold().FontSize(7).FontColor(MutedColor);
                });

                for (var i = 0; i < auditLog.Count; i++)
                {
                    var log = auditLog[i];
                    var bgColor = i % 2 == 1 ? AlternateRowColor : "#ffffff";

                    table.Cell().Background(bgColor).Padding(3)
                        .Text(log.Timestamp.ToString("MMM d, H:mm")).FontSize(7);
                    table.Cell().Background(bgColor).Padding(3)
                        .Text(log.Action).FontSize(7);
                    table.Cell().Background(bgColor).Padding(3)
                        .Text(log.EntityType).FontSize(7);
                    table.Cell().Background(bgColor).Padding(3)
                        .Text(log.Details ?? "—").FontSize(7);
                }
            });
        });
    }

    private static void ComposeSection(ColumnDescriptor column, string title, Action<ColumnDescriptor> content)
    {
        column.Item().PaddingTop(12).Column(section =>
        {
            section.Item().Text(title)
                .FontSize(12).Bold().FontColor(PrimaryColor);
            section.Item().PaddingTop(8).Column(c => content(c));
        });
    }

    private static void ComposeFooter(IContainer container, ClientDataExportDto data)
    {
        container.BorderTop(1).BorderColor("#e0e0e0").PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span("CONFIDENTIAL — Generated ").FontSize(8).FontColor(MutedColor);
                text.Span(data.ExportMetadata.ExportDate.ToString("MMMM d, yyyy")).FontSize(8).FontColor(MutedColor);
            });
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor(MutedColor);
                text.CurrentPageNumber().FontSize(8).FontColor(MutedColor);
                text.Span(" of ").FontSize(8).FontColor(MutedColor);
                text.TotalPages().FontSize(8).FontColor(MutedColor);
            });
        });
    }
}
