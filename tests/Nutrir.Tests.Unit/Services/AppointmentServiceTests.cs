using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
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
/// Factory that creates NEW TestAppDbContext instances sharing the same SQLite connection.
/// Required for testing methods that use IDbContextFactory (GetTodaysAppointmentsAsync,
/// GetUpcomingByClientAsync, GetListAsync) which dispose their context via 'await using'.
/// The shared connection keeps schema and data intact across multiple disposals.
/// </summary>
file sealed class SharedConnectionContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new TestAppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}

/// <summary>
/// Unit tests for AppointmentService covering GetByIdAsync, UpdateAsync, UpdateStatusAsync,
/// SoftDeleteAsync, GetTodaysAppointmentsAsync, GetUpcomingByClientAsync, GetWeekCountAsync,
/// CreateRecurringAsync, and buffer-time overlap behaviour.
///
/// What is NOT tested here (already covered by sibling test classes):
///   - Consent gating on CreateAsync / CreateRecurringAsync  (AppointmentConsentTests)
///   - Direct overlap detection on CreateAsync               (AppointmentOverlapTests)
///   - Static AppointmentStatusTransitions logic             (AppointmentStatusTransitionTests)
/// </summary>
public class AppointmentServiceTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------------

    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IAvailabilityService _availabilityService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ISessionNoteService _sessionNoteService;
    private readonly IRetentionTracker _retentionTracker;

    private readonly AppointmentService _sut;

    private const string NutritionistId = "nutritionist-svc-tests-001";
    private const string BufferNutritionistId = "nutritionist-buffer-001";
    private const string UserId = "acting-user-001";
    private int _clientId;

    public AppointmentServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _availabilityService = Substitute.For<IAvailabilityService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _sessionNoteService = Substitute.For<ISessionNoteService>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        // Availability always approves — consent and overlap are tested elsewhere.
        _availabilityService
            .IsSlotWithinScheduleAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns((true, (string?)null));

        // Use SharedConnectionContextFactory for factory-based methods so that
        // 'await using' disposal in the service does not tear down the shared schema.
        var dbContextFactory = new SharedConnectionContextFactory(_connection);

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
            UserName = "svc-nutritionist@test.com",
            NormalizedUserName = "SVC-NUTRITIONIST@TEST.COM",
            Email = "svc-nutritionist@test.com",
            NormalizedEmail = "SVC-NUTRITIONIST@TEST.COM",
            FirstName = "Jane",
            LastName = "Doe",
            DisplayName = "Jane Doe",
            BufferTimeMinutes = 0, // Simplifies overlap reasoning for most tests
            CreatedDate = DateTime.UtcNow
        };

        // Second nutritionist with a 15-minute buffer — used for buffer-time tests only.
        var bufferNutritionist = new ApplicationUser
        {
            Id = BufferNutritionistId,
            UserName = "buffer-nutritionist@test.com",
            NormalizedUserName = "BUFFER-NUTRITIONIST@TEST.COM",
            Email = "buffer-nutritionist@test.com",
            NormalizedEmail = "BUFFER-NUTRITIONIST@TEST.COM",
            FirstName = "Buffer",
            LastName = "Nutritionist",
            DisplayName = "Buffer Nutritionist",
            BufferTimeMinutes = 15,
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Test",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Users.Add(bufferNutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _clientId = client.Id;
    }

    /// <summary>
    /// Inserts an Appointment directly into the database, bypassing CreateAsync.
    /// Used to arrange test state without triggering overlap, consent, or audit side-effects.
    /// </summary>
    private Appointment SeedAppointment(
        DateTime startTime,
        AppointmentStatus status = AppointmentStatus.Scheduled,
        int? durationMinutes = null,
        string? nutritionistId = null)
    {
        var appointment = new Appointment
        {
            ClientId = _clientId,
            NutritionistId = nutritionistId ?? NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = status,
            StartTime = startTime,
            DurationMinutes = durationMinutes ?? 60,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();
        return appointment;
    }

    private static UpdateAppointmentDto BuildUpdateDto(
        Appointment existing,
        DateTime? startTime = null,
        int? durationMinutes = null,
        AppointmentStatus? status = null,
        string? notes = null,
        string? prepNotes = null) =>
        new(
            Id: existing.Id,
            Type: existing.Type,
            Status: status ?? existing.Status,
            StartTime: startTime ?? existing.StartTime,
            DurationMinutes: durationMinutes ?? existing.DurationMinutes,
            Location: existing.Location,
            VirtualMeetingUrl: existing.VirtualMeetingUrl,
            LocationNotes: existing.LocationNotes,
            Notes: notes ?? existing.Notes,
            PrepNotes: prepNotes ?? existing.PrepNotes);

    private static CreateAppointmentDto BuildCreateDto(
        int clientId,
        DateTime startTime,
        int durationMinutes = 60) =>
        new(
            ClientId: clientId,
            Type: AppointmentType.FollowUp,
            StartTime: startTime,
            DurationMinutes: durationMinutes,
            Location: AppointmentLocation.Virtual,
            VirtualMeetingUrl: null,
            LocationNotes: null,
            Notes: null,
            PrepNotes: null);

    // ---------------------------------------------------------------------------
    // GetByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ExistingAppointment_ReturnsDto()
    {
        // Arrange
        var appointment = SeedAppointment(DateTime.UtcNow.AddDays(1));

        // Act
        var result = await _sut.GetByIdAsync(appointment.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(appointment.Id);
        result.ClientId.Should().Be(_clientId);
        result.NutritionistId.Should().Be(NutritionistId);
        result.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task GetByIdAsync_MissingId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_SoftDeletedAppointment_ReturnsNull()
    {
        // Arrange — seed then soft-delete directly in the DB
        var appointment = SeedAppointment(DateTime.UtcNow.AddDays(1));
        appointment.IsDeleted = true;
        appointment.DeletedAt = DateTime.UtcNow;
        appointment.DeletedBy = UserId;
        _dbContext.SaveChanges();

        // Act — the global query filter should exclude soft-deleted rows
        var result = await _sut.GetByIdAsync(appointment.Id);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // UpdateAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_ValidUpdate_ReturnsTrueAndPersistsFields()
    {
        // Arrange
        var appointment = SeedAppointment(new DateTime(2027, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        var newNotes = "Updated consultation notes";
        var dto = BuildUpdateDto(appointment, notes: newNotes);

        // Act
        var result = await _sut.UpdateAsync(dto, UserId);

        // Assert
        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Notes.Should().Be(newNotes);
        persisted.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ValidUpdate_CallsAuditLog()
    {
        // Arrange
        var appointment = SeedAppointment(new DateTime(2027, 6, 2, 10, 0, 0, DateTimeKind.Utc));
        var dto = BuildUpdateDto(appointment, notes: "Audited update");

        // Act
        await _sut.UpdateAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AppointmentUpdated",
            "Appointment",
            appointment.Id.ToString(),
            Arg.Is<string>(s => s.Contains(appointment.Id.ToString())));
    }

    [Fact]
    public async Task UpdateAsync_MissingId_ReturnsFalse()
    {
        // Arrange — DTO references an appointment that does not exist
        var dto = new UpdateAppointmentDto(
            Id: 99999,
            Type: AppointmentType.FollowUp,
            Status: AppointmentStatus.Scheduled,
            StartTime: DateTime.UtcNow.AddDays(1),
            DurationMinutes: 60,
            Location: AppointmentLocation.Virtual,
            VirtualMeetingUrl: null,
            LocationNotes: null,
            Notes: null,
            PrepNotes: null);

        // Act
        var result = await _sut.UpdateAsync(dto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_InvalidStatusTransition_ThrowsInvalidStatusTransitionException()
    {
        // Arrange — Scheduled → Completed is not a permitted transition
        var appointment = SeedAppointment(new DateTime(2027, 6, 3, 10, 0, 0, DateTimeKind.Utc));
        var dto = BuildUpdateDto(appointment, status: AppointmentStatus.Completed);

        // Act
        var act = () => _sut.UpdateAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidStatusTransitionException>();
    }

    [Fact]
    public async Task UpdateAsync_TimeChangeOutsideWorkingHours_ThrowsSchedulingConflictException()
    {
        // Arrange — seed an appointment, then configure availability to reject the new time
        var appointment = SeedAppointment(new DateTime(2027, 6, 4, 10, 0, 0, DateTimeKind.Utc));
        var newStartTime = new DateTime(2027, 6, 4, 22, 0, 0, DateTimeKind.Utc);
        var dto = BuildUpdateDto(appointment, startTime: newStartTime);

        _availabilityService
            .IsSlotWithinScheduleAsync(Arg.Any<string>(), newStartTime, Arg.Any<int>())
            .Returns((false, "Outside working hours"));

        // Act
        var act = () => _sut.UpdateAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<SchedulingConflictException>();
    }

    [Fact]
    public async Task UpdateAsync_TimeChangeWithinWorkingHours_Succeeds()
    {
        // Arrange — seed an appointment, then reschedule it to a new valid time
        var appointment = SeedAppointment(new DateTime(2027, 6, 5, 10, 0, 0, DateTimeKind.Utc));
        var newStartTime = new DateTime(2027, 6, 5, 14, 0, 0, DateTimeKind.Utc);
        var dto = BuildUpdateDto(appointment, startTime: newStartTime);

        // Act
        var result = await _sut.UpdateAsync(dto, UserId);

        // Assert
        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.StartTime.Should().Be(newStartTime);
    }

    // ---------------------------------------------------------------------------
    // UpdateStatusAsync — valid transitions
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateStatusAsync_ScheduledToConfirmed_ReturnsTrueAndPersistsStatus()
    {
        // Arrange
        var appointment = SeedAppointment(new DateTime(2027, 7, 1, 9, 0, 0, DateTimeKind.Utc));

        // Act
        var result = await _sut.UpdateStatusAsync(appointment.Id, AppointmentStatus.Confirmed, UserId);

        // Assert
        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Status.Should().Be(AppointmentStatus.Confirmed);
    }

    [Fact]
    public async Task UpdateStatusAsync_ScheduledToCancelled_SetsCancellationFields()
    {
        // Arrange
        var appointment = SeedAppointment(new DateTime(2027, 7, 2, 9, 0, 0, DateTimeKind.Utc));
        const string reason = "Client rescheduled";

        // Act
        var result = await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.Cancelled, UserId, reason);

        // Assert
        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Status.Should().Be(AppointmentStatus.Cancelled);
        persisted.CancellationReason.Should().Be(reason);
        persisted.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ScheduledToLateCancellation_SetsCancellationFields()
    {
        // Arrange
        var appointment = SeedAppointment(new DateTime(2027, 7, 3, 9, 0, 0, DateTimeKind.Utc));
        const string reason = "Last-minute cancellation";

        // Act
        await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.LateCancellation, UserId, reason);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Status.Should().Be(AppointmentStatus.LateCancellation);
        persisted.CancellationReason.Should().Be(reason);
        persisted.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ConfirmedToCompleted_ReturnsTrueAndTriggersDraftNote()
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 7, 4, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);

        // Act
        var result = await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.Completed, UserId);

        // Assert
        result.Should().BeTrue();
        await _sessionNoteService.Received(1)
            .CreateDraftAsync(appointment.Id, _clientId, UserId);
    }

    [Fact]
    public async Task UpdateStatusAsync_ConfirmedToNoShow_ReturnsTrueAndDoesNotTriggerDraftNote()
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 7, 5, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);

        // Act
        var result = await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.NoShow, UserId);

        // Assert
        result.Should().BeTrue();
        await _sessionNoteService.DidNotReceive()
            .CreateDraftAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateStatusAsync_ConfirmedToCancelled_SetsCancellationFields()
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 7, 6, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);
        const string reason = "Practitioner unavailable";

        // Act
        await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.Cancelled, UserId, reason);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Status.Should().Be(AppointmentStatus.Cancelled);
        persisted.CancellationReason.Should().Be(reason);
        persisted.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ConfirmedToLateCancellation_SetsCancellationFields()
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 7, 7, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);

        // Act
        await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.LateCancellation, UserId, "No-show risk");

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.Status.Should().Be(AppointmentStatus.LateCancellation);
        persisted.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_NonCancellationTransition_DoesNotSetCancellationFields()
    {
        // Arrange — confirm that a plain status change to non-cancellation does not
        // set CancelledAt. Use Scheduled → Confirmed (a non-cancellation transition).
        var appointment = SeedAppointment(
            new DateTime(2027, 7, 8, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled);

        // Act
        await _sut.UpdateStatusAsync(appointment.Id, AppointmentStatus.Confirmed, UserId);

        // Assert
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments.FindAsync(appointment.Id);
        persisted!.CancelledAt.Should().BeNull();
        persisted.CancellationReason.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // UpdateStatusAsync — invalid transitions (terminal states)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.LateCancellation)]
    public async Task UpdateStatusAsync_CompletedToAny_ThrowsInvalidStatusTransitionException(
        AppointmentStatus target)
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 8, 1, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Completed);

        // Act
        var act = () => _sut.UpdateStatusAsync(appointment.Id, target, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidStatusTransitionException>();
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.LateCancellation)]
    public async Task UpdateStatusAsync_CancelledToAny_ThrowsInvalidStatusTransitionException(
        AppointmentStatus target)
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 8, 2, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Cancelled);

        // Act
        var act = () => _sut.UpdateStatusAsync(appointment.Id, target, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidStatusTransitionException>();
    }

    // ---------------------------------------------------------------------------
    // UpdateStatusAsync — audit log and missing id
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateStatusAsync_ValidTransition_CallsAuditLog()
    {
        // Arrange
        var appointment = SeedAppointment(
            new DateTime(2027, 8, 10, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Scheduled);

        // Act
        await _sut.UpdateStatusAsync(appointment.Id, AppointmentStatus.Confirmed, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AppointmentStatusChanged",
            "Appointment",
            appointment.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Scheduled") && s.Contains("Confirmed")));
    }

    [Fact]
    public async Task UpdateStatusAsync_MissingId_ReturnsFalse()
    {
        // Act
        var result = await _sut.UpdateStatusAsync(99999, AppointmentStatus.Confirmed, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStatusAsync_CompletedWithFailingSessionNote_StillReturnsTrue()
    {
        // Arrange — session note service throws, but the failure must be non-fatal
        _sessionNoteService
            .CreateDraftAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException("Draft creation failed"));

        var appointment = SeedAppointment(
            new DateTime(2027, 8, 11, 9, 0, 0, DateTimeKind.Utc),
            AppointmentStatus.Confirmed);

        // Act
        var result = await _sut.UpdateStatusAsync(
            appointment.Id, AppointmentStatus.Completed, UserId);

        // Assert — the service must swallow the exception and return true
        result.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // SoftDeleteAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteAsync_ExistingAppointment_ReturnsTrueAndSetsDeletedFields()
    {
        // Arrange
        var appointment = SeedAppointment(DateTime.UtcNow.AddDays(3));

        // Act
        var result = await _sut.SoftDeleteAsync(appointment.Id, UserId);

        // Assert
        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var persisted = await _dbContext.Appointments
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == appointment.Id);
        persisted!.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task SoftDeleteAsync_ExistingAppointment_CallsAuditLog()
    {
        // Arrange
        var appointment = SeedAppointment(DateTime.UtcNow.AddDays(4));

        // Act
        await _sut.SoftDeleteAsync(appointment.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AppointmentSoftDeleted",
            "Appointment",
            appointment.Id.ToString(),
            Arg.Is<string>(s => s.Contains(appointment.Id.ToString())));
    }

    [Fact]
    public async Task SoftDeleteAsync_MissingId_ReturnsFalse()
    {
        // Act
        var result = await _sut.SoftDeleteAsync(99999, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_SoftDeletedAppointmentIsHiddenFromNormalQueries()
    {
        // Arrange
        var appointment = SeedAppointment(DateTime.UtcNow.AddDays(5));

        // Act
        await _sut.SoftDeleteAsync(appointment.Id, UserId);

        // Assert — global query filter hides it from GetByIdAsync
        var fromService = await _sut.GetByIdAsync(appointment.Id);
        fromService.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetTodaysAppointmentsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetTodaysAppointmentsAsync_AppointmentsForToday_ReturnsThemOrderedByStartTime()
    {
        // Arrange — two appointments today at different times
        var today = DateTime.UtcNow.Date;
        var later = SeedAppointment(today.AddHours(14));
        var earlier = SeedAppointment(today.AddHours(9));

        // Act
        var result = await _sut.GetTodaysAppointmentsAsync(NutritionistId);

        // Assert
        var ids = result.Select(a => a.Id).ToList();
        ids.Should().Contain(earlier.Id);
        ids.Should().Contain(later.Id);
        result.Select(a => a.StartTime).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_AppointmentYesterday_IsExcluded()
    {
        // Arrange
        var yesterday = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);
        var old = SeedAppointment(yesterday);

        // Act
        var result = await _sut.GetTodaysAppointmentsAsync(NutritionistId);

        // Assert
        result.Should().NotContain(a => a.Id == old.Id);
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_AppointmentTomorrow_IsExcluded()
    {
        // Arrange
        var tomorrow = DateTime.UtcNow.Date.AddDays(1).AddHours(10);
        var future = SeedAppointment(tomorrow);

        // Act
        var result = await _sut.GetTodaysAppointmentsAsync(NutritionistId);

        // Assert
        result.Should().NotContain(a => a.Id == future.Id);
    }

    [Fact]
    public async Task GetTodaysAppointmentsAsync_OtherNutritionistAppointments_AreExcluded()
    {
        // Arrange — appointment for buffer nutritionist, not the primary one under test
        var today = DateTime.UtcNow.Date.AddHours(11);
        var otherNutritionist = SeedAppointment(today, nutritionistId: BufferNutritionistId);

        // Act
        var result = await _sut.GetTodaysAppointmentsAsync(NutritionistId);

        // Assert
        result.Should().NotContain(a => a.Id == otherNutritionist.Id);
    }

    // ---------------------------------------------------------------------------
    // GetUpcomingByClientAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetUpcomingByClientAsync_FutureAppointments_ReturnsThemOrderedByStartTime()
    {
        // Arrange — three future appointments at different times
        var base1 = DateTime.UtcNow.AddDays(2);
        var a1 = SeedAppointment(base1.AddHours(2));
        var a2 = SeedAppointment(base1);
        var a3 = SeedAppointment(base1.AddHours(4));

        // Act
        var result = await _sut.GetUpcomingByClientAsync(_clientId, count: 10);

        // Assert
        var ids = result.Select(r => r.Id).ToList();
        ids.Should().Contain([a1.Id, a2.Id, a3.Id]);
        result.Select(r => r.StartTime).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetUpcomingByClientAsync_PastAppointments_AreExcluded()
    {
        // Arrange
        var past = SeedAppointment(DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _sut.GetUpcomingByClientAsync(_clientId, count: 10);

        // Assert
        result.Should().NotContain(a => a.Id == past.Id);
    }

    [Fact]
    public async Task GetUpcomingByClientAsync_RespectsCountLimit()
    {
        // Arrange — seed more appointments than the limit
        var baseTime = DateTime.UtcNow.AddDays(10);
        for (var i = 0; i < 6; i++)
            SeedAppointment(baseTime.AddDays(i));

        // Act
        var result = await _sut.GetUpcomingByClientAsync(_clientId, count: 3);

        // Assert
        result.Should().HaveCount(3);
    }

    // ---------------------------------------------------------------------------
    // GetWeekCountAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetWeekCountAsync_AppointmentsThisWeek_ReturnsCorrectCount()
    {
        // Arrange — seed two appointments within the current week
        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        var midWeek = startOfWeek.AddDays(3).AddHours(10);

        // Clear existing tracker state from previous seeds that may fall in this week
        _retentionTracker.ClearReceivedCalls();

        SeedAppointment(midWeek);
        SeedAppointment(midWeek.AddHours(2));

        // Act
        var count = await _sut.GetWeekCountAsync(NutritionistId);

        // Assert
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetWeekCountAsync_AppointmentsOutsideWeek_AreNotCounted()
    {
        // Arrange — record count before seeding out-of-range appointments
        var countBefore = await _sut.GetWeekCountAsync(NutritionistId);

        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        SeedAppointment(startOfWeek.AddDays(-2)); // last week
        SeedAppointment(startOfWeek.AddDays(8));  // next week

        // Act
        var countAfter = await _sut.GetWeekCountAsync(NutritionistId);

        // Assert — out-of-range appointments must not change the count
        countAfter.Should().Be(countBefore,
            because: "appointments outside the current week should not be counted");
    }

    [Fact]
    public async Task GetWeekCountAsync_OtherNutritionistAppointments_AreNotCounted()
    {
        // Arrange — record the primary nutritionist's count before adding anything,
        // then seed a this-week appointment exclusively for the buffer nutritionist.
        var primaryCountBefore = await _sut.GetWeekCountAsync(NutritionistId);

        var now = DateTime.UtcNow;
        var startOfWeek = now.Date.AddDays(-(int)now.DayOfWeek);
        SeedAppointment(startOfWeek.AddDays(2).AddHours(9), nutritionistId: BufferNutritionistId);

        // Act
        var primaryCountAfter = await _sut.GetWeekCountAsync(NutritionistId);
        var bufferCount = await _sut.GetWeekCountAsync(BufferNutritionistId);

        // Assert — adding a buffer-nutritionist appointment must not change the primary count,
        // and the buffer nutritionist must see at least the appointment we just seeded.
        primaryCountAfter.Should().Be(primaryCountBefore,
            because: "the seeded appointment belongs to a different nutritionist");
        bufferCount.Should().BeGreaterThanOrEqualTo(1,
            because: "we seeded one this-week appointment for the buffer nutritionist");
    }

    // ---------------------------------------------------------------------------
    // CreateRecurringAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateRecurringAsync_ValidRequest_CreatesCorrectNumberOfAppointments()
    {
        // Arrange
        var baseTime = new DateTime(2028, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var baseDto = BuildCreateDto(_clientId, baseTime);
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 7, Count: 4);

        // Act
        var result = await _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        result.CreatedCount.Should().Be(4);
        result.SkippedCount.Should().Be(0);
        result.CreatedIds.Should().HaveCount(4);
    }

    [Fact]
    public async Task CreateRecurringAsync_ValidRequest_RespectsIntervalDays()
    {
        // Arrange — 3 appointments, 14 days apart
        var baseTime = new DateTime(2028, 4, 1, 10, 0, 0, DateTimeKind.Utc);
        var baseDto = BuildCreateDto(_clientId, baseTime);
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 14, Count: 3);

        // Act
        var result = await _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        result.CreatedCount.Should().Be(3);
        var appointments = await _dbContext.Appointments
            .Where(a => result.CreatedIds.Contains(a.Id))
            .OrderBy(a => a.StartTime)
            .ToListAsync();

        appointments[0].StartTime.Should().Be(baseTime);
        appointments[1].StartTime.Should().Be(baseTime.AddDays(14));
        appointments[2].StartTime.Should().Be(baseTime.AddDays(28));
    }

    [Fact]
    public async Task CreateRecurringAsync_ConflictOnOneOccurrence_SkipsItAndContinues()
    {
        // Arrange — pre-book the second slot so it conflicts; the rest should be created
        var baseTime = new DateTime(2028, 5, 1, 10, 0, 0, DateTimeKind.Utc);
        SeedAppointment(baseTime.AddDays(7)); // blocks the second occurrence

        var baseDto = BuildCreateDto(_clientId, baseTime);
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 7, Count: 3);

        // Act
        var result = await _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        result.CreatedCount.Should().Be(2);
        result.SkippedCount.Should().Be(1);
        result.SkippedReasons.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateRecurringAsync_CountBelowMinimum_ThrowsArgumentException()
    {
        // Arrange — count of 1 is invalid (minimum is 2)
        var baseDto = BuildCreateDto(_clientId, new DateTime(2028, 6, 1, 10, 0, 0, DateTimeKind.Utc));
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 7, Count: 1);

        // Act
        var act = () => _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*count*");
    }

    [Fact]
    public async Task CreateRecurringAsync_CountAboveMaximum_ThrowsArgumentException()
    {
        // Arrange — count of 53 exceeds the 52-occurrence ceiling
        var baseDto = BuildCreateDto(_clientId, new DateTime(2028, 7, 1, 10, 0, 0, DateTimeKind.Utc));
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 7, Count: 53);

        // Act
        var act = () => _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*count*");
    }

    [Fact]
    public async Task CreateRecurringAsync_IntervalDaysZero_ThrowsArgumentException()
    {
        // Arrange — interval of 0 is invalid (minimum is 1)
        var baseDto = BuildCreateDto(_clientId, new DateTime(2028, 8, 1, 10, 0, 0, DateTimeKind.Utc));
        var dto = new CreateRecurringAppointmentDto(Base: baseDto, IntervalDays: 0, Count: 4);

        // Act
        var act = () => _sut.CreateRecurringAsync(dto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*Interval*");
    }

    // ---------------------------------------------------------------------------
    // Buffer time — overlap check respects BufferTimeMinutes
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithinBufferZone_ThrowsSchedulingConflictException()
    {
        // Arrange — buffer nutritionist has BufferTimeMinutes = 15.
        // Seed an appointment 10:00-11:00; then attempt to book at 11:05 (inside the 15-min buffer).
        var baseDate = new DateTime(2029, 1, 15, 10, 0, 0, DateTimeKind.Utc);
        SeedAppointment(baseDate, nutritionistId: BufferNutritionistId);

        var conflictingDto = BuildCreateDto(
            _clientId,
            baseDate.AddMinutes(65)); // 11:05 — within the 15-min post-appointment buffer

        // Act
        var act = () => _sut.CreateAsync(conflictingDto, BufferNutritionistId);

        // Assert
        await act.Should().ThrowAsync<SchedulingConflictException>();
    }

    [Fact]
    public async Task CreateAsync_OutsideBufferZone_Succeeds()
    {
        // Arrange — buffer nutritionist has BufferTimeMinutes = 15.
        // Seed an appointment 10:00-11:00; then attempt to book at 11:16 (safely outside the buffer).
        var baseDate = new DateTime(2029, 1, 16, 10, 0, 0, DateTimeKind.Utc);
        SeedAppointment(baseDate, nutritionistId: BufferNutritionistId);

        var safeDto = BuildCreateDto(
            _clientId,
            baseDate.AddMinutes(76)); // 11:16 — just beyond the 15-min buffer

        // Act
        var act = () => _sut.CreateAsync(safeDto, BufferNutritionistId);

        // Assert
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
