using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Exceptions;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// Minimal IDbContextFactory implementation that always returns the same pre-created context.
/// </summary>
file sealed class FixedContextFactory(AppDbContext context) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => context;
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(context);
}

public class AppointmentOverlapTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IAvailabilityService _availabilityService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ISessionNoteService _sessionNoteService;
    private readonly IRetentionTracker _retentionTracker;

    private readonly AppointmentService _sut;

    private const string NutritionistId = "nutritionist-overlap-user-001";
    private const int ConsentClientId = 10;

    public AppointmentOverlapTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _availabilityService = Substitute.For<IAvailabilityService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _sessionNoteService = Substitute.For<ISessionNoteService>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        var dbContextFactory = new FixedContextFactory(_dbContext);

        // Availability always approves so overlap logic is the only gate under test
        _availabilityService
            .IsSlotWithinScheduleAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns((true, (string?)null));

        _sut = new AppointmentService(
            _dbContext,
            dbContextFactory,
            _auditLogService,
            _availabilityService,
            _notificationDispatcher,
            _sessionNoteService,
            _retentionTracker,
            NullLogger<AppointmentService>.Instance);

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
            UserName = "overlap-nutritionist@test.com",
            NormalizedUserName = "OVERLAP-NUTRITIONIST@TEST.COM",
            Email = "overlap-nutritionist@test.com",
            NormalizedEmail = "OVERLAP-NUTRITIONIST@TEST.COM",
            FirstName = "Test",
            LastName = "Nutritionist",
            DisplayName = "Test Nutritionist",
            BufferTimeMinutes = 0, // Disable buffer so tests reason only about direct overlap
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            Id = ConsentClientId,
            FirstName = "Overlap",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();
    }

    private static CreateAppointmentDto BuildDto(DateTime startTime, int durationMinutes = 60) =>
        new(
            ClientId: ConsentClientId,
            Type: AppointmentType.FollowUp,
            StartTime: startTime,
            DurationMinutes: durationMinutes,
            Location: AppointmentLocation.InPerson,
            VirtualMeetingUrl: null,
            LocationNotes: null,
            Notes: null,
            PrepNotes: null);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_NoOverlap_Succeeds()
    {
        // Arrange — first appointment 10:00-11:00, second at 12:00-13:00 (no gap needed
        // because BufferTimeMinutes is 0 for the seeded user)
        var baseDate = new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        var firstDto = BuildDto(baseDate, durationMinutes: 60);
        var secondDto = BuildDto(baseDate.AddHours(2), durationMinutes: 60);

        await _sut.CreateAsync(firstDto, NutritionistId);

        // Act
        var act = () => _sut.CreateAsync(secondDto, NutritionistId);

        // Assert — should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateAsync_WithOverlap_ThrowsSchedulingConflictException()
    {
        // Arrange — first appointment 10:00-11:00, second at 10:30-11:30 (overlapping)
        var baseDate = new DateTime(2026, 6, 2, 10, 0, 0, DateTimeKind.Utc);
        var firstDto = BuildDto(baseDate, durationMinutes: 60);
        var overlappingDto = BuildDto(baseDate.AddMinutes(30), durationMinutes: 60);

        await _sut.CreateAsync(firstDto, NutritionistId);

        // Act
        var act = () => _sut.CreateAsync(overlappingDto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<SchedulingConflictException>();
    }

    [Fact]
    public async Task CreateAsync_OverlapWithCancelled_Succeeds()
    {
        // Arrange — seed a cancelled appointment directly at 10:00-11:00, then
        // try to book the same slot. Cancelled appointments must not block new bookings.
        var baseDate = new DateTime(2026, 6, 3, 10, 0, 0, DateTimeKind.Utc);

        var cancelledAppointment = new Appointment
        {
            ClientId = ConsentClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Cancelled,
            StartTime = baseDate,
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.Add(cancelledAppointment);
        await _dbContext.SaveChangesAsync();

        var dto = BuildDto(baseDate, durationMinutes: 60);

        // Act
        var act = () => _sut.CreateAsync(dto, NutritionistId);

        // Assert — cancelled slot must be bookable again
        await act.Should().NotThrowAsync();
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
