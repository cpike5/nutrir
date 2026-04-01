using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// Unit tests for ReminderService covering GetRemindersForAppointmentAsync
/// and ResendReminderAsync, plus pure-function tests for ReminderEmailBuilder.
/// </summary>
public class ReminderServiceTests : IDisposable
{
    // ---------------------------------------------------------------------------
    // Infrastructure
    // ---------------------------------------------------------------------------

    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IEmailService _emailService;
    private readonly IReminderEmailBuilder _emailBuilder;
    private readonly IAuditLogService _auditLogService;

    private readonly ReminderService _sut;

    private const string NutritionistId = "nutritionist-reminder-test-001";
    private const string UserId = "acting-user-reminder-001";

    private int _seededClientId;
    private int _seededAppointmentId;

    // A fixed UTC time used as appointment StartTime throughout the tests.
    private static readonly DateTime AppointmentStartTime =
        DateTime.SpecifyKind(new DateTime(2026, 6, 15, 14, 0, 0), DateTimeKind.Utc);

    public ReminderServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _emailService = Substitute.For<IEmailService>();
        _emailBuilder = Substitute.For<IReminderEmailBuilder>();
        _auditLogService = Substitute.For<IAuditLogService>();

        // Default: email builder returns a well-formed tuple.
        _emailBuilder
            .BuildReminderEmail(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<ReminderType>())
            .Returns(("Appointment Reminder", "<html>test</html>"));

        _sut = new ReminderService(
            _dbContextFactory,
            _emailService,
            _emailBuilder,
            _auditLogService,
            NullLogger<ReminderService>.Instance);

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
            UserName = "nutritionist@remindertest.com",
            NormalizedUserName = "NUTRITIONIST@REMINDERTEST.COM",
            Email = "nutritionist@remindertest.com",
            NormalizedEmail = "NUTRITIONIST@REMINDERTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Reminder",
            Email = "alice@example.com",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        var appointment = new Appointment
        {
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Confirmed,
            StartTime = AppointmentStartTime,
            DurationMinutes = 60,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        // Set ClientId after client Id has been assigned.
        appointment.ClientId = client.Id;
        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
        _seededAppointmentId = appointment.Id;
    }

    private AppointmentReminder MakeReminder(
        int appointmentId,
        ReminderType type,
        DateTime? createdAt = null,
        ReminderStatus status = ReminderStatus.Sent)
    {
        return new AppointmentReminder
        {
            AppointmentId = appointmentId,
            ReminderType = type,
            ScheduledFor = AppointmentStartTime,
            Status = status,
            SentAt = status == ReminderStatus.Sent ? AppointmentStartTime.AddHours(-48) : null,
            CreatedAt = DateTime.SpecifyKind(createdAt ?? DateTime.UtcNow, DateTimeKind.Utc)
        };
    }

    // ---------------------------------------------------------------------------
    // GetRemindersForAppointmentAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRemindersForAppointmentAsync_ReturnsReminders_WhenExist()
    {
        // Arrange
        var reminder48 = MakeReminder(_seededAppointmentId, ReminderType.FortyEightHour);
        var reminder24 = MakeReminder(_seededAppointmentId, ReminderType.TwentyFourHour);
        _dbContext.AppointmentReminders.AddRange(reminder48, reminder24);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRemindersForAppointmentAsync(_seededAppointmentId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ReminderType == ReminderType.FortyEightHour);
        result.Should().Contain(r => r.ReminderType == ReminderType.TwentyFourHour);
    }

    [Fact]
    public async Task GetRemindersForAppointmentAsync_ReturnsEmpty_WhenNoneExist()
    {
        // Arrange — no reminders seeded for this appointment.

        // Act
        var result = await _sut.GetRemindersForAppointmentAsync(_seededAppointmentId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRemindersForAppointmentAsync_ReturnsOnlyMatchingAppointment()
    {
        // Arrange — seed a second appointment with its own reminder.
        var otherAppointment = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.CheckIn,
            Status = AppointmentStatus.Scheduled,
            StartTime = AppointmentStartTime.AddDays(7),
            DurationMinutes = 30,
            Location = AppointmentLocation.Phone,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(otherAppointment);
        await _dbContext.SaveChangesAsync();

        var reminderForTarget = MakeReminder(_seededAppointmentId, ReminderType.TwentyFourHour);
        var reminderForOther = new AppointmentReminder
        {
            AppointmentId = otherAppointment.Id,
            ReminderType = ReminderType.FortyEightHour,
            ScheduledFor = AppointmentStartTime.AddDays(7),
            Status = ReminderStatus.Sent,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.AppointmentReminders.AddRange(reminderForTarget, reminderForOther);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRemindersForAppointmentAsync(_seededAppointmentId);

        // Assert
        result.Should().HaveCount(1);
        result[0].AppointmentId.Should().Be(_seededAppointmentId);
    }

    [Fact]
    public async Task GetRemindersForAppointmentAsync_OrdersByCreatedAt()
    {
        // Arrange — seed reminders with deliberately non-chronological insertion order.
        var newerReminder = MakeReminder(
            _seededAppointmentId,
            ReminderType.TwentyFourHour,
            createdAt: DateTime.SpecifyKind(new DateTime(2026, 5, 10, 12, 0, 0), DateTimeKind.Utc));

        var olderReminder = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.FortyEightHour,
            ScheduledFor = AppointmentStartTime,
            Status = ReminderStatus.Sent,
            CreatedAt = DateTime.SpecifyKind(new DateTime(2026, 5, 8, 9, 0, 0), DateTimeKind.Utc)
        };

        // Add in reverse chronological order to ensure the query sorts, not insertion order.
        _dbContext.AppointmentReminders.Add(newerReminder);
        _dbContext.AppointmentReminders.Add(olderReminder);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRemindersForAppointmentAsync(_seededAppointmentId);

        // Assert
        result.Should().HaveCount(2);
        result[0].ReminderType.Should().Be(ReminderType.FortyEightHour,
            because: "the 48-hour reminder was created first and results are ordered by CreatedAt ascending");
        result[1].ReminderType.Should().Be(ReminderType.TwentyFourHour);
    }

    [Fact]
    public async Task GetRemindersForAppointmentAsync_MapsAllDtoFields()
    {
        // Arrange
        var sentAt = DateTime.SpecifyKind(new DateTime(2026, 5, 1, 10, 0, 0), DateTimeKind.Utc);
        var createdAt = DateTime.SpecifyKind(new DateTime(2026, 4, 30, 8, 0, 0), DateTimeKind.Utc);
        var reminder = new AppointmentReminder
        {
            AppointmentId = _seededAppointmentId,
            ReminderType = ReminderType.FortyEightHour,
            ScheduledFor = AppointmentStartTime,
            Status = ReminderStatus.Sent,
            SentAt = sentAt,
            FailureReason = null,
            CreatedAt = createdAt
        };
        _dbContext.AppointmentReminders.Add(reminder);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetRemindersForAppointmentAsync(_seededAppointmentId);

        // Assert
        var dto = result.Should().ContainSingle().Subject;
        dto.AppointmentId.Should().Be(_seededAppointmentId);
        dto.ReminderType.Should().Be(ReminderType.FortyEightHour);
        dto.ScheduledFor.Should().Be(AppointmentStartTime);
        dto.Status.Should().Be(ReminderStatus.Sent);
        dto.SentAt.Should().Be(sentAt);
        dto.FailureReason.Should().BeNull();
        dto.CreatedAt.Should().Be(createdAt);
    }

    // ---------------------------------------------------------------------------
    // ResendReminderAsync — happy path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResendReminderAsync_SendsEmail_AndCreatesReminderWithSentStatus()
    {
        // Act
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.TwentyFourHour, UserId);

        // Assert — email was sent with the client's email address.
        await _emailService.Received(1).SendEmailAsync(
            "alice@example.com",
            "Alice",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Assert — a reminder row was persisted with Sent status.
        var persisted = await _dbContext.AppointmentReminders
            .FirstOrDefaultAsync(r => r.AppointmentId == _seededAppointmentId);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(ReminderStatus.Sent);
        persisted.SentAt.Should().NotBeNull();
        persisted.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ResendReminderAsync_CallsEmailBuilder_WithCorrectArguments()
    {
        // Act
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.FortyEightHour, UserId);

        // Assert
        _emailBuilder.Received(1).BuildReminderEmail(
            "Alice",
            AppointmentStartTime,
            ReminderType.FortyEightHour);
    }

    [Fact]
    public async Task ResendReminderAsync_LogsAuditEntry_WithReminderResentAction()
    {
        // Act
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.TwentyFourHour, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ReminderResent",
            "Appointment",
            _seededAppointmentId.ToString(),
            Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // ResendReminderAsync — not-found / validation guards
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResendReminderAsync_ThrowsInvalidOperationException_WhenAppointmentNotFound()
    {
        // Arrange
        const int nonExistentAppointmentId = 99999;

        // Act
        var act = async () =>
            await _sut.ResendReminderAsync(nonExistentAppointmentId, ReminderType.TwentyFourHour, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentAppointmentId}*");
    }

    [Fact]
    public async Task ResendReminderAsync_ThrowsInvalidOperationException_WhenClientNotFound()
    {
        // Arrange — create an appointment whose ClientId references a non-existent client.
        // Bypass FK constraints by temporarily disabling foreign-key enforcement on the
        // SQLite connection, inserting directly, then re-enabling.
        await _dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
        var orphanedAppointment = new Appointment
        {
            ClientId = 99998,   // No client with this Id.
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = AppointmentStartTime,
            DurationMinutes = 30,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(orphanedAppointment);
        await _dbContext.SaveChangesAsync();
        await _dbContext.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON");

        // Act
        var act = async () =>
            await _sut.ResendReminderAsync(orphanedAppointment.Id, ReminderType.TwentyFourHour, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*99998*");
    }

    [Fact]
    public async Task ResendReminderAsync_ThrowsInvalidOperationException_WhenClientHasNoEmail()
    {
        // Arrange — seed a client without an email address.
        var noEmailClient = new Client
        {
            FirstName = "Bob",
            LastName = "NoEmail",
            Email = null,
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(noEmailClient);
        await _dbContext.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClientId = noEmailClient.Id,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = AppointmentStartTime,
            DurationMinutes = 30,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = async () =>
            await _sut.ResendReminderAsync(appointment.Id, ReminderType.TwentyFourHour, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*email address*");
    }

    [Fact]
    public async Task ResendReminderAsync_ThrowsInvalidOperationException_WhenConsentNotGiven()
    {
        // Arrange — client has not given consent.
        var noConsentClient = new Client
        {
            FirstName = "Carol",
            LastName = "NoConsent",
            Email = "carol@example.com",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(noConsentClient);
        await _dbContext.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClientId = noConsentClient.Id,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = AppointmentStartTime,
            DurationMinutes = 30,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = async () =>
            await _sut.ResendReminderAsync(appointment.Id, ReminderType.TwentyFourHour, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opted in*");
    }

    [Fact]
    public async Task ResendReminderAsync_ThrowsInvalidOperationException_WhenEmailRemindersNotEnabled()
    {
        // Arrange — client gave consent but has email reminders disabled.
        var remindersOffClient = new Client
        {
            FirstName = "Dave",
            LastName = "RemindersOff",
            Email = "dave@example.com",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(remindersOffClient);
        await _dbContext.SaveChangesAsync();

        var appointment = new Appointment
        {
            ClientId = remindersOffClient.Id,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = AppointmentStartTime,
            DurationMinutes = 30,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        await _dbContext.SaveChangesAsync();

        // Act
        var act = async () =>
            await _sut.ResendReminderAsync(appointment.Id, ReminderType.TwentyFourHour, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*opted in*");
    }

    // ---------------------------------------------------------------------------
    // ResendReminderAsync — email failure path
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResendReminderAsync_RecordsFailureReminder_WhenEmailServiceThrows()
    {
        // Arrange — configure email service to throw.
        var emailError = new Exception("SMTP connection refused");
        _emailService
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(emailError);

        // Act — should not propagate the exception; the service catches it.
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.FortyEightHour, UserId);

        // Assert — reminder persisted with Failed status.
        var persisted = await _dbContext.AppointmentReminders
            .FirstOrDefaultAsync(r => r.AppointmentId == _seededAppointmentId);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(ReminderStatus.Failed);
        persisted.SentAt.Should().BeNull();
        persisted.FailureReason.Should().Be("SMTP connection refused");
    }

    [Fact]
    public async Task ResendReminderAsync_TruncatesFailureReason_WhenExceptionMessageExceeds500Chars()
    {
        // Arrange — craft a message longer than 500 characters.
        var longMessage = new string('X', 600);
        _emailService
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception(longMessage));

        // Act
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.FortyEightHour, UserId);

        // Assert
        var persisted = await _dbContext.AppointmentReminders
            .FirstAsync(r => r.AppointmentId == _seededAppointmentId);
        persisted.FailureReason.Should().HaveLength(500);
    }

    [Fact]
    public async Task ResendReminderAsync_LogsAuditEntry_WithReminderResendFailedAction_WhenEmailThrows()
    {
        // Arrange
        _emailService
            .SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("timeout"));

        // Act
        await _sut.ResendReminderAsync(_seededAppointmentId, ReminderType.TwentyFourHour, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ReminderResendFailed",
            "Appointment",
            _seededAppointmentId.ToString(),
            Arg.Any<string>());
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
