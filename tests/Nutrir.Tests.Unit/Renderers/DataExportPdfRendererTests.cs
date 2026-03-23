using FluentAssertions;
using Nutrir.Core.DTOs;
using Nutrir.Infrastructure.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace Nutrir.Tests.Unit.Renderers;

public class DataExportPdfRendererTests
{
    public DataExportPdfRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ---------------------------------------------------------------------------
    // Helpers — build test DTOs
    // ---------------------------------------------------------------------------

    private static ExportMetadataDto BuildMetadata() => new(
        ExportDate: new DateTime(2024, 9, 15, 12, 0, 0, DateTimeKind.Utc),
        ExportVersion: "1.0",
        ExportFormat: "pdf",
        ClientId: 42,
        GeneratedByName: "Dr. Sarah Green, RD",
        PipedaNotice: "This export is provided in compliance with PIPEDA.");

    private static ClientProfileExportDto BuildClientProfile() => new(
        FirstName: "Jane",
        LastName: "Doe",
        Email: "jane.doe@example.com",
        Phone: "555-0100",
        DateOfBirth: new DateOnly(1985, 4, 20),
        Notes: "Long-term client, excellent compliance.",
        ConsentGiven: true,
        ConsentTimestamp: new DateTime(2023, 1, 10, 9, 0, 0, DateTimeKind.Utc),
        ConsentPolicyVersion: "1.0",
        PrimaryNutritionistName: "Dr. Sarah Green, RD",
        IsDeleted: false,
        CreatedAt: new DateTime(2023, 1, 10, 9, 0, 0, DateTimeKind.Utc),
        UpdatedAt: new DateTime(2024, 3, 5, 11, 0, 0, DateTimeKind.Utc),
        DeletedAt: null);

    private static HealthProfileExportDto BuildHealthProfile() => new(
        Allergies:
        [
            new AllergyExportDto("Peanuts", "Moderate", "Food", IsDeleted: false, DeletedAt: null)
        ],
        Medications:
        [
            new MedicationExportDto("Vitamin D", "1000 IU", "Daily", "Bone health", IsDeleted: false, DeletedAt: null)
        ],
        Conditions:
        [
            new ConditionExportDto("Type 2 Diabetes", "E11", new DateOnly(2020, 6, 1), "Active", "Well controlled", IsDeleted: false, DeletedAt: null)
        ],
        DietaryRestrictions:
        [
            new DietaryRestrictionExportDto("GlutenFree", "Coeliac disease", IsDeleted: false, DeletedAt: null)
        ]);

    private static List<AppointmentExportDto> BuildAppointments() =>
    [
        new AppointmentExportDto(
            Type: "InitialConsultation",
            Status: "Completed",
            StartTime: new DateTime(2023, 2, 14, 10, 0, 0, DateTimeKind.Utc),
            DurationMinutes: 60,
            Location: "InPerson",
            LocationNotes: "Main office",
            Notes: "Went well.",
            NutritionistName: "Dr. Sarah Green, RD",
            CancellationReason: null,
            CancelledAt: null,
            IsDeleted: false,
            DeletedAt: null,
            CreatedAt: new DateTime(2023, 2, 1, 9, 0, 0, DateTimeKind.Utc))
    ];

    private static List<MealPlanExportDto> BuildMealPlans() =>
    [
        new MealPlanExportDto(
            Title: "Spring Reset Plan",
            Description: "A clean-eating plan for spring.",
            Status: "Active",
            StartDate: new DateOnly(2024, 3, 1),
            EndDate: new DateOnly(2024, 3, 7),
            CalorieTarget: 1800m,
            ProteinTargetG: 130m,
            CarbsTargetG: 200m,
            FatTargetG: 60m,
            Notes: "Practitioner notes.",
            Instructions: "Drink 2L of water daily.",
            CreatedByName: "Dr. Sarah Green, RD",
            Days:
            [
                new MealPlanDayExportDto(
                    DayNumber: 1,
                    Label: "Monday",
                    Notes: null,
                    MealSlots:
                    [
                        new MealSlotExportDto(
                            MealType: "Breakfast",
                            CustomName: null,
                            Notes: null,
                            Items:
                            [
                                new MealItemExportDto("Oatmeal", 1m, "cup", 320m, 12m, 55m, 6m, null)
                            ])
                    ])
            ],
            IsDeleted: false,
            DeletedAt: null,
            CreatedAt: new DateTime(2024, 2, 25, 10, 0, 0, DateTimeKind.Utc))
    ];

    private static List<ProgressGoalExportDto> BuildProgressGoals() =>
    [
        new ProgressGoalExportDto(
            Title: "Lose 10 lbs",
            Description: "Gradual weight loss over 3 months.",
            GoalType: "Weight",
            TargetValue: 165m,
            TargetUnit: "lbs",
            TargetDate: new DateOnly(2024, 6, 1),
            Status: "Active",
            CreatedByName: "Dr. Sarah Green, RD",
            IsDeleted: false,
            DeletedAt: null,
            CreatedAt: new DateTime(2024, 1, 15, 9, 0, 0, DateTimeKind.Utc))
    ];

    private static List<ProgressEntryExportDto> BuildProgressEntries() =>
    [
        new ProgressEntryExportDto(
            EntryDate: new DateOnly(2024, 4, 10),
            Notes: "Feeling great this week.",
            CreatedByName: "Dr. Sarah Green, RD",
            Measurements:
            [
                new ProgressMeasurementExportDto("Weight", null, 175m, "lbs"),
                new ProgressMeasurementExportDto("BloodPressureSystolic", null, 118m, "mmHg")
            ],
            IsDeleted: false,
            DeletedAt: null,
            CreatedAt: new DateTime(2024, 4, 10, 14, 0, 0, DateTimeKind.Utc))
    ];

    private static List<IntakeFormExportDto> BuildIntakeForms() =>
    [
        new IntakeFormExportDto(
            Status: "Submitted",
            SubmittedAt: new DateTime(2023, 1, 5, 11, 0, 0, DateTimeKind.Utc),
            ReviewedAt: new DateTime(2023, 1, 8, 10, 0, 0, DateTimeKind.Utc),
            ReviewedByName: "Dr. Sarah Green, RD",
            CreatedByName: "System",
            Responses:
            [
                new IntakeFormResponseExportDto("personal", "occupation", "Teacher"),
                new IntakeFormResponseExportDto("health", "current_weight", "185")
            ],
            IsDeleted: false,
            DeletedAt: null,
            CreatedAt: new DateTime(2023, 1, 3, 8, 0, 0, DateTimeKind.Utc))
    ];

    private static ConsentHistoryExportDto BuildConsentHistory() => new(
        Events:
        [
            new ConsentEventExportDto(
                EventType: "ConsentGiven",
                ConsentPurpose: "Nutritional counselling",
                PolicyVersion: "1.0",
                Timestamp: new DateTime(2023, 1, 10, 9, 0, 0, DateTimeKind.Utc),
                RecordedByName: "Dr. Sarah Green, RD",
                Notes: null)
        ],
        Forms:
        [
            new ConsentFormExportDto(
                FormVersion: "1.0",
                GeneratedAt: new DateTime(2023, 1, 10, 8, 30, 0, DateTimeKind.Utc),
                GeneratedByName: "Dr. Sarah Green, RD",
                SignatureMethod: "Digital",
                IsSigned: true,
                SignedAt: new DateTime(2023, 1, 10, 9, 5, 0, DateTimeKind.Utc),
                SignedByName: "Jane Doe",
                Notes: null,
                CreatedAt: new DateTime(2023, 1, 10, 8, 30, 0, DateTimeKind.Utc))
        ]);

    private static List<AuditLogExportDto> BuildAuditLog() =>
    [
        new AuditLogExportDto(
            Timestamp: new DateTime(2023, 1, 10, 9, 0, 0, DateTimeKind.Utc),
            Action: "ClientCreated",
            EntityType: "Client",
            EntityId: "42",
            Details: "Initial client record created",
            Source: "Web"),
        new AuditLogExportDto(
            Timestamp: new DateTime(2024, 9, 15, 12, 0, 0, DateTimeKind.Utc),
            Action: "ClientDataExported",
            EntityType: "Client",
            EntityId: "42",
            Details: "Exported as PDF",
            Source: "Web")
    ];

    // ---------------------------------------------------------------------------
    // Render — fully populated export DTO
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithFullyPopulatedData_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var data = new ClientDataExportDto(
            ExportMetadata: BuildMetadata(),
            ClientProfile: BuildClientProfile(),
            HealthProfile: BuildHealthProfile(),
            Appointments: BuildAppointments(),
            MealPlans: BuildMealPlans(),
            ProgressGoals: BuildProgressGoals(),
            ProgressEntries: BuildProgressEntries(),
            IntakeForms: BuildIntakeForms(),
            ConsentHistory: BuildConsentHistory(),
            AuditLog: BuildAuditLog());

        // Act
        var result = DataExportPdfRenderer.Render(data);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a fully-populated client data export should produce a valid PDF");
    }

    // ---------------------------------------------------------------------------
    // Render — minimal / sparse export DTO (empty collections, no optional fields)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithMinimalData_ReturnsNonEmptyByteArray()
    {
        // Arrange — all collection properties are empty; optional fields are null
        var data = new ClientDataExportDto(
            ExportMetadata: new ExportMetadataDto(
                ExportDate: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ExportVersion: "1.0",
                ExportFormat: "pdf",
                ClientId: 1,
                GeneratedByName: "System",
                PipedaNotice: "PIPEDA notice."),
            ClientProfile: new ClientProfileExportDto(
                FirstName: "Test",
                LastName: "Client",
                Email: null,
                Phone: null,
                DateOfBirth: null,
                Notes: null,
                ConsentGiven: false,
                ConsentTimestamp: null,
                ConsentPolicyVersion: null,
                PrimaryNutritionistName: "Unknown",
                IsDeleted: false,
                CreatedAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt: null,
                DeletedAt: null),
            HealthProfile: new HealthProfileExportDto(
                Allergies: [],
                Medications: [],
                Conditions: [],
                DietaryRestrictions: []),
            Appointments: [],
            MealPlans: [],
            ProgressGoals: [],
            ProgressEntries: [],
            IntakeForms: [],
            ConsentHistory: new ConsentHistoryExportDto(Events: [], Forms: []),
            AuditLog: []);

        // Act
        var result = DataExportPdfRenderer.Render(data);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a sparse export with empty collections should still produce a valid PDF structure");
    }

    // ---------------------------------------------------------------------------
    // Render — meal plan with nested day/slot/item hierarchy
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithMealPlanHierarchy_ReturnsNonEmptyByteArray()
    {
        // Arrange — focus on a multi-day plan with multiple slots and items
        var data = new ClientDataExportDto(
            ExportMetadata: BuildMetadata(),
            ClientProfile: BuildClientProfile(),
            HealthProfile: new HealthProfileExportDto([], [], [], []),
            Appointments: [],
            MealPlans:
            [
                new MealPlanExportDto(
                    Title: "Complex Plan",
                    Description: null,
                    Status: "Draft",
                    StartDate: null,
                    EndDate: null,
                    CalorieTarget: null,
                    ProteinTargetG: null,
                    CarbsTargetG: null,
                    FatTargetG: null,
                    Notes: null,
                    Instructions: null,
                    CreatedByName: "Nutritionist",
                    Days:
                    [
                        new MealPlanDayExportDto(1, "Day 1", null,
                        [
                            new MealSlotExportDto("Breakfast", null, null,
                            [
                                new MealItemExportDto("Eggs", 2m, "large", 156m, 12m, 1m, 11m, null),
                                new MealItemExportDto("Toast", 1m, "slice", 79m, 3m, 15m, 1m, "Whole grain")
                            ]),
                            new MealSlotExportDto("Lunch", null, null, [])
                        ]),
                        new MealPlanDayExportDto(2, null, null, [])
                    ],
                    IsDeleted: false,
                    DeletedAt: null,
                    CreatedAt: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ],
            ProgressGoals: [],
            ProgressEntries: [],
            IntakeForms: [],
            ConsentHistory: new ConsentHistoryExportDto([], []),
            AuditLog: []);

        // Act
        var result = DataExportPdfRenderer.Render(data);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a meal plan export with nested day/slot/item hierarchy should render successfully");
    }
}
