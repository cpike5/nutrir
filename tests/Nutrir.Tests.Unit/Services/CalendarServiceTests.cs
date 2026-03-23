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

/// <summary>
/// Unit tests for CalendarService.GetAppointmentsByDateRangeAsync.
///
/// Key behaviours under test:
///   - Appointments whose computed end time (StartTime + DurationMinutes) falls within
///     the requested range are returned.
///   - Appointments completely outside the range (both start and end before or after) are excluded.
///   - The 90-minute look-back buffer allows appointments that started before the range
///     but are still running at range-start to be returned.
///   - Soft-deleted appointments are always excluded.
///   - An empty range (no qualifying appointments) returns an empty list.
///   - The returned title is formatted as "h:mm tt · FirstName LastName".
/// </summary>
public class CalendarServiceTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------------

    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly CalendarService _sut;

    private const string NutritionistId = "nutritionist-calendar-test-001";

    private int _clientId;

    public CalendarServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        var dbContextFactory = new SharedConnectionContextFactory(_connection);

        _sut = new CalendarService(
            dbContextFactory,
            NullLogger<CalendarService>.Instance);

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
            UserName = "nutritionist@calendartest.com",
            NormalizedUserName = "NUTRITIONIST@CALENDARTEST.COM",
            Email = "nutritionist@calendartest.com",
            NormalizedEmail = "NUTRITIONIST@CALENDARTEST.COM",
            FirstName = "Jane",
            LastName = "Doe",
            DisplayName = "Jane Doe",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Calendar",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _clientId = client.Id;
    }

    /// <summary>
    /// Inserts an Appointment directly into the database, bypassing any service logic.
    /// </summary>
    private Appointment SeedAppointment(
        DateTime startTime,
        int durationMinutes = 60,
        AppointmentType type = AppointmentType.FollowUp,
        AppointmentStatus status = AppointmentStatus.Scheduled,
        bool isDeleted = false)
    {
        var appointment = new Appointment
        {
            ClientId = _clientId,
            NutritionistId = NutritionistId,
            Type = type,
            Status = status,
            StartTime = startTime,
            DurationMinutes = durationMinutes,
            Location = AppointmentLocation.Virtual,
            IsDeleted = isDeleted,
            DeletedAt = isDeleted ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();
        return appointment;
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — appointments within range
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_WithAppointmentsInRange_ReturnsMatching()
    {
        // Arrange
        var rangeStart = new DateTime(2025, 6, 10, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 10, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(new DateTime(2025, 6, 10, 10, 0, 0, DateTimeKind.Utc), durationMinutes: 60);
        SeedAppointment(new DateTime(2025, 6, 10, 14, 0, 0, DateTimeKind.Utc), durationMinutes: 30);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().HaveCount(2, because: "both appointments start and end within the requested range");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_WithAppointmentsInRange_ReturnsCorrectIds()
    {
        // Arrange
        var rangeStart = new DateTime(2025, 6, 11, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 11, 17, 0, 0, DateTimeKind.Utc);

        var inRange = SeedAppointment(new DateTime(2025, 6, 11, 11, 0, 0, DateTimeKind.Utc));

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle(r => r.Id == inRange.Id);
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — appointments outside range
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_ExcludesOutsideRange()
    {
        // Arrange
        var rangeStart = new DateTime(2025, 6, 15, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 15, 17, 0, 0, DateTimeKind.Utc);

        // Ends before range start — completely before
        SeedAppointment(new DateTime(2025, 6, 15, 7, 0, 0, DateTimeKind.Utc), durationMinutes: 30);

        // Starts after range end — completely after
        SeedAppointment(new DateTime(2025, 6, 15, 18, 0, 0, DateTimeKind.Utc), durationMinutes: 60);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().BeEmpty(because: "neither appointment overlaps the requested range");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_ExcludesAppointmentEndingExactlyAtRangeStart()
    {
        // Arrange — a 60-minute appointment ending exactly at rangeStart does not overlap.
        var rangeStart = new DateTime(2025, 6, 16, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 16, 17, 0, 0, DateTimeKind.Utc);

        // StartTime = 08:00, Duration = 60 min → EndTime = 09:00 == rangeStart (not > rangeStart)
        SeedAppointment(new DateTime(2025, 6, 16, 8, 0, 0, DateTimeKind.Utc), durationMinutes: 60);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().BeEmpty(
            because: "an appointment whose EndTime equals rangeStart does not satisfy EndTime > rangeStart");
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — 90-minute buffer overlap
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_IncludesOverlappingAppointment()
    {
        // Arrange — appointment starts 60 minutes before rangeStart but its 90-minute
        // duration means it is still running when the range opens. The 90-min buffer
        // ensures the query fetches it so the in-memory filter can include it.
        var rangeStart = new DateTime(2025, 6, 20, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 20, 17, 0, 0, DateTimeKind.Utc);

        // StartTime = 08:00, Duration = 90 min → EndTime = 09:30 > rangeStart (09:00)
        var overlapping = SeedAppointment(
            new DateTime(2025, 6, 20, 8, 0, 0, DateTimeKind.Utc),
            durationMinutes: 90);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle(r => r.Id == overlapping.Id,
            because: "the appointment ends after rangeStart so it overlaps the requested window");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_ExcludesAppointmentBeyondBuffer()
    {
        // Arrange — appointment starts more than 90 minutes before rangeStart AND ends
        // before rangeStart, so it falls outside even the buffered query window.
        var rangeStart = new DateTime(2025, 6, 21, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 6, 21, 17, 0, 0, DateTimeKind.Utc);

        // StartTime = 07:00 (120 min before rangeStart), Duration = 60 min → EndTime = 08:00
        // bufferStart = rangeStart - 90 min = 07:30 → StartTime (07:00) < bufferStart, excluded by DB query
        SeedAppointment(
            new DateTime(2025, 6, 21, 7, 0, 0, DateTimeKind.Utc),
            durationMinutes: 60);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().BeEmpty(
            because: "the appointment started more than 90 minutes before the range and ended before it");
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — soft-deleted exclusion
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_ExcludesSoftDeleted()
    {
        // Arrange
        var rangeStart = new DateTime(2025, 7, 1, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 7, 1, 17, 0, 0, DateTimeKind.Utc);

        var active  = SeedAppointment(new DateTime(2025, 7, 1, 10, 0, 0, DateTimeKind.Utc), isDeleted: false);
        var deleted = SeedAppointment(new DateTime(2025, 7, 1, 11, 0, 0, DateTimeKind.Utc), isDeleted: true);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle(r => r.Id == active.Id,
            because: "only the non-deleted appointment should be returned");
        results.Should().NotContain(r => r.Id == deleted.Id,
            because: "soft-deleted appointments must be excluded by the IsDeleted filter");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_AllSoftDeleted_ReturnsEmpty()
    {
        // Arrange
        var rangeStart = new DateTime(2025, 7, 2, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 7, 2, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(new DateTime(2025, 7, 2, 10, 0, 0, DateTimeKind.Utc), isDeleted: true);
        SeedAppointment(new DateTime(2025, 7, 2, 12, 0, 0, DateTimeKind.Utc), isDeleted: true);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().BeEmpty(because: "all seeded appointments are soft-deleted");
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — empty range
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_EmptyRange_ReturnsEmptyList()
    {
        // Arrange — use a date range with no seeded appointments
        var rangeStart = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2030, 1, 1, 23, 59, 59, DateTimeKind.Utc);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().BeEmpty(because: "no appointments exist within the given date range");
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — title format
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_FormatsTitle()
    {
        // Arrange — use a fixed time that has a predictable "h:mm tt" representation
        // 02:30 PM → "2:30 PM"
        var startTime  = new DateTime(2025, 8, 5, 14, 30, 0, DateTimeKind.Utc);
        var rangeStart = new DateTime(2025, 8, 5, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 8, 5, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(startTime);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle();
        var expectedTitle = $"{startTime:h:mm tt} · Alice Calendar";
        results[0].Title.Should().Be(expectedTitle,
            because: "the title must follow the 'h:mm tt · FirstName LastName' pattern");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_FormatsTitle_MidnightHour()
    {
        // Arrange — 12:00 AM uses "12:00 AM" in h:mm tt format
        var startTime  = new DateTime(2025, 8, 6, 0, 0, 0, DateTimeKind.Utc);
        var rangeStart = new DateTime(2025, 8, 5, 23, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 8, 6, 2, 0, 0, DateTimeKind.Utc);

        SeedAppointment(startTime);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle();
        var expectedTitle = $"{startTime:h:mm tt} · Alice Calendar";
        results[0].Title.Should().Be(expectedTitle,
            because: "midnight should render as '12:00 AM' in the h:mm tt format");
    }

    // ---------------------------------------------------------------------------
    // GetAppointmentsByDateRangeAsync — DTO field mapping
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_WithAppointmentsInRange_MapsStartTimeCorrectly()
    {
        // Arrange
        var startTime  = new DateTime(2025, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        var rangeStart = new DateTime(2025, 9, 1, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 9, 1, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(startTime, durationMinutes: 60);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle();
        results[0].StartTime.Should().Be(startTime);
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_WithAppointmentsInRange_MapsEndTimeCorrectly()
    {
        // Arrange
        var startTime    = new DateTime(2025, 9, 2, 10, 0, 0, DateTimeKind.Utc);
        var expectedEnd  = startTime.AddMinutes(45);
        var rangeStart   = new DateTime(2025, 9, 2, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd     = new DateTime(2025, 9, 2, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(startTime, durationMinutes: 45);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle();
        results[0].EndTime.Should().Be(expectedEnd,
            because: "EndTime should equal StartTime plus DurationMinutes");
    }

    [Fact]
    public async Task GetAppointmentsByDateRangeAsync_WithAppointmentsInRange_MapsTypeAndStatus()
    {
        // Arrange
        var startTime  = new DateTime(2025, 9, 3, 10, 0, 0, DateTimeKind.Utc);
        var rangeStart = new DateTime(2025, 9, 3, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd   = new DateTime(2025, 9, 3, 17, 0, 0, DateTimeKind.Utc);

        SeedAppointment(startTime,
            type: AppointmentType.InitialConsultation,
            status: AppointmentStatus.Confirmed);

        // Act
        var results = await _sut.GetAppointmentsByDateRangeAsync(rangeStart, rangeEnd);

        // Assert
        results.Should().ContainSingle();
        results[0].Type.Should().Be(AppointmentType.InitialConsultation);
        results[0].Status.Should().Be(AppointmentStatus.Confirmed);
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
