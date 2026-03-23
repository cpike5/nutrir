using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Integration.Fixtures;
using Xunit;

namespace Nutrir.Tests.Integration.Services;

/// <summary>
/// CRUD smoke tests for all primary domain services against a real Postgres database
/// via Testcontainers. Each test is fully isolated; ResetDatabaseAsync truncates all
/// tables before every test method.
/// </summary>
[Collection("Database")]
public class ServiceCrudSmokeTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    // Shared test user ID used as "current practitioner" throughout all tests.
    // Set during InitializeAsync after seeding the ApplicationUser.
    private string _testUserId = null!;

    public ServiceCrudSmokeTests(DatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await _fixture.ResetDatabaseAsync();

        // Seed an ApplicationUser to satisfy FK constraints on PrimaryNutritionistId / NutritionistId
        await using var db = await _fixture.CreateDbContextAsync();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "test@test.com",
            NormalizedUserName = "TEST@TEST.COM",
            Email = "test@test.com",
            NormalizedEmail = "TEST@TEST.COM",
            FirstName = "Test",
            LastName = "User",
            DisplayName = "Test User",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _testUserId = user.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Service factory helpers
    // -------------------------------------------------------------------------

    private async Task<(ClientService service, AppDbContext context)> CreateClientServiceAsync()
    {
        var context = await _fixture.CreateDbContextAsync();
        var service = new ClientService(
            context,
            _fixture.DbContextFactory,
            Substitute.For<IAuditLogService>(),
            Substitute.For<IConsentService>(),
            Substitute.For<INotificationDispatcher>(),
            NullLogger<ClientService>.Instance);
        return (service, context);
    }

    private async Task<(AppointmentService service, AppDbContext context)> CreateAppointmentServiceAsync()
    {
        var context = await _fixture.CreateDbContextAsync();

        // IsSlotWithinScheduleAsync must return (true, null) so the time-range guard
        // in AppointmentService.CreateAsync does not throw SchedulingConflictException.
        var availabilityService = Substitute.For<IAvailabilityService>();
        availabilityService
            .IsSlotWithinScheduleAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns((true, (string?)null));

        var service = new AppointmentService(
            context,
            _fixture.DbContextFactory,
            Substitute.For<IAuditLogService>(),
            availabilityService,
            Substitute.For<INotificationDispatcher>(),
            Substitute.For<ISessionNoteService>(),
            Substitute.For<IRetentionTracker>(),
            NullLogger<AppointmentService>.Instance);
        return (service, context);
    }

    private async Task<(MealPlanService service, AppDbContext context)> CreateMealPlanServiceAsync()
    {
        var context = await _fixture.CreateDbContextAsync();

        // CanActivateAsync must return true so UpdateStatusAsync(Draft -> Active) succeeds
        var allergenCheckService = Substitute.For<IAllergenCheckService>();
        allergenCheckService
            .CanActivateAsync(Arg.Any<int>())
            .Returns(true);

        var service = new MealPlanService(
            context,
            _fixture.DbContextFactory,
            Substitute.For<IAuditLogService>(),
            allergenCheckService,
            Substitute.For<INotificationDispatcher>(),
            Substitute.For<IRetentionTracker>(),
            NullLogger<MealPlanService>.Instance);
        return (service, context);
    }

    private async Task<(ProgressService service, AppDbContext context)> CreateProgressServiceAsync()
    {
        var context = await _fixture.CreateDbContextAsync();
        var service = new ProgressService(
            context,
            _fixture.DbContextFactory,
            Substitute.For<IAuditLogService>(),
            Substitute.For<IRetentionTracker>(),
            Substitute.For<INotificationDispatcher>(),
            NullLogger<ProgressService>.Instance);
        return (service, context);
    }

    private async Task<(SessionNoteService service, AppDbContext context)> CreateSessionNoteServiceAsync()
    {
        var context = await _fixture.CreateDbContextAsync();
        var service = new SessionNoteService(
            context,
            _fixture.DbContextFactory,
            Substitute.For<IAuditLogService>(),
            Substitute.For<INotificationDispatcher>(),
            NullLogger<SessionNoteService>.Instance);
        return (service, context);
    }

    // -------------------------------------------------------------------------
    // Seed helpers — bypass service layer to insert prerequisite records directly
    // so FK constraints are satisfied without coupling tests to each other.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Seeds a Client entity directly into the database with ConsentGiven = true
    /// so it satisfies the consent guard in AppointmentService.CreateAsync.
    /// </summary>
    private async Task<Client> SeedClientAsync(AppDbContext context)
    {
        var client = new Client
        {
            FirstName = "Jane",
            LastName = "Smoke",
            Email = "jane.smoke@example.com",
            PrimaryNutritionistId = _testUserId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            ConsentPolicyVersion = "1.0",
            CreatedAt = DateTime.UtcNow
        };
        context.Clients.Add(client);
        await context.SaveChangesAsync();
        return client;
    }

    /// <summary>
    /// Seeds an Appointment entity directly, after seeding a client first.
    /// </summary>
    private async Task<(Client client, Appointment appointment)> SeedAppointmentAsync(AppDbContext context)
    {
        var client = await SeedClientAsync(context);

        var appointment = new Appointment
        {
            ClientId = client.Id,
            NutritionistId = _testUserId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        context.Appointments.Add(appointment);
        await context.SaveChangesAsync();
        return (client, appointment);
    }

    // =========================================================================
    // ClientService tests
    // =========================================================================

    [Fact]
    public async Task ClientService_Create_PersistsClientToDatabase()
    {
        var (service, _) = await CreateClientServiceAsync();

        var dto = new ClientDto(
            Id: 0,
            FirstName: "Alice",
            LastName: "Test",
            Email: "alice@example.com",
            Phone: null,
            DateOfBirth: new DateOnly(1990, 5, 20),
            PrimaryNutritionistId: _testUserId,
            PrimaryNutritionistName: null,
            ConsentGiven: true,
            ConsentTimestamp: DateTime.UtcNow,
            ConsentPolicyVersion: "1.0",
            Notes: null,
            IsDeleted: false,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            DeletedAt: null);

        var result = await service.CreateAsync(dto, _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.FirstName.Should().Be("Alice");
        result.LastName.Should().Be("Test");
        result.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public async Task ClientService_GetById_ReturnsPersistedClient()
    {
        var (service, context) = await CreateClientServiceAsync();
        var seeded = await SeedClientAsync(context);

        var result = await service.GetByIdAsync(seeded.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(seeded.Id);
        result.FirstName.Should().Be(seeded.FirstName);
        result.LastName.Should().Be(seeded.LastName);
    }

    [Fact]
    public async Task ClientService_GetById_ReturnsNull_WhenClientDoesNotExist()
    {
        var (service, _) = await CreateClientServiceAsync();

        var result = await service.GetByIdAsync(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ClientService_GetList_ReturnsAllNonDeletedClients()
    {
        var (service, context) = await CreateClientServiceAsync();
        await SeedClientAsync(context);

        var result = await service.GetListAsync();

        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(c => c.IsDeleted.Should().BeFalse());
    }

    [Fact]
    public async Task ClientService_Update_PersistsChangesToDatabase()
    {
        var (service, context) = await CreateClientServiceAsync();
        var seeded = await SeedClientAsync(context);

        var updateDto = new ClientDto(
            Id: seeded.Id,
            FirstName: "Updated",
            LastName: "Name",
            Email: "updated@example.com",
            Phone: null,
            DateOfBirth: null,
            PrimaryNutritionistId: _testUserId,
            PrimaryNutritionistName: null,
            ConsentGiven: true,
            ConsentTimestamp: DateTime.UtcNow,
            ConsentPolicyVersion: "1.0",
            Notes: "Updated notes",
            IsDeleted: false,
            CreatedAt: seeded.CreatedAt,
            UpdatedAt: null,
            DeletedAt: null);

        var updated = await service.UpdateAsync(seeded.Id, updateDto, _testUserId);

        updated.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.Clients.FindAsync(seeded.Id);
        entity!.FirstName.Should().Be("Updated");
        entity.LastName.Should().Be("Name");
        entity.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ClientService_SoftDelete_SetsIsDeletedFlag()
    {
        var (service, context) = await CreateClientServiceAsync();
        var seeded = await SeedClientAsync(context);

        var result = await service.SoftDeleteAsync(seeded.Id, _testUserId);

        result.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == seeded.Id);
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.DeletedBy.Should().Be(_testUserId);
    }

    // =========================================================================
    // AppointmentService tests
    // =========================================================================

    [Fact]
    public async Task AppointmentService_Create_PersistsAppointmentToDatabase()
    {
        var (service, context) = await CreateAppointmentServiceAsync();
        var client = await SeedClientAsync(context);

        var dto = new CreateAppointmentDto(
            ClientId: client.Id,
            Type: AppointmentType.FollowUp,
            StartTime: DateTime.UtcNow.AddDays(2),
            DurationMinutes: 45,
            Location: AppointmentLocation.Virtual,
            VirtualMeetingUrl: "https://meet.example.com/abc",
            LocationNotes: null,
            Notes: "First follow-up",
            PrepNotes: null);

        var result = await service.CreateAsync(dto, _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(client.Id);
        result.Type.Should().Be(AppointmentType.FollowUp);
        result.Status.Should().Be(AppointmentStatus.Scheduled);
        result.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public async Task AppointmentService_GetById_ReturnsPersistedAppointment()
    {
        var (service, context) = await CreateAppointmentServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var result = await service.GetByIdAsync(appointment.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(appointment.Id);
        result.Type.Should().Be(appointment.Type);
    }

    [Fact]
    public async Task AppointmentService_GetById_ReturnsNull_WhenAppointmentDoesNotExist()
    {
        var (service, _) = await CreateAppointmentServiceAsync();

        var result = await service.GetByIdAsync(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AppointmentService_GetList_ReturnsAppointmentsForClient()
    {
        var (service, context) = await CreateAppointmentServiceAsync();
        var (client, _) = await SeedAppointmentAsync(context);

        var result = await service.GetListAsync(clientId: client.Id);

        result.Should().HaveCount(1);
        result[0].ClientId.Should().Be(client.Id);
    }

    [Fact]
    public async Task AppointmentService_UpdateStatus_TransitionsStatusCorrectly()
    {
        var (service, context) = await CreateAppointmentServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        // Scheduled -> Confirmed is a valid transition
        var result = await service.UpdateStatusAsync(
            appointment.Id,
            AppointmentStatus.Confirmed,
            _testUserId);

        result.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.Appointments.FindAsync(appointment.Id);
        entity!.Status.Should().Be(AppointmentStatus.Confirmed);
        entity.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AppointmentService_SoftDelete_SetsIsDeletedFlag()
    {
        var (service, context) = await CreateAppointmentServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var result = await service.SoftDeleteAsync(appointment.Id, _testUserId);

        result.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.Appointments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == appointment.Id);
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
    }

    // =========================================================================
    // MealPlanService tests
    // =========================================================================

    [Fact]
    public async Task MealPlanService_Create_PersistsMealPlanToDatabase()
    {
        var (service, context) = await CreateMealPlanServiceAsync();
        var client = await SeedClientAsync(context);

        var dto = new CreateMealPlanDto(
            ClientId: client.Id,
            Title: "Week 1 Plan",
            Description: "Initial meal plan",
            StartDate: DateOnly.FromDateTime(DateTime.UtcNow),
            EndDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(6)),
            CalorieTarget: 2000m,
            ProteinTargetG: 150m,
            CarbsTargetG: 200m,
            FatTargetG: 70m,
            Notes: null,
            Instructions: null,
            NumberOfDays: 7);

        var result = await service.CreateAsync(dto, _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(client.Id);
        result.Title.Should().Be("Week 1 Plan");
        result.Status.Should().Be(MealPlanStatus.Draft);
        result.Days.Should().HaveCount(7);
    }

    [Fact]
    public async Task MealPlanService_GetById_ReturnsPersistedMealPlan()
    {
        var (service, context) = await CreateMealPlanServiceAsync();
        var client = await SeedClientAsync(context);

        var created = await service.CreateAsync(
            new CreateMealPlanDto(client.Id, "Test Plan", null, null, null,
                null, null, null, null, null, null, 3),
            _testUserId);

        var result = await service.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Title.Should().Be("Test Plan");
        result.Days.Should().HaveCount(3);
    }

    [Fact]
    public async Task MealPlanService_GetById_ReturnsNull_WhenMealPlanDoesNotExist()
    {
        var (service, _) = await CreateMealPlanServiceAsync();

        var result = await service.GetByIdAsync(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task MealPlanService_GetList_ReturnsMealPlansForClient()
    {
        var (service, context) = await CreateMealPlanServiceAsync();
        var client = await SeedClientAsync(context);

        await service.CreateAsync(
            new CreateMealPlanDto(client.Id, "Plan A", null, null, null,
                null, null, null, null, null, null, 1),
            _testUserId);

        var result = await service.GetListAsync(clientId: client.Id);

        result.Should().HaveCount(1);
        result[0].ClientId.Should().Be(client.Id);
    }

    [Fact]
    public async Task MealPlanService_UpdateStatus_DraftToActive_Succeeds()
    {
        var (service, context) = await CreateMealPlanServiceAsync();
        var client = await SeedClientAsync(context);

        var created = await service.CreateAsync(
            new CreateMealPlanDto(client.Id, "Activation Test", null, null, null,
                null, null, null, null, null, null, 2),
            _testUserId);

        created.Status.Should().Be(MealPlanStatus.Draft);

        var result = await service.UpdateStatusAsync(created.Id, MealPlanStatus.Active, _testUserId);

        result.Success.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.MealPlans.FindAsync(created.Id);
        entity!.Status.Should().Be(MealPlanStatus.Active);
    }

    [Fact]
    public async Task MealPlanService_SoftDelete_SetsIsDeletedFlag()
    {
        var (service, context) = await CreateMealPlanServiceAsync();
        var client = await SeedClientAsync(context);

        var created = await service.CreateAsync(
            new CreateMealPlanDto(client.Id, "To Delete", null, null, null,
                null, null, null, null, null, null, 1),
            _testUserId);

        var result = await service.SoftDeleteAsync(created.Id, _testUserId);

        result.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.MealPlans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(mp => mp.Id == created.Id);
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
    }

    // =========================================================================
    // ProgressService tests
    // =========================================================================

    [Fact]
    public async Task ProgressService_CreateEntry_PersistsProgressEntryToDatabase()
    {
        var (service, context) = await CreateProgressServiceAsync();
        var client = await SeedClientAsync(context);

        var dto = new CreateProgressEntryDto(
            ClientId: client.Id,
            EntryDate: DateOnly.FromDateTime(DateTime.UtcNow),
            Notes: "Feeling good",
            Measurements:
            [
                new CreateProgressMeasurementDto(MetricType.Weight, null, 75.5m, "kg")
            ]);

        var result = await service.CreateEntryAsync(dto, _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(client.Id);
        result.Notes.Should().Be("Feeling good");
        result.Measurements.Should().HaveCount(1);
        result.Measurements[0].MetricType.Should().Be(MetricType.Weight);
        result.Measurements[0].Value.Should().Be(75.5m);
    }

    [Fact]
    public async Task ProgressService_CreateGoal_PersistsProgressGoalToDatabase()
    {
        var (service, context) = await CreateProgressServiceAsync();
        var client = await SeedClientAsync(context);

        var dto = new CreateProgressGoalDto(
            ClientId: client.Id,
            Title: "Lose 5kg",
            Description: "Target body weight goal",
            GoalType: GoalType.Weight,
            TargetValue: 70m,
            TargetUnit: "kg",
            TargetDate: DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)));

        var result = await service.CreateGoalAsync(dto, _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(client.Id);
        result.Title.Should().Be("Lose 5kg");
        result.GoalType.Should().Be(GoalType.Weight);
        result.TargetValue.Should().Be(70m);
        result.Status.Should().Be(GoalStatus.Active);
    }

    [Fact]
    public async Task ProgressService_GetChartData_ReturnsDataForExistingEntries()
    {
        var (service, context) = await CreateProgressServiceAsync();
        var client = await SeedClientAsync(context);

        // Create two weight entries so GetChartDataAsync has data to return
        await service.CreateEntryAsync(new CreateProgressEntryDto(
            client.Id,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)),
            null,
            [new CreateProgressMeasurementDto(MetricType.Weight, null, 80m, "kg")]),
            _testUserId);

        await service.CreateEntryAsync(new CreateProgressEntryDto(
            client.Id,
            DateOnly.FromDateTime(DateTime.UtcNow),
            null,
            [new CreateProgressMeasurementDto(MetricType.Weight, null, 78.5m, "kg")]),
            _testUserId);

        var chartData = await service.GetChartDataAsync(client.Id, MetricType.Weight);

        chartData.Should().NotBeNull();
        chartData!.Points.Should().HaveCount(2);
        chartData.Points.Should().BeInAscendingOrder(dp => dp.Date);
    }

    // =========================================================================
    // SessionNoteService tests
    // =========================================================================

    [Fact]
    public async Task SessionNoteService_CreateDraft_PersistsSessionNoteToDatabase()
    {
        var (service, context) = await CreateSessionNoteServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var result = await service.CreateDraftAsync(
            appointmentId: appointment.Id,
            clientId: appointment.ClientId,
            userId: _testUserId);

        result.Id.Should().BeGreaterThan(0);
        result.AppointmentId.Should().Be(appointment.Id);
        result.ClientId.Should().Be(appointment.ClientId);
        result.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task SessionNoteService_GetById_ReturnsPersistedSessionNote()
    {
        var (service, context) = await CreateSessionNoteServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var created = await service.CreateDraftAsync(appointment.Id, appointment.ClientId, _testUserId);

        var result = await service.GetByIdAsync(created.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.AppointmentId.Should().Be(appointment.Id);
        result.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task SessionNoteService_GetById_ReturnsNull_WhenSessionNoteDoesNotExist()
    {
        var (service, _) = await CreateSessionNoteServiceAsync();

        var result = await service.GetByIdAsync(99999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionNoteService_GetList_ReturnsSessionNotesForClient()
    {
        var (service, context) = await CreateSessionNoteServiceAsync();
        var (client, appointment) = await SeedAppointmentAsync(context);

        await service.CreateDraftAsync(appointment.Id, client.Id, _testUserId);

        var result = await service.GetByClientAsync(client.Id);

        result.Should().HaveCount(1);
        result[0].ClientId.Should().Be(client.Id);
    }

    [Fact]
    public async Task SessionNoteService_Update_PersistsChangesToDatabase()
    {
        var (service, context) = await CreateSessionNoteServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var created = await service.CreateDraftAsync(appointment.Id, appointment.ClientId, _testUserId);

        var updateDto = new UpdateSessionNoteDto(
            Notes: "Client is progressing well.",
            AdherenceScore: 85,
            MeasurementsTaken: "Weight: 75kg",
            PlanAdjustments: "Reduce carbs by 10%",
            FollowUpActions: "Book next appointment");

        var updated = await service.UpdateAsync(created.Id, updateDto, _testUserId);

        updated.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.SessionNotes.FindAsync(created.Id);
        entity!.Notes.Should().Be("Client is progressing well.");
        entity.AdherenceScore.Should().Be(85);
        entity.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SessionNoteService_SoftDelete_SetsIsDeletedFlag()
    {
        var (service, context) = await CreateSessionNoteServiceAsync();
        var (_, appointment) = await SeedAppointmentAsync(context);

        var created = await service.CreateDraftAsync(appointment.Id, appointment.ClientId, _testUserId);

        var result = await service.SoftDeleteAsync(created.Id, _testUserId);

        result.Should().BeTrue();

        await using var verifyContext = await _fixture.CreateDbContextAsync();
        var entity = await verifyContext.SessionNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(sn => sn.Id == created.Id);
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
    }
}
