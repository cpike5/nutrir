using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class DataPurgeServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly IAuditLogService _auditLogService;
    private readonly DataPurgeService _sut;

    private const string NutritionistId = "nutritionist-purge-test-001";
    private const string ActingUserId = "acting-user-purge-001";

    private int _seededClientId;

    public DataPurgeServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();

        var factory = new SharedConnectionContextFactory(_connection);

        _sut = new DataPurgeService(
            factory,
            _auditLogService,
            NullLogger<DataPurgeService>.Instance);

        SeedBaseData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedBaseData()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@purgetest.com",
            NormalizedUserName = "NUTRITIONIST@PURGETEST.COM",
            Email = "nutritionist@purgetest.com",
            NormalizedEmail = "NUTRITIONIST@PURGETEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Purge",
            Email = "alice@example.com",
            Phone = "555-0100",
            DateOfBirth = new DateOnly(1985, 6, 15),
            Notes = "Some notes",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            RetentionYears = 7,
            LastInteractionDate = DateTime.UtcNow.AddYears(-1),
            RetentionExpiresAt = DateTime.UtcNow.AddDays(30),
            IsPurged = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    private Client AddClient(
        string firstName,
        string lastName,
        DateTime? retentionExpiresAt = null,
        bool isPurged = false,
        bool isDeleted = false,
        DateTime? lastInteractionDate = null)
    {
        var client = new Client
        {
            FirstName = firstName,
            LastName = lastName,
            Email = $"{firstName.ToLower()}@example.com",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            RetentionYears = 7,
            RetentionExpiresAt = retentionExpiresAt,
            LastInteractionDate = lastInteractionDate ?? DateTime.UtcNow.AddYears(-1),
            IsPurged = isPurged,
            IsDeleted = isDeleted,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();
        return client;
    }

    // ---------------------------------------------------------------------------
    // GetExpiringClientsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetExpiringClientsAsync_ReturnsClientsWhoseRetentionExpiresWithinWindow()
    {
        // Arrange — add a client expiring in 45 days (within default 90-day window)
        var expiringClient = AddClient("Bob", "Expiring",
            retentionExpiresAt: DateTime.UtcNow.AddDays(45));

        // Act
        var result = await _sut.GetExpiringClientsAsync(withinDays: 90);

        // Assert — both the seeded client (30 days) and new client (45 days) should appear
        var ids = result.Select(r => r.ClientId).ToList();
        ids.Should().Contain(expiringClient.Id,
            because: "a client expiring in 45 days should appear within a 90-day window");
        ids.Should().Contain(_seededClientId,
            because: "the seeded client expiring in 30 days should also appear");
    }

    [Fact]
    public async Task GetExpiringClientsAsync_DoesNotReturnAlreadyExpiredClients()
    {
        // Arrange — client whose retention already expired yesterday
        var expiredClient = AddClient("Carol", "Expired",
            retentionExpiresAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _sut.GetExpiringClientsAsync(withinDays: 90);

        // Assert
        result.Should().NotContain(r => r.ClientId == expiredClient.Id,
            because: "clients whose retention already expired are not 'expiring', they are 'expired'");
    }

    [Fact]
    public async Task GetExpiringClientsAsync_DoesNotReturnAlreadyPurgedClients()
    {
        // Arrange — purged client expiring within the window
        var purgedClient = AddClient("Dave", "Purged",
            retentionExpiresAt: DateTime.UtcNow.AddDays(20),
            isPurged: true);

        // Act
        var result = await _sut.GetExpiringClientsAsync(withinDays: 90);

        // Assert
        result.Should().NotContain(r => r.ClientId == purgedClient.Id,
            because: "already-purged clients should not appear in the expiring list");
    }

    [Fact]
    public async Task GetExpiringClientsAsync_DoesNotReturnSoftDeletedClients()
    {
        // Arrange — soft-deleted client expiring within the window
        var deletedClient = AddClient("Eve", "Deleted",
            retentionExpiresAt: DateTime.UtcNow.AddDays(20),
            isDeleted: true);

        // Act
        var result = await _sut.GetExpiringClientsAsync(withinDays: 90);

        // Assert
        result.Should().NotContain(r => r.ClientId == deletedClient.Id,
            because: "soft-deleted clients should not appear in the expiring list");
    }

    [Fact]
    public async Task GetExpiringClientsAsync_ReturnsResultsOrderedByRetentionExpiresAtAscending()
    {
        // Arrange — add two clients with different expiry dates; earlier one should come first
        var laterClient = AddClient("Frank", "Later", retentionExpiresAt: DateTime.UtcNow.AddDays(80));
        var earlierClient = AddClient("Grace", "Earlier", retentionExpiresAt: DateTime.UtcNow.AddDays(10));

        // Act
        var result = await _sut.GetExpiringClientsAsync(withinDays: 90);

        // Assert — filter to just these two clients for a deterministic ordering check
        var subset = result
            .Where(r => r.ClientId == earlierClient.Id || r.ClientId == laterClient.Id)
            .ToList();

        subset.Should().HaveCount(2);
        subset[0].ClientId.Should().Be(earlierClient.Id,
            because: "the client expiring soonest should appear first");
        subset[1].ClientId.Should().Be(laterClient.Id);
    }

    // ---------------------------------------------------------------------------
    // GetExpiredClientsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetExpiredClientsAsync_ReturnsClientsWhoseRetentionHasAlreadyExpired()
    {
        // Arrange — client expired 10 days ago
        var expiredClient = AddClient("Henry", "OldExpired",
            retentionExpiresAt: DateTime.UtcNow.AddDays(-10));

        // Act
        var result = await _sut.GetExpiredClientsAsync();

        // Assert
        result.Should().Contain(r => r.ClientId == expiredClient.Id,
            because: "a client whose RetentionExpiresAt is in the past should be returned");
    }

    [Fact]
    public async Task GetExpiredClientsAsync_DoesNotReturnClientsStillWithinRetentionWindow()
    {
        // Arrange — use the seeded client which expires in 30 days (future)
        // Act
        var result = await _sut.GetExpiredClientsAsync();

        // Assert
        result.Should().NotContain(r => r.ClientId == _seededClientId,
            because: "the seeded client expires in 30 days and should not appear as expired");
    }

    [Fact]
    public async Task GetExpiredClientsAsync_DoesNotReturnAlreadyPurgedClients()
    {
        // Arrange — expired but already purged
        var purgedExpiredClient = AddClient("Iris", "PurgedExpired",
            retentionExpiresAt: DateTime.UtcNow.AddDays(-5),
            isPurged: true);

        // Act
        var result = await _sut.GetExpiredClientsAsync();

        // Assert
        result.Should().NotContain(r => r.ClientId == purgedExpiredClient.Id,
            because: "clients already purged should not appear in the expired list");
    }

    [Fact]
    public async Task GetExpiredClientsAsync_DoesNotReturnSoftDeletedClients()
    {
        // Arrange — expired but soft-deleted
        var deletedExpiredClient = AddClient("Julia", "DeletedExpired",
            retentionExpiresAt: DateTime.UtcNow.AddDays(-5),
            isDeleted: true);

        // Act
        var result = await _sut.GetExpiredClientsAsync();

        // Assert
        result.Should().NotContain(r => r.ClientId == deletedExpiredClient.Id,
            because: "soft-deleted clients should not appear in the expired list");
    }

    // ---------------------------------------------------------------------------
    // GetPurgeHistoryAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPurgeHistoryAsync_ReturnsLogsOrderedByPurgedAtDescending()
    {
        // Arrange — add two audit log entries with different timestamps
        var olderLog = new DataPurgeAuditLog
        {
            PurgedAt = DateTime.UtcNow.AddDays(-2),
            PurgedByUserId = NutritionistId,
            ClientId = _seededClientId,
            ClientIdentifier = "Client #1 - A.P.",
            PurgedEntities = "{}",
            Justification = "Old purge"
        };
        var newerLog = new DataPurgeAuditLog
        {
            PurgedAt = DateTime.UtcNow.AddDays(-1),
            PurgedByUserId = NutritionistId,
            ClientId = _seededClientId,
            ClientIdentifier = "Client #1 - A.P.",
            PurgedEntities = "{}",
            Justification = "Newer purge"
        };
        _dbContext.DataPurgeAuditLogs.AddRange(olderLog, newerLog);
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeHistoryAsync();

        // Assert
        var ids = result.Select(r => r.Id).ToList();
        ids.IndexOf(newerLog.Id).Should().BeLessThan(ids.IndexOf(olderLog.Id),
            because: "the most recently purged entry should appear first");
    }

    [Fact]
    public async Task GetPurgeHistoryAsync_ResolvesUserDisplayNamesFromUsersTable()
    {
        // Arrange — add audit log referencing the seeded nutritionist (whose DisplayName = "Jane Smith")
        var log = new DataPurgeAuditLog
        {
            PurgedAt = DateTime.UtcNow,
            PurgedByUserId = NutritionistId,
            ClientId = _seededClientId,
            ClientIdentifier = "Client #1 - A.P.",
            PurgedEntities = "{}",
            Justification = "User display name test"
        };
        _dbContext.DataPurgeAuditLogs.Add(log);
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeHistoryAsync();

        // Assert
        var entry = result.Should().ContainSingle(r => r.Id == log.Id).Subject;
        entry.PurgedByName.Should().Be("Jane Smith",
            because: "the service should resolve the user's DisplayName from the Users table");
    }

    [Fact]
    public async Task GetPurgeHistoryAsync_ReturnsUnknownForMissingUserIds()
    {
        // Arrange — add audit log referencing a user that does not exist
        var log = new DataPurgeAuditLog
        {
            PurgedAt = DateTime.UtcNow,
            PurgedByUserId = "nonexistent-user-999",
            ClientId = _seededClientId,
            ClientIdentifier = "Client #1 - A.P.",
            PurgedEntities = "{}",
            Justification = "Unknown user test"
        };
        _dbContext.DataPurgeAuditLogs.Add(log);
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeHistoryAsync();

        // Assert
        var entry = result.Should().ContainSingle(r => r.Id == log.Id).Subject;
        entry.PurgedByName.Should().Be("Unknown",
            because: "when the user cannot be resolved the name should fall back to 'Unknown'");
    }

    // ---------------------------------------------------------------------------
    // GetPurgeSummaryAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsNullForNonExistentClient()
    {
        // Act
        var result = await _sut.GetPurgeSummaryAsync(999_888);

        // Assert
        result.Should().BeNull(because: "no client with that id exists");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectAppointmentCount()
    {
        // Arrange
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
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.AppointmentCount.Should().Be(1,
            because: "exactly one appointment was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectMealPlanCount()
    {
        // Arrange
        _dbContext.MealPlans.Add(new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Test Plan",
            Status = MealPlanStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.MealPlanCount.Should().Be(1,
            because: "exactly one meal plan was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectProgressEntryCount()
    {
        // Arrange
        _dbContext.ProgressEntries.Add(new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.ProgressEntryCount.Should().Be(1,
            because: "exactly one progress entry was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectProgressGoalCount()
    {
        // Arrange
        _dbContext.ProgressGoals.Add(new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Lose weight",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.ProgressGoalCount.Should().Be(1,
            because: "exactly one progress goal was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectConsentEventCount()
    {
        // Arrange
        _dbContext.ConsentEvents.Add(new ConsentEvent
        {
            ClientId = _seededClientId,
            EventType = ConsentEventType.ConsentGiven,
            ConsentPurpose = "Treatment",
            PolicyVersion = "1.0",
            RecordedByUserId = NutritionistId,
            Timestamp = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.ConsentEventCount.Should().Be(1,
            because: "exactly one consent event was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectIntakeFormCount()
    {
        // Arrange
        _dbContext.IntakeForms.Add(new IntakeForm
        {
            ClientId = _seededClientId,
            Status = IntakeFormStatus.Submitted,
            Token = Guid.NewGuid().ToString(),
            ClientEmail = "alice@example.com",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.IntakeFormCount.Should().Be(1,
            because: "exactly one intake form was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_ReturnsCorrectSessionNoteCount()
    {
        // Arrange — need an appointment first since SessionNote references one
        var appt = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Completed,
            StartTime = DateTime.UtcNow.AddDays(-7),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appt);
        _dbContext.SaveChanges();

        _dbContext.SessionNotes.Add(new SessionNote
        {
            ClientId = _seededClientId,
            AppointmentId = appt.Id,
            CreatedByUserId = NutritionistId,
            Notes = "Session notes content",
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.SessionNoteCount.Should().Be(1,
            because: "exactly one session note was seeded for this client");
    }

    [Fact]
    public async Task GetPurgeSummaryAsync_HealthProfileItemCount_IsAllergyPlusMedicationPlusConditionPlusDietaryRestriction()
    {
        // Arrange — one of each health profile item type
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Peanuts",
            Severity = AllergySeverity.Severe,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientMedications.Add(new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Metformin",
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientConditions.Add(new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Diabetes Type 2",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientDietaryRestrictions.Add(new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.GlutenFree,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPurgeSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result!.HealthProfileItemCount.Should().Be(4,
            because: "1 allergy + 1 medication + 1 condition + 1 dietary restriction = 4");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — guard conditions
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithWrongConfirmationString_ReturnsFailureResult()
    {
        // Act
        var result = await _sut.ExecutePurgeAsync(_seededClientId, "WRONG CONFIRMATION", ActingUserId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Confirmation text does not match");
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithNonExistentClient_ReturnsFailureResult()
    {
        // Act
        var result = await _sut.ExecutePurgeAsync(999_777, "PURGE Some Client", ActingUserId);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Be("Client not found");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — client anonymization
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_AnonymizesAllClientPiiFields()
    {
        // Act
        var result = await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        result.Success.Should().BeTrue();

        _dbContext.ChangeTracker.Clear();
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        client.FirstName.Should().Be("Purged");
        client.LastName.Should().Be("Client");
        client.Email.Should().BeNull();
        client.Phone.Should().BeNull();
        client.DateOfBirth.Should().BeNull();
        client.Notes.Should().BeNull();
        client.IsPurged.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — appointment notes purge
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_NullsAppointmentNotesAndPrepNotes()
    {
        // Arrange
        var appt = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(7),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            Notes = "Appointment notes",
            PrepNotes = "Prep notes",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appt);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == appt.Id);

        persisted.Notes.Should().BeNull(because: "appointment notes must be purged");
        persisted.PrepNotes.Should().BeNull(because: "appointment prep notes must be purged");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — meal plan notes purge
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_NullsMealPlanNotesAndDescription()
    {
        // Arrange
        var mealPlan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Test Plan",
            Status = MealPlanStatus.Active,
            Notes = "Meal plan notes",
            Description = "Meal plan description",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MealPlans.Add(mealPlan);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.MealPlans
            .IgnoreQueryFilters()
            .FirstAsync(mp => mp.Id == mealPlan.Id);

        persisted.Notes.Should().BeNull(because: "meal plan notes must be purged");
        persisted.Description.Should().BeNull(because: "meal plan description must be purged");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — progress entry notes purge
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_NullsProgressEntryNotes()
    {
        // Arrange
        var entry = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            Notes = "Progress notes",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ProgressEntries.Add(entry);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ProgressEntries
            .IgnoreQueryFilters()
            .FirstAsync(pe => pe.Id == entry.Id);

        persisted.Notes.Should().BeNull(because: "progress entry notes must be purged");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — health profile items soft-delete
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_SoftDeletesClientAllergies()
    {
        // Arrange
        var allergy = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Shellfish",
            Severity = AllergySeverity.Moderate,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(allergy);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ClientAllergies
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == allergy.Id);

        persisted.IsDeleted.Should().BeTrue(because: "client allergies must be soft-deleted during a purge");
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(ActingUserId);
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_SoftDeletesClientMedications()
    {
        // Arrange
        var medication = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Lisinopril",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientMedications.Add(medication);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ClientMedications
            .IgnoreQueryFilters()
            .FirstAsync(m => m.Id == medication.Id);

        persisted.IsDeleted.Should().BeTrue(because: "client medications must be soft-deleted during a purge");
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(ActingUserId);
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_SoftDeletesClientConditions()
    {
        // Arrange
        var condition = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Hypertension",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientConditions.Add(condition);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ClientConditions
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == condition.Id);

        persisted.IsDeleted.Should().BeTrue(because: "client conditions must be soft-deleted during a purge");
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(ActingUserId);
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_SoftDeletesClientDietaryRestrictions()
    {
        // Arrange
        var restriction = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.Vegan,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientDietaryRestrictions.Add(restriction);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ClientDietaryRestrictions
            .IgnoreQueryFilters()
            .FirstAsync(dr => dr.Id == restriction.Id);

        persisted.IsDeleted.Should().BeTrue(because: "client dietary restrictions must be soft-deleted during a purge");
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(ActingUserId);
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — consent event notes purge
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_NullsConsentEventNotes()
    {
        // Arrange
        var consentEvent = new ConsentEvent
        {
            ClientId = _seededClientId,
            EventType = ConsentEventType.ConsentGiven,
            ConsentPurpose = "Treatment",
            PolicyVersion = "1.0",
            RecordedByUserId = NutritionistId,
            Notes = "Consent event notes",
            Timestamp = DateTime.UtcNow
        };
        _dbContext.ConsentEvents.Add(consentEvent);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.ConsentEvents
            .FirstAsync(ce => ce.Id == consentEvent.Id);

        persisted.Notes.Should().BeNull(because: "consent event notes must be purged");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — session note purge
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_NullsAllSessionNoteFields()
    {
        // Arrange
        var appt = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Completed,
            StartTime = DateTime.UtcNow.AddDays(-14),
            DurationMinutes = 45,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appt);
        _dbContext.SaveChanges();

        var sessionNote = new SessionNote
        {
            ClientId = _seededClientId,
            AppointmentId = appt.Id,
            CreatedByUserId = NutritionistId,
            Notes = "Detailed session notes",
            MeasurementsTaken = "Weight: 75kg",
            PlanAdjustments = "Reduce carbs",
            FollowUpActions = "Schedule next appointment",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.SessionNotes.Add(sessionNote);
        _dbContext.SaveChanges();

        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.SessionNotes
            .IgnoreQueryFilters()
            .FirstAsync(sn => sn.Id == sessionNote.Id);

        persisted.Notes.Should().BeNull(because: "session note Notes must be purged");
        persisted.MeasurementsTaken.Should().BeNull(because: "session note MeasurementsTaken must be purged");
        persisted.PlanAdjustments.Should().BeNull(because: "session note PlanAdjustments must be purged");
        persisted.FollowUpActions.Should().BeNull(because: "session note FollowUpActions must be purged");
    }

    // ---------------------------------------------------------------------------
    // ExecutePurgeAsync — audit log creation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_CreatesDataPurgeAuditLogRecord()
    {
        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var auditLog = await _dbContext.DataPurgeAuditLogs
            .FirstOrDefaultAsync(l => l.ClientId == _seededClientId);

        auditLog.Should().NotBeNull(because: "a DataPurgeAuditLog record must be created after a successful purge");
        auditLog!.PurgedByUserId.Should().Be(ActingUserId);
        auditLog.ClientIdentifier.Should().Contain(_seededClientId.ToString());
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_CallsAuditLogService()
    {
        // Act
        await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            ActingUserId,
            "ClientDataPurged",
            "Client",
            _seededClientId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task ExecutePurgeAsync_WithCorrectConfirmation_ReturnsSuccessTrue()
    {
        // Act
        var result = await _sut.ExecutePurgeAsync(_seededClientId, "PURGE Alice Purge", ActingUserId);

        // Assert
        result.Success.Should().BeTrue(because: "a valid purge request with correct confirmation should succeed");
        result.Error.Should().BeNull(because: "there should be no error message on success");
    }

    // ---------------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}

