using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class SoftDeleteFilterTests : IDisposable
{
    private readonly Nutrir.Infrastructure.Data.AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    // A seeded ApplicationUser is required to satisfy the FK on Client.PrimaryNutritionistId
    private const string NutritionistId = "soft-delete-nutritionist-001";

    // Shared seeded client and appointment for FK-dependent entities
    private int _seededClientId;
    private int _seededAppointmentId;

    public SoftDeleteFilterTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        SeedNutritionist();
        SeedClient();
        SeedAppointment();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedNutritionist()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "soft-delete@test.com",
            NormalizedUserName = "SOFT-DELETE@TEST.COM",
            Email = "soft-delete@test.com",
            NormalizedEmail = "SOFT-DELETE@TEST.COM",
            FirstName = "Soft",
            LastName = "Delete",
            DisplayName = "Soft Delete",
            CreatedDate = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.SaveChanges();
    }

    private void SeedClient()
    {
        var client = new Client
        {
            FirstName = "Shared",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();
        _seededClientId = client.Id;
    }

    private void SeedAppointment()
    {
        var appointment = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();
        _seededAppointmentId = appointment.Id;
    }

    private Client MakeClient(string firstName, string lastName) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        PrimaryNutritionistId = NutritionistId,
        ConsentGiven = false,
        CreatedAt = DateTime.UtcNow
    };

    // ---------------------------------------------------------------------------
    // Tests — Client
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ExcludesSoftDeleted()
    {
        // Arrange — seed 3 clients; soft-delete one of them
        var active1 = MakeClient("Alice", "Active");
        var active2 = MakeClient("Bob", "Active");
        var deleted = MakeClient("Charlie", "Deleted");

        _dbContext.Clients.AddRange(active1, active2, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.DeletedBy = NutritionistId;
        await _dbContext.SaveChangesAsync();

        // Act — standard query respects the HasQueryFilter
        var clients = await _dbContext.Clients.ToListAsync();

        // Assert — seeded shared client plus the two active ones; deleted is excluded
        clients.Should().NotContain(c => c.LastName == "Deleted");
        clients.Should().OnlyContain(c => !c.IsDeleted);
    }

    [Fact]
    public async Task IgnoreQueryFilters_IncludesSoftDeleted()
    {
        // Arrange — seed 3 clients; soft-delete one of them
        var active1 = MakeClient("Diana", "Active");
        var active2 = MakeClient("Eve", "Active");
        var deleted = MakeClient("Frank", "Deleted");

        _dbContext.Clients.AddRange(active1, active2, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.DeletedBy = NutritionistId;
        await _dbContext.SaveChangesAsync();

        // Act — bypass the query filter explicitly
        var allClients = await _dbContext.Clients.IgnoreQueryFilters().ToListAsync();

        // Assert
        allClients.Should().Contain(c => c.LastName == "Deleted");
        allClients.Should().Contain(c => c.IsDeleted);
    }

    [Fact]
    public async Task SoftDelete_SetsFields()
    {
        // Arrange
        var client = MakeClient("Grace", "SoftDeleteFields");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var deletedBy = NutritionistId;
        var deletedAt = DateTime.UtcNow;

        // Act — apply soft-delete fields manually (as a service would)
        client.IsDeleted = true;
        client.DeletedAt = deletedAt;
        client.DeletedBy = deletedBy;
        await _dbContext.SaveChangesAsync();

        // Re-load bypassing the query filter to inspect the persisted state
        var persisted = await _dbContext.Clients
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == client.Id);

        // Assert
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedBy.Should().Be(deletedBy);
        persisted.DeletedAt.Should().BeCloseTo(deletedAt, precision: TimeSpan.FromSeconds(1));
    }

    // ---------------------------------------------------------------------------
    // Tests — Appointment
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_Appointment_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(3),
            DurationMinutes = 30,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Appointments.ToListAsync();

        // Assert
        results.Should().NotContain(a => a.IsDeleted);
        results.Should().NotContain(a => a.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_Appointment_IncludesSoftDeleted()
    {
        // Arrange
        var active = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.CheckIn,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(4),
            DurationMinutes = 15,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(5),
            DurationMinutes = 30,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Appointments.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(a => a.Id == deleted.Id);
        results.Should().Contain(a => a.IsDeleted);
    }

    [Fact]
    public async Task SoftDelete_Appointment_SetsFields()
    {
        // Arrange
        var appointment = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(20),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        await _dbContext.SaveChangesAsync();

        var deletedAt = DateTime.UtcNow;
        var deletedBy = NutritionistId;

        // Act
        appointment.IsDeleted = true;
        appointment.DeletedAt = deletedAt;
        appointment.DeletedBy = deletedBy;
        await _dbContext.SaveChangesAsync();

        var persisted = await _dbContext.Appointments
            .IgnoreQueryFilters()
            .FirstAsync(a => a.Id == appointment.Id);

        // Assert
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedBy.Should().Be(deletedBy);
        persisted.DeletedAt.Should().BeCloseTo(deletedAt, precision: TimeSpan.FromSeconds(1));
    }

    // ---------------------------------------------------------------------------
    // Tests — MealPlan
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_MealPlan_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Active Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Deleted Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.MealPlans.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.MealPlans.ToListAsync();

        // Assert
        results.Should().NotContain(mp => mp.IsDeleted);
        results.Should().NotContain(mp => mp.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_MealPlan_IncludesSoftDeleted()
    {
        // Arrange
        var active = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Active Plan 2",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Deleted Plan 2",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.MealPlans.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.MealPlans.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(mp => mp.IsDeleted);
        results.Should().Contain(mp => mp.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ProgressGoal
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ProgressGoal_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Active Goal",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Deleted Goal",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProgressGoals.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ProgressGoals.ToListAsync();

        // Assert
        results.Should().NotContain(g => g.IsDeleted);
        results.Should().NotContain(g => g.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ProgressGoal_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Active Goal 2",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ProgressGoal
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Deleted Goal 2",
            GoalType = GoalType.Weight,
            Status = GoalStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProgressGoals.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ProgressGoals.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(g => g.IsDeleted);
        results.Should().Contain(g => g.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ProgressEntry
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ProgressEntry_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow),
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProgressEntries.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ProgressEntries.ToListAsync();

        // Assert
        results.Should().NotContain(e => e.IsDeleted);
        results.Should().NotContain(e => e.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ProgressEntry_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)),
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ProgressEntry
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            EntryDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ProgressEntries.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ProgressEntries.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(e => e.IsDeleted);
        results.Should().Contain(e => e.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — SessionNote (requires seeded Appointment)
    // SessionNote has a unique index on AppointmentId, so each note needs its own
    // appointment. A helper creates a fresh appointment per test pair.
    // ---------------------------------------------------------------------------

    private async Task<int> CreateAppointmentAsync()
    {
        var appt = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(30 + _dbContext.Appointments.IgnoreQueryFilters().Count()),
            DurationMinutes = 30,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appt);
        await _dbContext.SaveChangesAsync();
        return appt.Id;
    }

    [Fact]
    public async Task Query_SessionNote_ExcludesSoftDeleted()
    {
        // Arrange — each SessionNote requires a distinct Appointment (unique index)
        var activeApptId = await CreateAppointmentAsync();
        var deletedApptId = await CreateAppointmentAsync();

        var active = new SessionNote
        {
            AppointmentId = activeApptId,
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new SessionNote
        {
            AppointmentId = deletedApptId,
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.SessionNotes.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.SessionNotes.ToListAsync();

        // Assert
        results.Should().NotContain(n => n.IsDeleted);
        results.Should().NotContain(n => n.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_SessionNote_IncludesSoftDeleted()
    {
        // Arrange — each SessionNote requires a distinct Appointment (unique index)
        var activeApptId = await CreateAppointmentAsync();
        var deletedApptId = await CreateAppointmentAsync();

        var active = new SessionNote
        {
            AppointmentId = activeApptId,
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new SessionNote
        {
            AppointmentId = deletedApptId,
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.SessionNotes.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.SessionNotes.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(n => n.IsDeleted);
        results.Should().Contain(n => n.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — IntakeForm
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_IntakeForm_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new IntakeForm
        {
            Token = "active-token-001",
            ClientEmail = "active@test.com",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = IntakeFormStatus.Pending,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new IntakeForm
        {
            Token = "deleted-token-001",
            ClientEmail = "deleted@test.com",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = IntakeFormStatus.Pending,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.IntakeForms.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.IntakeForms.ToListAsync();

        // Assert
        results.Should().NotContain(f => f.IsDeleted);
        results.Should().NotContain(f => f.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_IntakeForm_IncludesSoftDeleted()
    {
        // Arrange
        var active = new IntakeForm
        {
            Token = "active-token-002",
            ClientEmail = "active2@test.com",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = IntakeFormStatus.Pending,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new IntakeForm
        {
            Token = "deleted-token-002",
            ClientEmail = "deleted2@test.com",
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            Status = IntakeFormStatus.Pending,
            CreatedByUserId = NutritionistId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.IntakeForms.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.IntakeForms.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(f => f.IsDeleted);
        results.Should().Contain(f => f.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ClientAllergy
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ClientAllergy_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Peanuts",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Shellfish",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientAllergies.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientAllergies.ToListAsync();

        // Assert
        results.Should().NotContain(a => a.IsDeleted);
        results.Should().NotContain(a => a.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ClientAllergy_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Tree Nuts",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Dairy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientAllergies.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientAllergies.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(a => a.IsDeleted);
        results.Should().Contain(a => a.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ClientMedication
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ClientMedication_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Metformin",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Lisinopril",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientMedications.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientMedications.ToListAsync();

        // Assert
        results.Should().NotContain(m => m.IsDeleted);
        results.Should().NotContain(m => m.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ClientMedication_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Atorvastatin",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Omeprazole",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientMedications.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientMedications.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(m => m.IsDeleted);
        results.Should().Contain(m => m.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ClientCondition
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ClientCondition_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Type 2 Diabetes",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Hypertension",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientConditions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientConditions.ToListAsync();

        // Assert
        results.Should().NotContain(c => c.IsDeleted);
        results.Should().NotContain(c => c.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ClientCondition_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Celiac Disease",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "IBS",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientConditions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientConditions.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(c => c.IsDeleted);
        results.Should().Contain(c => c.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — ClientDietaryRestriction
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ClientDietaryRestriction_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.GlutenFree,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.Vegan,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientDietaryRestrictions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientDietaryRestrictions.ToListAsync();

        // Assert
        results.Should().NotContain(r => r.IsDeleted);
        results.Should().NotContain(r => r.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ClientDietaryRestriction_IncludesSoftDeleted()
    {
        // Arrange
        var active = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.DairyFree,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.Vegetarian,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientDietaryRestrictions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.ClientDietaryRestrictions.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(r => r.IsDeleted);
        results.Should().Contain(r => r.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — Condition (no DeletedAt/DeletedBy fields)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_Condition_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new Condition
        {
            Name = "Active Condition",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Condition
        {
            Name = "Deleted Condition",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Conditions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Conditions.ToListAsync();

        // Assert
        results.Should().NotContain(c => c.IsDeleted);
        results.Should().NotContain(c => c.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_Condition_IncludesSoftDeleted()
    {
        // Arrange
        var active = new Condition
        {
            Name = "Active Condition 2",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Condition
        {
            Name = "Deleted Condition 2",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Conditions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Conditions.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(c => c.IsDeleted);
        results.Should().Contain(c => c.Id == deleted.Id);
    }

    [Fact]
    public async Task SoftDelete_Condition_SetsIsDeletedOnly()
    {
        // Arrange — Condition has IsDeleted but no DeletedAt/DeletedBy fields
        var condition = new Condition { Name = "SoftDeleteFieldTest" };
        _dbContext.Conditions.Add(condition);
        await _dbContext.SaveChangesAsync();

        // Act
        condition.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        var persisted = await _dbContext.Conditions
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == condition.Id);

        // Assert
        persisted.IsDeleted.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // Tests — Allergen (no DeletedAt/DeletedBy fields)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_Allergen_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new Allergen
        {
            Name = "Active Allergen",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Allergen
        {
            Name = "Deleted Allergen",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Allergens.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Allergens.ToListAsync();

        // Assert
        results.Should().NotContain(a => a.IsDeleted);
        results.Should().NotContain(a => a.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_Allergen_IncludesSoftDeleted()
    {
        // Arrange
        var active = new Allergen
        {
            Name = "Active Allergen 2",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Allergen
        {
            Name = "Deleted Allergen 2",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Allergens.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Allergens.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(a => a.IsDeleted);
        results.Should().Contain(a => a.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — Medication (no DeletedAt/DeletedBy fields)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_Medication_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new Medication
        {
            Name = "Active Medication",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Medication
        {
            Name = "Deleted Medication",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Medications.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Medications.ToListAsync();

        // Assert
        results.Should().NotContain(m => m.IsDeleted);
        results.Should().NotContain(m => m.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_Medication_IncludesSoftDeleted()
    {
        // Arrange
        var active = new Medication
        {
            Name = "Active Medication 2",
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new Medication
        {
            Name = "Deleted Medication 2",
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Medications.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.Medications.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(m => m.IsDeleted);
        results.Should().Contain(m => m.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — PractitionerSchedule
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_PractitionerSchedule_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Monday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Tuesday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PractitionerSchedules.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.PractitionerSchedules.ToListAsync();

        // Assert
        results.Should().NotContain(s => s.IsDeleted);
        results.Should().NotContain(s => s.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_PractitionerSchedule_IncludesSoftDeleted()
    {
        // Arrange
        var active = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Wednesday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new PractitionerSchedule
        {
            UserId = NutritionistId,
            DayOfWeek = DayOfWeek.Thursday,
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(17, 0),
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PractitionerSchedules.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.PractitionerSchedules.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(s => s.IsDeleted);
        results.Should().Contain(s => s.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — PractitionerTimeBlock
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_PractitionerTimeBlock_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)),
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Lunch,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(8)),
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Lunch,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PractitionerTimeBlocks.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.PractitionerTimeBlocks.ToListAsync();

        // Assert
        results.Should().NotContain(b => b.IsDeleted);
        results.Should().NotContain(b => b.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_PractitionerTimeBlock_IncludesSoftDeleted()
    {
        // Arrange
        var active = new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(9)),
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Personal,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new PractitionerTimeBlock
        {
            UserId = NutritionistId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)),
            StartTime = new TimeOnly(12, 0),
            EndTime = new TimeOnly(13, 0),
            BlockType = TimeBlockType.Meeting,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PractitionerTimeBlocks.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.PractitionerTimeBlocks.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(b => b.IsDeleted);
        results.Should().Contain(b => b.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // Tests — AppointmentReminder (requires seeded Appointment; no DeletedBy field)
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_AppointmentReminder_ExcludesSoftDeleted()
    {
        // Arrange
        var active = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.TwentyFourHour,
            ScheduledFor = DateTime.UtcNow.AddHours(-24),
            Status = ReminderStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.FortyEightHour,
            ScheduledFor = DateTime.UtcNow.AddHours(-48),
            Status = ReminderStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AppointmentReminders.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.AppointmentReminders.ToListAsync();

        // Assert
        results.Should().NotContain(r => r.IsDeleted);
        results.Should().NotContain(r => r.Id == deleted.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_AppointmentReminder_IncludesSoftDeleted()
    {
        // Arrange
        var active = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.TwentyFourHour,
            ScheduledFor = DateTime.UtcNow.AddHours(-25),
            Status = ReminderStatus.Failed,
            CreatedAt = DateTime.UtcNow
        };
        var deleted = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.FortyEightHour,
            ScheduledFor = DateTime.UtcNow.AddHours(-50),
            Status = ReminderStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.AppointmentReminders.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _dbContext.AppointmentReminders.IgnoreQueryFilters().ToListAsync();

        // Assert
        results.Should().Contain(r => r.IsDeleted);
        results.Should().Contain(r => r.Id == deleted.Id);
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
