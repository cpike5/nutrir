using FluentAssertions;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// Unit tests for DashboardService covering all seven public async methods:
/// GetMetricsAsync, GetRecentClientsAsync, GetClientsMissingConsentAsync,
/// GetTodaysAppointmentsAsync, GetThisWeekAppointmentCountAsync,
/// GetActiveMealPlanCountAsync, and GetRecentMealPlansAsync.
/// </summary>
public class DashboardServiceTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------------

    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAppointmentService _appointmentService;

    private readonly DashboardService _sut;

    private const string NutritionistId = "nutritionist-dashboard-test-001";

    public DashboardServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _appointmentService = Substitute.For<IAppointmentService>();

        _sut = new DashboardService(_dbContextFactory, _appointmentService);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    /// <summary>Seeds a nutritionist ApplicationUser needed for FK constraints.</summary>
    private void SeedNutritionist()
    {
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@dashboardtest.com",
            NormalizedUserName = "NUTRITIONIST@DASHBOARDTEST.COM",
            Email = "nutritionist@dashboardtest.com",
            NormalizedEmail = "NUTRITIONIST@DASHBOARDTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private Client MakeClient(string firstName, string lastName, bool consentGiven, DateTime createdAt)
        => new()
        {
            FirstName = firstName,
            LastName = lastName,
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = consentGiven,
            EmailRemindersEnabled = true,
            CreatedAt = createdAt
        };

    private Appointment MakeAppointment(int clientId, DateTime startTime, int durationMinutes = 60)
        => new()
        {
            ClientId = clientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };

    private MealPlan MakeMealPlan(int clientId, MealPlanStatus status, DateTime createdAt)
        => new()
        {
            ClientId = clientId,
            CreatedByUserId = NutritionistId,
            Title = $"Plan for client {clientId}",
            Status = status,
            CreatedAt = createdAt
        };

    // ---------------------------------------------------------------------------
    // GetMetricsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetMetricsAsync_NoClients_ReturnsAllZeros()
    {
        var result = await _sut.GetMetricsAsync();

        result.TotalActiveClients.Should().Be(0);
        result.PendingConsentCount.Should().Be(0);
        result.NewClientsThisMonth.Should().Be(0);
    }

    [Fact]
    public async Task GetMetricsAsync_WithClients_ReturnsTotalActiveCount()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Alice", "A", consentGiven: true, createdAt: now),
            MakeClient("Bob", "B", consentGiven: true, createdAt: now),
            MakeClient("Carol", "C", consentGiven: false, createdAt: now));
        _dbContext.SaveChanges();

        var result = await _sut.GetMetricsAsync();

        result.TotalActiveClients.Should().Be(3);
    }

    [Fact]
    public async Task GetMetricsAsync_SomeClientsWithoutConsent_ReturnsPendingConsentCount()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Alice", "A", consentGiven: true, createdAt: now),
            MakeClient("Bob", "B", consentGiven: false, createdAt: now),
            MakeClient("Carol", "C", consentGiven: false, createdAt: now));
        _dbContext.SaveChanges();

        var result = await _sut.GetMetricsAsync();

        result.PendingConsentCount.Should().Be(2);
    }

    [Fact]
    public async Task GetMetricsAsync_ClientsCreatedThisMonth_ReturnsNewClientsThisMonthCount()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        var startOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastMonth = startOfMonth.AddDays(-1);

        _dbContext.Clients.AddRange(
            MakeClient("New1", "N", consentGiven: true, createdAt: startOfMonth),
            MakeClient("New2", "N", consentGiven: true, createdAt: now),
            MakeClient("Old", "O", consentGiven: true, createdAt: lastMonth));
        _dbContext.SaveChanges();

        var result = await _sut.GetMetricsAsync();

        result.NewClientsThisMonth.Should().Be(2);
    }

    [Fact]
    public async Task GetMetricsAsync_AllClientsHaveConsent_ReturnsPendingConsentCountZero()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Alice", "A", consentGiven: true, createdAt: now),
            MakeClient("Bob", "B", consentGiven: true, createdAt: now));
        _dbContext.SaveChanges();

        var result = await _sut.GetMetricsAsync();

        result.PendingConsentCount.Should().Be(0);
    }

    [Fact]
    public async Task GetMetricsAsync_ExcludesSoftDeletedClients()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Active", "A", consentGiven: true, createdAt: now),
            MakeClient("Deleted", "D", consentGiven: false, createdAt: now));
        _dbContext.SaveChanges();

        // Soft-delete one client
        var deleted = _dbContext.Clients.First(c => c.FirstName == "Deleted");
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        _dbContext.SaveChanges();

        var result = await _sut.GetMetricsAsync();

        result.TotalActiveClients.Should().Be(1);
        result.PendingConsentCount.Should().Be(0);
    }

    [Fact]
    public async Task GetRecentClientsAsync_ExcludesSoftDeletedClients()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Active", "A", consentGiven: true, createdAt: now),
            MakeClient("Deleted", "D", consentGiven: true, createdAt: now.AddSeconds(1)));
        _dbContext.SaveChanges();

        var deleted = _dbContext.Clients.First(c => c.FirstName == "Deleted");
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentClientsAsync();

        result.Should().HaveCount(1);
        result[0].FirstName.Should().Be("Active");
    }

    // ---------------------------------------------------------------------------
    // GetRecentClientsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentClientsAsync_NoClients_ReturnsEmptyList()
    {
        var result = await _sut.GetRecentClientsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentClientsAsync_ReturnsClientsOrderedByCreatedAtDescending()
    {
        SeedNutritionist();
        var referenceTime = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Oldest", "O", consentGiven: true, createdAt: referenceTime.AddDays(-5)),
            MakeClient("Middle", "M", consentGiven: true, createdAt: referenceTime.AddDays(-2)),
            MakeClient("Newest", "N", consentGiven: true, createdAt: referenceTime));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentClientsAsync();

        result.Select(c => c.FirstName).Should().ContainInOrder("Newest", "Middle", "Oldest");
    }

    [Fact]
    public async Task GetRecentClientsAsync_RespectsCountParameter()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
            _dbContext.Clients.Add(MakeClient($"Client{i}", "C", consentGiven: true, createdAt: now.AddDays(-i)));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentClientsAsync(count: 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecentClientsAsync_DefaultCountReturnsSeven()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
            _dbContext.Clients.Add(MakeClient($"Client{i}", "C", consentGiven: true, createdAt: now.AddDays(-i)));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentClientsAsync();

        result.Should().HaveCount(7);
    }

    // ---------------------------------------------------------------------------
    // GetClientsMissingConsentAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetClientsMissingConsentAsync_AllHaveConsent_ReturnsEmptyList()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Alice", "A", consentGiven: true, createdAt: now),
            MakeClient("Bob", "B", consentGiven: true, createdAt: now));
        _dbContext.SaveChanges();

        var result = await _sut.GetClientsMissingConsentAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetClientsMissingConsentAsync_ReturnsOnlyClientsWithoutConsent()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("WithConsent", "W", consentGiven: true, createdAt: now),
            MakeClient("NoConsent1", "N", consentGiven: false, createdAt: now.AddDays(-1)),
            MakeClient("NoConsent2", "N", consentGiven: false, createdAt: now.AddDays(-2)));
        _dbContext.SaveChanges();

        var result = await _sut.GetClientsMissingConsentAsync();

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(c => c.ConsentGiven.Should().BeFalse());
    }

    [Fact]
    public async Task GetClientsMissingConsentAsync_OrdersByCreatedAtDescending()
    {
        SeedNutritionist();
        var now = DateTime.UtcNow;
        _dbContext.Clients.AddRange(
            MakeClient("Older", "O", consentGiven: false, createdAt: now.AddDays(-5)),
            MakeClient("Newer", "N", consentGiven: false, createdAt: now));
        _dbContext.SaveChanges();

        var result = await _sut.GetClientsMissingConsentAsync();

        result[0].FirstName.Should().Be("Newer");
        result[1].FirstName.Should().Be("Older");
    }

    // ---------------------------------------------------------------------------
    // GetTodaysAppointmentsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetTodaysAppointmentsAsync_NoAppointmentsToday_ReturnsEmptyList()
    {
        var result = await _sut.GetTodaysAppointmentsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_ExcludesYesterdayAndTomorrowAppointments()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, startTime: today.AddDays(-1).AddHours(10)), // yesterday
            MakeAppointment(client.Id, startTime: today.AddDays(1).AddHours(10)));  // tomorrow
        _dbContext.SaveChanges();

        var result = await _sut.GetTodaysAppointmentsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_ReturnsOnlyTodaysAppointments()
    {
        SeedNutritionist();
        var client = MakeClient("Today", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, startTime: today.AddHours(9)),   // today morning
            MakeAppointment(client.Id, startTime: today.AddHours(14)),  // today afternoon
            MakeAppointment(client.Id, startTime: today.AddDays(-1).AddHours(9))); // yesterday
        _dbContext.SaveChanges();

        var result = await _sut.GetTodaysAppointmentsAsync();

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_PopulatesClientFirstAndLastName()
    {
        SeedNutritionist();
        var client = MakeClient("John", "Doe", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        _dbContext.Appointments.Add(MakeAppointment(client.Id, startTime: today.AddHours(10)));
        _dbContext.SaveChanges();

        var result = await _sut.GetTodaysAppointmentsAsync();

        result.Should().HaveCount(1);
        result[0].ClientFirstName.Should().Be("John");
        result[0].ClientLastName.Should().Be("Doe");
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_PopulatesNutritionistDisplayName()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        _dbContext.Appointments.Add(MakeAppointment(client.Id, startTime: today.AddHours(10)));
        _dbContext.SaveChanges();

        var result = await _sut.GetTodaysAppointmentsAsync();

        result.Should().HaveCount(1);
        result[0].NutritionistName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_OrdersByStartTimeAscending()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, startTime: today.AddHours(14)),
            MakeAppointment(client.Id, startTime: today.AddHours(9)));
        _dbContext.SaveChanges();

        var result = await _sut.GetTodaysAppointmentsAsync();

        result[0].StartTime.Should().BeBefore(result[1].StartTime);
    }

    // ---------------------------------------------------------------------------
    // GetThisWeekAppointmentCountAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetThisWeekAppointmentCountAsync_NoAppointments_ReturnsZero()
    {
        var result = await _sut.GetThisWeekAppointmentCountAsync();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetThisWeekAppointmentCountAsync_CountsAppointmentsWithinCurrentWeek()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var startOfWeek = DateTime.SpecifyKind(now.Date.AddDays(-(int)now.DayOfWeek), DateTimeKind.Utc);

        // Two appointments in this week
        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, startTime: startOfWeek.AddHours(10)),
            MakeAppointment(client.Id, startTime: startOfWeek.AddDays(2).AddHours(14)));
        _dbContext.SaveChanges();

        var result = await _sut.GetThisWeekAppointmentCountAsync();

        result.Should().Be(2);
    }

    [Fact]
    public async Task GetThisWeekAppointmentCountAsync_ExcludesAppointmentsOutsideCurrentWeek()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var startOfWeek = DateTime.SpecifyKind(now.Date.AddDays(-(int)now.DayOfWeek), DateTimeKind.Utc);

        // One in-week, one last week, one next week
        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, startTime: startOfWeek.AddHours(10)),             // this week
            MakeAppointment(client.Id, startTime: startOfWeek.AddDays(-1).AddHours(10)), // last week
            MakeAppointment(client.Id, startTime: startOfWeek.AddDays(7).AddHours(10))); // next week
        _dbContext.SaveChanges();

        var result = await _sut.GetThisWeekAppointmentCountAsync();

        result.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // GetActiveMealPlanCountAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetActiveMealPlanCountAsync_NoMealPlans_ReturnsZero()
    {
        var result = await _sut.GetActiveMealPlanCountAsync();

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetActiveMealPlanCountAsync_CountsOnlyActivePlans()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        _dbContext.MealPlans.AddRange(
            MakeMealPlan(client.Id, MealPlanStatus.Active, now),
            MakeMealPlan(client.Id, MealPlanStatus.Active, now.AddDays(-1)),
            MakeMealPlan(client.Id, MealPlanStatus.Draft, now.AddDays(-2)),
            MakeMealPlan(client.Id, MealPlanStatus.Archived, now.AddDays(-3)));
        _dbContext.SaveChanges();

        var result = await _sut.GetActiveMealPlanCountAsync();

        result.Should().Be(2);
    }

    [Fact]
    public async Task GetActiveMealPlanCountAsync_NoDraftOrArchivedPlansAreCountedAsActive()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        _dbContext.MealPlans.AddRange(
            MakeMealPlan(client.Id, MealPlanStatus.Draft, now),
            MakeMealPlan(client.Id, MealPlanStatus.Archived, now));
        _dbContext.SaveChanges();

        var result = await _sut.GetActiveMealPlanCountAsync();

        result.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // GetRecentMealPlansAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentMealPlansAsync_NoMealPlans_ReturnsEmptyList()
    {
        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_ReturnsPlansOrderedByCreatedAtDescending()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        var oldest = MakeMealPlan(client.Id, MealPlanStatus.Active, now.AddDays(-3));
        oldest.Title = "Oldest";
        var middle = MakeMealPlan(client.Id, MealPlanStatus.Active, now.AddDays(-1));
        middle.Title = "Middle";
        var newest = MakeMealPlan(client.Id, MealPlanStatus.Active, now);
        newest.Title = "Newest";
        _dbContext.MealPlans.AddRange(oldest, middle, newest);
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Select(p => p.Title).Should().ContainInOrder("Newest", "Middle", "Oldest");
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_RespectsCountParameter()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        for (var i = 0; i < 8; i++)
            _dbContext.MealPlans.Add(MakeMealPlan(client.Id, MealPlanStatus.Active, now.AddDays(-i)));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync(count: 3);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_DefaultCountReturnsFive()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var now = DateTime.UtcNow;
        for (var i = 0; i < 8; i++)
            _dbContext.MealPlans.Add(MakeMealPlan(client.Id, MealPlanStatus.Active, now.AddDays(-i)));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().HaveCount(5);
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_PopulatesClientName()
    {
        SeedNutritionist();
        var client = MakeClient("Alice", "Wonder", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _dbContext.MealPlans.Add(MakeMealPlan(client.Id, MealPlanStatus.Active, DateTime.UtcNow));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().HaveCount(1);
        result[0].ClientFirstName.Should().Be("Alice");
        result[0].ClientLastName.Should().Be("Wonder");
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_IncludesDayCountAndTotalItemsFromPlanContent()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        var plan = MakeMealPlan(client.Id, MealPlanStatus.Active, DateTime.UtcNow);
        plan.Days =
        [
            new MealPlanDay
            {
                DayNumber = 1,
                MealSlots =
                [
                    new MealSlot
                    {
                        MealType = MealType.Breakfast,
                        Items =
                        [
                            new MealItem { FoodName = "Oats", Quantity = 100, Unit = "g" },
                            new MealItem { FoodName = "Milk", Quantity = 200, Unit = "ml" }
                        ]
                    }
                ]
            },
            new MealPlanDay
            {
                DayNumber = 2,
                MealSlots =
                [
                    new MealSlot
                    {
                        MealType = MealType.Lunch,
                        Items = [ new MealItem { FoodName = "Chicken", Quantity = 150, Unit = "g" } ]
                    }
                ]
            }
        ];
        _dbContext.MealPlans.Add(plan);
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().HaveCount(1);
        result[0].DayCount.Should().Be(2);
        result[0].TotalItems.Should().Be(3);
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_PlanWithNoDays_HasZeroDayCountAndZeroItems()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _dbContext.MealPlans.Add(MakeMealPlan(client.Id, MealPlanStatus.Draft, DateTime.UtcNow));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().HaveCount(1);
        result[0].DayCount.Should().Be(0);
        result[0].TotalItems.Should().Be(0);
    }

    [Fact]
    public async Task GetRecentMealPlansAsync_PopulatesCreatedByName_UsingDisplayName()
    {
        SeedNutritionist();
        var client = MakeClient("Test", "Client", consentGiven: true, createdAt: DateTime.UtcNow);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _dbContext.MealPlans.Add(MakeMealPlan(client.Id, MealPlanStatus.Active, DateTime.UtcNow));
        _dbContext.SaveChanges();

        var result = await _sut.GetRecentMealPlansAsync();

        result.Should().HaveCount(1);
        // The seeded nutritionist has DisplayName = "Jane Smith"
        result[0].CreatedByName.Should().Be("Jane Smith");
    }
}
