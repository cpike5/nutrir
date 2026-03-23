using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using QuestPDF.Infrastructure;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class DataExportServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly IAuditLogService _auditLogService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DataExportService _sut;

    private const string NutritionistId = "nutritionist-export-test-001";
    private const string GeneratingUserId = "generating-user-export-001";

    private int _seededClientId;

    public DataExportServiceTests()
    {
        // QuestPDF requires a license type to be configured before generating any document.
        // The Community license is appropriate for open-source / non-commercial projects.
        QuestPDF.Settings.License = LicenseType.Community;

        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();

        var userStore = Substitute.For<IUserStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            userStore, null, null, null, null, null, null, null, null);

        // The service calls FindByIdAsync only for the generating user;
        // all other user display names are resolved via EF queries.
        _userManager.FindByIdAsync(GeneratingUserId)
            .Returns(Task.FromResult<ApplicationUser?>(new ApplicationUser
            {
                Id = GeneratingUserId,
                UserName = "generator@exporttest.com",
                NormalizedUserName = "GENERATOR@EXPORTTEST.COM",
                Email = "generator@exporttest.com",
                NormalizedEmail = "GENERATOR@EXPORTTEST.COM",
                FirstName = "Gen",
                LastName = "User",
                DisplayName = "Gen User",
                CreatedDate = DateTime.UtcNow
            }));

        var factory = new SharedConnectionContextFactory(_connection);

        _sut = new DataExportService(
            factory,
            _auditLogService,
            _userManager,
            NullLogger<DataExportService>.Instance);

        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        // Nutritionist user — seeded into ApplicationUsers table so the service can
        // resolve the display name via EF (db.Set<ApplicationUser>()).
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@exporttest.com",
            NormalizedUserName = "NUTRITIONIST@EXPORTTEST.COM",
            Email = "nutritionist@exporttest.com",
            NormalizedEmail = "NUTRITIONIST@EXPORTTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Export",
            Email = "alice@example.com",
            Phone = "555-0200",
            DateOfBirth = new DateOnly(1990, 3, 15),
            Notes = "Test client notes",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow.AddDays(-30),
            ConsentPolicyVersion = "1.0",
            RetentionYears = 7,
            IsPurged = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;

        // Appointment
        _dbContext.Appointments.Add(new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(7),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        });

        // Meal plan with Day -> Slot -> Item hierarchy
        var mealPlan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Test Meal Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Days =
            [
                new MealPlanDay
                {
                    DayNumber = 1,
                    Label = "Monday",
                    MealSlots =
                    [
                        new MealSlot
                        {
                            MealType = MealType.Breakfast,
                            SortOrder = 0,
                            Items =
                            [
                                new MealItem
                                {
                                    FoodName = "Oatmeal",
                                    Quantity = 1,
                                    Unit = "cup",
                                    CaloriesKcal = 300,
                                    ProteinG = 10,
                                    CarbsG = 54,
                                    FatG = 6,
                                    SortOrder = 0
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        _dbContext.MealPlans.Add(mealPlan);

        // Progress goal
        _dbContext.ProgressGoals.Add(new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Lose 10 lbs",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        });

        // Progress entry with measurement
        var progressEntry = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "Feeling good",
            CreatedAt = DateTime.UtcNow,
            Measurements =
            [
                new ProgressMeasurement
                {
                    MetricType = MetricType.Weight,
                    Value = 185.5m,
                    Unit = "lbs"
                }
            ]
        };
        _dbContext.ProgressEntries.Add(progressEntry);

        // Intake form with response
        var intakeForm = new IntakeForm
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Token = "test-token-001",
            Status = IntakeFormStatus.Pending,
            ClientEmail = "alice@example.com",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            Responses =
            [
                new IntakeFormResponse
                {
                    SectionKey = "personal",
                    FieldKey = "occupation",
                    Value = "Teacher"
                }
            ]
        };
        _dbContext.IntakeForms.Add(intakeForm);

        // Consent event
        _dbContext.ConsentEvents.Add(new ConsentEvent
        {
            ClientId = _seededClientId,
            EventType = ConsentEventType.ConsentGiven,
            ConsentPurpose = "Nutritional counselling",
            PolicyVersion = "1.0",
            RecordedByUserId = NutritionistId,
            Timestamp = DateTime.UtcNow.AddDays(-30)
        });

        // Consent form
        _dbContext.ConsentForms.Add(new ConsentForm
        {
            ClientId = _seededClientId,
            FormVersion = "1.0",
            GeneratedByUserId = NutritionistId,
            SignatureMethod = ConsentSignatureMethod.Digital,
            IsSigned = true,
            SignedAt = DateTime.UtcNow.AddDays(-29),
            GeneratedAt = DateTime.UtcNow.AddDays(-30),
            CreatedAt = DateTime.UtcNow.AddDays(-30)
        });

        // Health profile entities
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Peanuts",
            Severity = AllergySeverity.Moderate,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.ClientMedications.Add(new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Vitamin D",
            Dosage = "1000 IU",
            Frequency = "Daily",
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.ClientConditions.Add(new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Type 2 Diabetes",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        });

        _dbContext.ClientDietaryRestrictions.Add(new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.GlutenFree,
            CreatedAt = DateTime.UtcNow
        });

        // Audit log entries targeting the seeded client
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = NutritionistId,
            Action = "ClientCreated",
            EntityType = "Client",
            EntityId = _seededClientId.ToString(),
            Details = "Initial client record created",
            Timestamp = DateTime.UtcNow.AddDays(-30),
            Source = AuditSource.Web
        });

        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — ClientProfile
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_ReturnsClientProfile()
    {
        // Arrange — client was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.ClientProfile.FirstName.Should().Be("Alice");
        result.ClientProfile.LastName.Should().Be("Export");
        result.ClientProfile.Email.Should().Be("alice@example.com");
        result.ClientProfile.ConsentGiven.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Appointments
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesAppointments()
    {
        // Arrange — appointment was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.Appointments.Should().HaveCount(1, because: "one appointment was seeded");
        var appointment = result.Appointments.Single();
        appointment.Type.Should().Be(AppointmentType.InitialConsultation.ToString());
        appointment.Status.Should().Be(AppointmentStatus.Scheduled.ToString());
        appointment.Location.Should().Be(AppointmentLocation.InPerson.ToString());
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Meal Plans with hierarchy
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesMealPlansWithHierarchy()
    {
        // Arrange — meal plan with day/slot/item hierarchy was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — plan
        result.MealPlans.Should().HaveCount(1, because: "one meal plan was seeded");
        var plan = result.MealPlans.Single();
        plan.Title.Should().Be("Test Meal Plan");
        plan.Status.Should().Be(MealPlanStatus.Draft.ToString());

        // Assert — day
        plan.Days.Should().HaveCount(1, because: "one day was seeded");
        var day = plan.Days.Single();
        day.DayNumber.Should().Be(1);
        day.Label.Should().Be("Monday");

        // Assert — slot
        day.MealSlots.Should().HaveCount(1, because: "one meal slot was seeded");
        var slot = day.MealSlots.Single();
        slot.MealType.Should().Be(MealType.Breakfast.ToString());

        // Assert — item
        slot.Items.Should().HaveCount(1, because: "one meal item was seeded");
        var item = slot.Items.Single();
        item.FoodName.Should().Be("Oatmeal");
        item.CaloriesKcal.Should().Be(300);
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Progress Goals
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesProgressGoals()
    {
        // Arrange — progress goal was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.ProgressGoals.Should().HaveCount(1, because: "one progress goal was seeded");
        var goal = result.ProgressGoals.Single();
        goal.Title.Should().Be("Lose 10 lbs");
        goal.GoalType.Should().Be(GoalType.Weight.ToString());
        goal.Status.Should().Be(GoalStatus.Active.ToString());
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Progress Entries with Measurements
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesProgressEntriesWithMeasurements()
    {
        // Arrange — progress entry with measurement was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — entry
        result.ProgressEntries.Should().HaveCount(1, because: "one progress entry was seeded");
        var entry = result.ProgressEntries.Single();
        entry.Notes.Should().Be("Feeling good");

        // Assert — measurement
        entry.Measurements.Should().HaveCount(1, because: "one measurement was seeded");
        var measurement = entry.Measurements.Single();
        measurement.MetricType.Should().Be(MetricType.Weight.ToString());
        measurement.Value.Should().Be(185.5m);
        measurement.Unit.Should().Be("lbs");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Intake Forms with Responses
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesIntakeFormsWithResponses()
    {
        // Arrange — intake form with response was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — form
        result.IntakeForms.Should().HaveCount(1, because: "one intake form was seeded");
        var form = result.IntakeForms.Single();
        form.Status.Should().Be(IntakeFormStatus.Pending.ToString());

        // Assert — response
        form.Responses.Should().HaveCount(1, because: "one intake form response was seeded");
        var response = form.Responses.Single();
        response.SectionKey.Should().Be("personal");
        response.FieldKey.Should().Be("occupation");
        response.Value.Should().Be("Teacher");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Consent Events
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesConsentEvents()
    {
        // Arrange — consent event was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.ConsentHistory.Events.Should().HaveCount(1, because: "one consent event was seeded");
        var evt = result.ConsentHistory.Events.Single();
        evt.EventType.Should().Be(ConsentEventType.ConsentGiven.ToString());
        evt.ConsentPurpose.Should().Be("Nutritional counselling");
        evt.PolicyVersion.Should().Be("1.0");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Consent Forms
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesConsentForms()
    {
        // Arrange — consent form was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.ConsentHistory.Forms.Should().HaveCount(1, because: "one consent form was seeded");
        var form = result.ConsentHistory.Forms.Single();
        form.FormVersion.Should().Be("1.0");
        form.SignatureMethod.Should().Be(ConsentSignatureMethod.Digital.ToString());
        form.IsSigned.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Health Profile
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesHealthProfile()
    {
        // Arrange — allergies/medications/conditions/dietary restrictions seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — allergy
        result.HealthProfile.Allergies.Should().HaveCount(1, because: "one allergy was seeded");
        var allergy = result.HealthProfile.Allergies.Single();
        allergy.Name.Should().Be("Peanuts");
        allergy.Severity.Should().Be(AllergySeverity.Moderate.ToString());
        allergy.AllergyType.Should().Be(AllergyType.Food.ToString());

        // Assert — medication
        result.HealthProfile.Medications.Should().HaveCount(1, because: "one medication was seeded");
        var medication = result.HealthProfile.Medications.Single();
        medication.Name.Should().Be("Vitamin D");
        medication.Dosage.Should().Be("1000 IU");

        // Assert — condition
        result.HealthProfile.Conditions.Should().HaveCount(1, because: "one condition was seeded");
        var condition = result.HealthProfile.Conditions.Single();
        condition.Name.Should().Be("Type 2 Diabetes");
        condition.Status.Should().Be(ConditionStatus.Active.ToString());

        // Assert — dietary restriction
        result.HealthProfile.DietaryRestrictions.Should().HaveCount(1, because: "one dietary restriction was seeded");
        var restriction = result.HealthProfile.DietaryRestrictions.Single();
        restriction.RestrictionType.Should().Be(DietaryRestrictionType.GlutenFree.ToString());
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Audit Log Entries
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesClientAuditLogEntries()
    {
        // Arrange — AuditLogEntry for the client was seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — the seeded client-level audit entry should appear
        result.AuditLog.Should().ContainSingle(e =>
            e.Action == "ClientCreated" && e.EntityType == "Client",
            because: "exactly one client-level audit entry was seeded");

        var auditEntry = result.AuditLog.Single(e => e.EntityType == "Client");
        auditEntry.EntityId.Should().Be(_seededClientId.ToString());
        auditEntry.Source.Should().Be(AuditSource.Web.ToString());
    }

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesSubEntityAuditLogEntries()
    {
        // Arrange — seed an audit entry for the appointment sub-entity
        var appointment = _dbContext.Appointments.First(a => a.ClientId == _seededClientId);
        _dbContext.AuditLogEntries.Add(new AuditLogEntry
        {
            UserId = NutritionistId,
            Action = "AppointmentCreated",
            EntityType = "Appointment",
            EntityId = appointment.Id.ToString(),
            Details = "Appointment scheduled",
            Timestamp = DateTime.UtcNow,
            Source = AuditSource.Web
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — both the client-level and sub-entity audit entries should appear
        result.AuditLog.Should().HaveCount(2,
            because: "one client-level and one appointment-level audit entry were seeded");
        result.AuditLog.Should().ContainSingle(e =>
            e.EntityType == "Appointment" && e.Action == "AppointmentCreated",
            because: "the sub-entity audit entry should be included via the second-pass query");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Export Metadata
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_IncludesExportMetadata()
    {
        // Arrange — client seeded in constructor

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId, "json");

        // Assert
        result.ExportMetadata.ExportVersion.Should().Be("1.0");
        result.ExportMetadata.ExportFormat.Should().Be("json");
        result.ExportMetadata.ClientId.Should().Be(_seededClientId);
        result.ExportMetadata.PipedaNotice.Should().NotBeNullOrEmpty(
            because: "PIPEDA notice is required for compliance");
        result.ExportMetadata.ExportDate.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(30));
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — User Display Name Resolution
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithValidClient_ResolvesUserDisplayNames()
    {
        // Arrange — nutritionist seeded in the DB with DisplayName "Jane Smith";
        // the service resolves the primary nutritionist name via EF query.

        // Act
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert — nutritionist name on the client profile
        result.ClientProfile.PrimaryNutritionistName.Should().Be("Jane Smith",
            because: "the nutritionist was seeded in the DB with DisplayName 'Jane Smith'");

        // Assert — generating user name comes from UserManager.FindByIdAsync
        result.ExportMetadata.GeneratedByName.Should().Be("Gen User",
            because: "the generating user's DisplayName is 'Gen User' as configured on the mock");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Non-existent client
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithNonExistentClient_ThrowsKeyNotFoundException()
    {
        // Arrange
        const int nonExistentClientId = 999_901;

        // Act
        var act = async () => await _sut.CollectClientDataAsync(nonExistentClientId, GeneratingUserId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "the service throws KeyNotFoundException when the client does not exist");
    }

    // ---------------------------------------------------------------------------
    // CollectClientDataAsync — Soft-deleted client (IgnoreQueryFilters)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CollectClientDataAsync_WithSoftDeletedClient_StillReturnsData()
    {
        // Arrange — soft-delete the seeded client directly in the DB context
        var client = await _dbContext.Clients.FindAsync(_seededClientId);
        client!.IsDeleted = true;
        client.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act — the service uses IgnoreQueryFilters(), so soft-deleted clients are accessible
        var result = await _sut.CollectClientDataAsync(_seededClientId, GeneratingUserId);

        // Assert
        result.ClientProfile.IsDeleted.Should().BeTrue(
            because: "the client was soft-deleted");
        result.ClientProfile.FirstName.Should().Be("Alice",
            because: "the soft-deleted client's data should still be exported");
    }

    // ---------------------------------------------------------------------------
    // ExportAsJsonAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsJsonAsync_ReturnsNonEmptyByteArray()
    {
        // Arrange — client seeded in constructor

        // Act
        var bytes = await _sut.ExportAsJsonAsync(_seededClientId, GeneratingUserId);

        // Assert
        bytes.Should().NotBeNullOrEmpty(because: "a non-empty JSON export should be returned");
    }

    [Fact]
    public async Task ExportAsJsonAsync_ReturnsValidJsonContainingClientData()
    {
        // Arrange — client seeded in constructor

        // Act
        var bytes = await _sut.ExportAsJsonAsync(_seededClientId, GeneratingUserId);
        var json = Encoding.UTF8.GetString(bytes);

        // Assert — valid JSON that can be parsed
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Assert — client profile data is present (camelCase per service config)
        root.GetProperty("clientProfile").GetProperty("firstName").GetString()
            .Should().Be("Alice", because: "the client's first name should appear in the JSON");
        root.GetProperty("clientProfile").GetProperty("lastName").GetString()
            .Should().Be("Export", because: "the client's last name should appear in the JSON");

        // Assert — export metadata is present
        root.GetProperty("exportMetadata").GetProperty("exportVersion").GetString()
            .Should().Be("1.0", because: "the export metadata should include the version");
    }

    [Fact]
    public async Task ExportAsJsonAsync_CallsAuditLogService()
    {
        // Arrange — client seeded in constructor

        // Act
        await _sut.ExportAsJsonAsync(_seededClientId, GeneratingUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            GeneratingUserId,
            "ClientDataExported",
            "Client",
            _seededClientId.ToString(),
            "Exported as JSON");
    }

    [Fact]
    public async Task ExportAsJsonAsync_WithNonExistentClient_ThrowsKeyNotFoundException()
    {
        // Arrange
        const int nonExistentClientId = 999_901;

        // Act
        var act = async () => await _sut.ExportAsJsonAsync(nonExistentClientId, GeneratingUserId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "ExportAsJsonAsync delegates to CollectClientDataAsync which throws for missing clients");
    }

    // ---------------------------------------------------------------------------
    // ExportAsPdfAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExportAsPdfAsync_ReturnsNonEmptyByteArray()
    {
        // Arrange — client seeded in constructor

        // Act
        var bytes = await _sut.ExportAsPdfAsync(_seededClientId, GeneratingUserId);

        // Assert
        bytes.Should().NotBeNullOrEmpty(because: "a non-empty PDF should be returned");
    }

    [Fact]
    public async Task ExportAsPdfAsync_CallsAuditLogService()
    {
        // Arrange — client seeded in constructor

        // Act
        await _sut.ExportAsPdfAsync(_seededClientId, GeneratingUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            GeneratingUserId,
            "ClientDataExported",
            "Client",
            _seededClientId.ToString(),
            "Exported as PDF");
    }

    [Fact]
    public async Task ExportAsPdfAsync_WithNonExistentClient_ThrowsKeyNotFoundException()
    {
        // Arrange
        const int nonExistentClientId = 999_901;

        // Act
        var act = async () => await _sut.ExportAsPdfAsync(nonExistentClientId, GeneratingUserId);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>(
            because: "ExportAsPdfAsync delegates to CollectClientDataAsync which throws for missing clients");
    }
}
