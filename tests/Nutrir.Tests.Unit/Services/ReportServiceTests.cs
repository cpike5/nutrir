using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class ReportServiceTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------------

    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly ReportService _sut;

    private const string NutritionistId = "nutritionist-report-test-001";

    // Captured after SaveChanges so tests do not hard-code magic numbers.
    private int _clientOldId;   // created before the report window
    private int _clientNewId;   // created inside the report window
    private int _clientExtra1Id;
    private int _clientExtra2Id;

    // Fixed reference window used by most tests.
    // Range is exactly 30 days → weekly bucketing branch.
    private static readonly DateTime WindowStart = new(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd   = new(2025, 3, 31, 0, 0, 0, DateTimeKind.Utc);

    public ReportServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _sut = new ReportService(_dbContextFactory, NullLogger<ReportService>.Instance);

        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@reporttest.com",
            NormalizedUserName = "NUTRITIONIST@REPORTTEST.COM",
            Email = "nutritionist@reporttest.com",
            NormalizedEmail = "NUTRITIONIST@REPORTTEST.COM",
            FirstName = "Jane",
            LastName = "Report",
            DisplayName = "Jane Report",
            CreatedDate = DateTime.UtcNow
        };

        // clientOld — created BEFORE the window; represents a returning client
        var clientOld = new Client
        {
            FirstName = "Old",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = new DateTime(2024, 12, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        // clientNew — created INSIDE the window; represents a new client
        var clientNew = new Client
        {
            FirstName = "New",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = new DateTime(2025, 3, 5, 0, 0, 0, DateTimeKind.Utc)
        };

        // Two extra clients for variety in type-grouping and active-client tests
        var clientExtra1 = new Client
        {
            FirstName = "Extra",
            LastName = "One",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var clientExtra2 = new Client
        {
            FirstName = "Extra",
            LastName = "Two",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.AddRange(clientOld, clientNew, clientExtra1, clientExtra2);
        _dbContext.SaveChanges();

        _clientOldId    = clientOld.Id;
        _clientNewId    = clientNew.Id;
        _clientExtra1Id = clientExtra1.Id;
        _clientExtra2Id = clientExtra2.Id;

        // Seed appointments inside the window
        var appointments = new List<Appointment>
        {
            // Completed — for clientOld (returning) and clientNew (new)
            MakeAppointment(_clientOldId, AppointmentStatus.Completed, AppointmentType.FollowUp,
                new DateTime(2025, 3, 3, 10, 0, 0, DateTimeKind.Utc)),
            MakeAppointment(_clientNewId, AppointmentStatus.Completed, AppointmentType.InitialConsultation,
                new DateTime(2025, 3, 6, 10, 0, 0, DateTimeKind.Utc)),
            MakeAppointment(_clientExtra1Id, AppointmentStatus.Completed, AppointmentType.FollowUp,
                new DateTime(2025, 3, 10, 10, 0, 0, DateTimeKind.Utc)),

            // NoShow
            MakeAppointment(_clientExtra2Id, AppointmentStatus.NoShow, AppointmentType.CheckIn,
                new DateTime(2025, 3, 12, 9, 0, 0, DateTimeKind.Utc)),

            // Cancelled
            MakeAppointment(_clientExtra1Id, AppointmentStatus.Cancelled, AppointmentType.CheckIn,
                new DateTime(2025, 3, 15, 9, 0, 0, DateTimeKind.Utc)),

            // LateCancellation
            MakeAppointment(_clientExtra2Id, AppointmentStatus.LateCancellation, AppointmentType.FollowUp,
                new DateTime(2025, 3, 20, 9, 0, 0, DateTimeKind.Utc)),

            // Scheduled (in range but not Completed/NoShow/Cancelled — counts toward active clients)
            MakeAppointment(_clientOldId, AppointmentStatus.Scheduled, AppointmentType.CheckIn,
                new DateTime(2025, 3, 25, 11, 0, 0, DateTimeKind.Utc)),
        };

        _dbContext.Appointments.AddRange(appointments);
        _dbContext.SaveChanges();
    }

    private Appointment MakeAppointment(int clientId, AppointmentStatus status, AppointmentType type, DateTime startTime) =>
        new()
        {
            ClientId        = clientId,
            NutritionistId  = NutritionistId,
            Type            = type,
            Status          = status,
            StartTime       = startTime,
            DurationMinutes = 60,
            CreatedAt       = DateTime.UtcNow
        };

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // ── GetPracticeSummaryAsync: Aggregation ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsTotalVisits_OnlyCompletedAppointmentsCount()
    {
        // Arrange: seeded data has 3 Completed appointments in the window.

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.TotalVisits.Should().Be(3);
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsNewClients_ClientsCreatedWithinRange()
    {
        // Arrange: only clientNew was created inside the window (2025-03-05).

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.NewClients.Should().Be(1);
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsReturningClients_ClientsCreatedBeforeRangeWithCompletedAppointments()
    {
        // Arrange: clientOld (created 2024-12-01) and clientExtra1 (created 2024-06-01)
        // both have Completed appointments inside the window → 2 returning clients.

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.ReturningClients.Should().Be(2);
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsNoShows_CountAndRateAreAccurate()
    {
        // Arrange: 1 NoShow out of 7 total appointments in window.
        // Expected rate = round(1/7 * 100, 1) = 14.3%

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.NoShowCount.Should().Be(1);
        result.NoShowRate.Should().Be(Math.Round(1m / 7m * 100m, 1));
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsCancellations_BothCancelledAndLateCancellationIncluded()
    {
        // Arrange: 1 Cancelled + 1 LateCancellation = 2 cancellations out of 7 total.
        // Expected rate = round(2/7 * 100, 1) = 28.6%

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.CancellationCount.Should().Be(2);
        result.CancellationRate.Should().Be(Math.Round(2m / 7m * 100m, 1));
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_CorrectlyCountsActiveClients_DistinctClientsWithNonCancelledAppointments()
    {
        // Arrange:
        // Completed  → clientOld, clientNew, clientExtra1
        // NoShow     → clientExtra2
        // Scheduled  → clientOld  (duplicate, still counted once)
        // Cancelled  → clientExtra1  (excluded)
        // LateCancellation → clientExtra2  (excluded)
        //
        // Non-cancelled statuses: Completed (3), NoShow (1), Scheduled (1)
        // Distinct client IDs with at least one non-cancelled appointment:
        //   clientOld, clientNew, clientExtra1, clientExtra2 → 4

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert
        result.ActiveClients.Should().Be(4);
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_GroupsAppointmentsByType_OrderedByCountDescending()
    {
        // Arrange:
        // FollowUp            → 3 (clientOld Completed, clientExtra1 Completed, clientExtra2 LateCancellation)
        // CheckIn             → 3 (clientExtra2 NoShow, clientExtra1 Cancelled, clientOld Scheduled)
        // InitialConsultation → 1 (clientNew Completed)
        //
        // FollowUp and CheckIn are tied at 3; InitialConsultation is last at 1.

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert: 3 distinct type groups
        result.AppointmentsByType.Should().HaveCount(3);

        // The two tied types both appear with count 3, in the first two positions
        var topTwo = result.AppointmentsByType.Take(2).ToList();
        topTwo.Should().AllSatisfy(x => x.Count.Should().Be(3));
        topTwo.Select(x => x.Type).Should().BeEquivalentTo(
            new[] { AppointmentType.FollowUp.ToString(), AppointmentType.CheckIn.ToString() });

        // InitialConsultation is the lowest-count type, placed last
        result.AppointmentsByType[2].Type.Should().Be(AppointmentType.InitialConsultation.ToString());
        result.AppointmentsByType[2].Count.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // ── GetPracticeSummaryAsync: Trend Bucketing ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPracticeSummaryAsync_DailyBuckets_WhenRangeIs14DaysOrLess()
    {
        // Arrange: 7-day window with a single appointment on day 3.
        var start = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2025, 6, 8, 0, 0, 0, DateTimeKind.Utc); // 7 days

        var client = new Client
        {
            FirstName = "Daily",
            LastName  = "Bucket",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = start
        };
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _dbContext.Appointments.Add(MakeAppointment(
            client.Id, AppointmentStatus.Completed, AppointmentType.CheckIn,
            new DateTime(2025, 6, 3, 10, 0, 0, DateTimeKind.Utc)));
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPracticeSummaryAsync(start, end);

        // Assert: 7 daily buckets (Jun 1 through Jun 7)
        result.TrendData.Should().HaveCount(7);

        // Labels should follow "MMM d" pattern
        result.TrendData[0].Label.Should().Be("Jun 1");
        result.TrendData[6].Label.Should().Be("Jun 7");

        // The single completed appointment lands in the Jun 3 bucket (index 2)
        result.TrendData[2].Label.Should().Be("Jun 3");
        result.TrendData[2].Visits.Should().Be(1);

        // All other buckets should have zero visits
        result.TrendData.Where(b => b.Label != "Jun 3").Should().AllSatisfy(b => b.Visits.Should().Be(0));
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_WeeklyBuckets_WhenRangeIs15To90Days()
    {
        // Arrange: the default 30-day window (WindowStart → WindowEnd) already seeds
        // appointments; 30 days falls in the 15-90 day branch → weekly buckets.

        // Act
        var result = await _sut.GetPracticeSummaryAsync(WindowStart, WindowEnd);

        // Assert: with a 30-day window the service creates weeks of 7 days each
        // (4 full weeks + partial remainder = 5 buckets).
        // Week labels follow "W{ISO week number}" pattern.
        result.TrendData.Should().NotBeEmpty();
        result.TrendData.Should().HaveCountGreaterThan(1,
            because: "a 30-day range must produce multiple weekly buckets");

        // Every label should start with "W" followed by digits
        result.TrendData.Should().AllSatisfy(b =>
            b.Label.Should().MatchRegex(@"^W\d+$",
                because: "weekly bucket labels are ISO week numbers prefixed with 'W'"));

        // Total visits across all buckets equals the total in the summary
        result.TrendData.Sum(b => b.Visits).Should().Be(result.TotalVisits);
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_MonthlyBuckets_WhenRangeExceeds90Days()
    {
        // Arrange: 6-month window with appointments spread across different months.
        var start = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2025, 7, 1, 0, 0, 0, DateTimeKind.Utc); // 181 days

        var client = new Client
        {
            FirstName = "Monthly",
            LastName  = "Bucket",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = start
        };
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _dbContext.Appointments.AddRange(
            MakeAppointment(client.Id, AppointmentStatus.Completed, AppointmentType.FollowUp,
                new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc)),
            MakeAppointment(client.Id, AppointmentStatus.Completed, AppointmentType.FollowUp,
                new DateTime(2025, 4, 10, 10, 0, 0, DateTimeKind.Utc)),
            MakeAppointment(client.Id, AppointmentStatus.NoShow, AppointmentType.CheckIn,
                new DateTime(2025, 6, 20, 9, 0, 0, DateTimeKind.Utc))
        );
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPracticeSummaryAsync(start, end);

        // Assert: 6 monthly buckets (Jan through Jun)
        result.TrendData.Should().HaveCount(6);
        result.TrendData[0].Label.Should().Be("Jan 2025");
        result.TrendData[5].Label.Should().Be("Jun 2025");

        // January bucket has 1 visit
        result.TrendData[0].Visits.Should().Be(1);

        // April bucket (index 3) has 1 visit
        result.TrendData[3].Visits.Should().Be(1);

        // June bucket (index 5) has 1 no-show
        result.TrendData[5].NoShows.Should().Be(1);
    }

    // ---------------------------------------------------------------------------
    // ── GetPracticeSummaryAsync: Edge Cases ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPracticeSummaryAsync_EmptyRange_ReturnsZerosAndEmptyCollections()
    {
        // Arrange: a window with no appointments and no clients created in it.
        var emptyStart = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var emptyEnd   = new DateTime(2020, 1, 8, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var result = await _sut.GetPracticeSummaryAsync(emptyStart, emptyEnd);

        // Assert
        result.TotalVisits.Should().Be(0);
        result.NewClients.Should().Be(0);
        result.ReturningClients.Should().Be(0);
        result.NoShowCount.Should().Be(0);
        result.NoShowRate.Should().Be(0m);
        result.CancellationCount.Should().Be(0);
        result.CancellationRate.Should().Be(0m);
        result.ActiveClients.Should().Be(0);
        result.AppointmentsByType.Should().BeEmpty();
        // 7 daily buckets for a 7-day range, all zeroed
        result.TrendData.Should().HaveCount(7);
        result.TrendData.Should().AllSatisfy(b =>
        {
            b.Visits.Should().Be(0);
            b.NoShows.Should().Be(0);
            b.Cancellations.Should().Be(0);
        });
    }

    [Fact]
    public async Task GetPracticeSummaryAsync_NoCompletedAppointments_ReturnsZeroReturningClients()
    {
        // Arrange: window with only NoShow and Cancelled appointments for a pre-existing client.
        var start = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = new DateTime(2025, 9, 8, 0, 0, 0, DateTimeKind.Utc);

        // Use clientOld (created 2024-12-01, before this window) — if it had Completed
        // appointments in this window it would be a returning client, but we deliberately
        // only add non-Completed appointments.
        _dbContext.Appointments.AddRange(
            MakeAppointment(_clientOldId, AppointmentStatus.NoShow, AppointmentType.CheckIn,
                new DateTime(2025, 9, 3, 10, 0, 0, DateTimeKind.Utc)),
            MakeAppointment(_clientOldId, AppointmentStatus.Cancelled, AppointmentType.FollowUp,
                new DateTime(2025, 9, 5, 10, 0, 0, DateTimeKind.Utc))
        );
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.GetPracticeSummaryAsync(start, end);

        // Assert
        result.TotalVisits.Should().Be(0,
            because: "no Completed appointments exist in this range");
        result.ReturningClients.Should().Be(0,
            because: "returning-client logic requires Completed appointments to identify client IDs");
    }
}
