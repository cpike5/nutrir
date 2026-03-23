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
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class SessionNoteServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;

    private readonly SessionNoteService _sut;

    private const string NutritionistId = "nutritionist-sessionnote-test-001";
    private const string UserId = "user-sessionnote-test-001";

    private int _seededClientId;
    private int _seededCompletedAppointmentId;
    private int _seededScheduledAppointmentId;
    private readonly DateTime _seededAppointmentTime = new(2026, 3, 15, 10, 0, 0, DateTimeKind.Utc);

    public SessionNoteServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _auditLogService = Substitute.For<IAuditLogService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();

        _sut = new SessionNoteService(
            _dbContext,
            _dbContextFactory,
            _auditLogService,
            _notificationDispatcher,
            NullLogger<SessionNoteService>.Instance);

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
            UserName = "nutritionist@sessionnotetest.com",
            NormalizedUserName = "NUTRITIONIST@SESSIONNOTETEST.COM",
            Email = "nutritionist@sessionnotetest.com",
            NormalizedEmail = "NUTRITIONIST@SESSIONNOTETEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        // Acting user (the logged-in practitioner who performs write operations).
        // Must exist as a valid ApplicationUser because SessionNote.CreatedByUserId
        // carries a FK constraint to the Users table.
        var actingUser = new ApplicationUser
        {
            Id = UserId,
            UserName = "actinguser@sessionnotetest.com",
            NormalizedUserName = "ACTINGUSER@SESSIONNOTETEST.COM",
            Email = "actinguser@sessionnotetest.com",
            NormalizedEmail = "ACTINGUSER@SESSIONNOTETEST.COM",
            FirstName = "Acting",
            LastName = "User",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "TestClient",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        var completedAppointment = new Appointment
        {
            ClientId = 0, // will be set after client is saved
            NutritionistId = NutritionistId,
            Status = AppointmentStatus.Completed,
            StartTime = _seededAppointmentTime,
            DurationMinutes = 60,
            Type = AppointmentType.InitialConsultation,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };

        var scheduledAppointment = new Appointment
        {
            ClientId = 0, // will be set after client is saved
            NutritionistId = NutritionistId,
            Status = AppointmentStatus.Scheduled,
            StartTime = _seededAppointmentTime.AddDays(7),
            DurationMinutes = 45,
            Type = AppointmentType.FollowUp,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Users.Add(actingUser);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
        completedAppointment.ClientId = _seededClientId;
        scheduledAppointment.ClientId = _seededClientId;

        _dbContext.Appointments.Add(completedAppointment);
        _dbContext.Appointments.Add(scheduledAppointment);
        _dbContext.SaveChanges();

        _seededCompletedAppointmentId = completedAppointment.Id;
        _seededScheduledAppointmentId = scheduledAppointment.Id;
    }

    private async Task<SessionNoteDto> CreateDraftForCompletedAppointmentAsync()
        => await _sut.CreateDraftAsync(_seededCompletedAppointmentId, _seededClientId, UserId);

    // ---------------------------------------------------------------------------
    // CreateDraftAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateDraftAsync_CreatesNewDraft_WithAuditLog()
    {
        // Act
        var result = await _sut.CreateDraftAsync(_seededCompletedAppointmentId, _seededClientId, UserId);

        // Assert
        result.Should().NotBeNull();
        result.AppointmentId.Should().Be(_seededCompletedAppointmentId);
        result.ClientId.Should().Be(_seededClientId);
        result.IsDraft.Should().BeTrue();
        result.Id.Should().BeGreaterThan(0);

        await _auditLogService.Received(1).LogAsync(
            UserId,
            "SessionNoteCreated",
            "SessionNote",
            Arg.Any<string>(),
            Arg.Any<string>());

        await _notificationDispatcher.Received(1).DispatchAsync(
            Arg.Is<EntityChangeNotification>(n =>
                n.EntityType == "SessionNote" &&
                n.ChangeType == EntityChangeType.Created));
    }

    [Fact]
    public async Task CreateDraftAsync_ReturnsExisting_WhenAlreadyExists()
    {
        // Arrange — create once
        var first = await _sut.CreateDraftAsync(_seededCompletedAppointmentId, _seededClientId, UserId);

        // Reset the substitute call counts so second call is isolated
        _auditLogService.ClearReceivedCalls();
        _notificationDispatcher.ClearReceivedCalls();

        // Act — create again for same appointment
        var second = await _sut.CreateDraftAsync(_seededCompletedAppointmentId, _seededClientId, UserId);

        // Assert — same note returned, no duplicate audit entry
        second.Id.Should().Be(first.Id);
        second.AppointmentId.Should().Be(first.AppointmentId);

        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());

        await _notificationDispatcher.DidNotReceive().DispatchAsync(Arg.Any<EntityChangeNotification>());
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsNote_WhenExists()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();

        // Act
        var result = await _sut.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.AppointmentId.Should().Be(_seededCompletedAppointmentId);
        result.ClientId.Should().Be(_seededClientId);
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("TestClient");
        result.CreatedByUserId.Should().Be(UserId);
        // CreatedByName is resolved from the ApplicationUser whose Id matches CreatedByUserId.
        // That user is the acting user seeded with FirstName="Acting", LastName="User".
        result.CreatedByName.Should().Be("Acting User");
        result.AppointmentDate.Should().Be(_seededAppointmentTime);
        result.IsDraft.Should().BeTrue();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _sut.GetByIdAsync(99999);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetByAppointmentIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByAppointmentIdAsync_ReturnsNote_WhenExists()
    {
        // Arrange
        await CreateDraftForCompletedAppointmentAsync();

        // Act
        var result = await _sut.GetByAppointmentIdAsync(_seededCompletedAppointmentId);

        // Assert
        result.Should().NotBeNull();
        result!.AppointmentId.Should().Be(_seededCompletedAppointmentId);
        result.ClientId.Should().Be(_seededClientId);
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("TestClient");
    }

    [Fact]
    public async Task GetByAppointmentIdAsync_ReturnsNull_WhenNotFound()
    {
        // Act
        var result = await _sut.GetByAppointmentIdAsync(99999);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetByClientAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByClientAsync_ReturnsNotesOrderedByCreatedAtDesc()
    {
        // Arrange — create a note for the completed appointment, then add a second
        // note directly via DbContext for the scheduled appointment so we can
        // control the CreatedAt timestamps precisely.
        var firstNote = new SessionNote
        {
            AppointmentId = _seededCompletedAppointmentId,
            ClientId = _seededClientId,
            CreatedByUserId = UserId,
            IsDraft = false,
            CreatedAt = new DateTime(2026, 3, 15, 9, 0, 0, DateTimeKind.Utc)
        };

        var secondNote = new SessionNote
        {
            AppointmentId = _seededScheduledAppointmentId,
            ClientId = _seededClientId,
            CreatedByUserId = UserId,
            IsDraft = true,
            CreatedAt = new DateTime(2026, 3, 22, 9, 0, 0, DateTimeKind.Utc)
        };

        _dbContext.SessionNotes.AddRange(firstNote, secondNote);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetByClientAsync(_seededClientId);

        // Assert — most recent first
        results.Should().HaveCount(2);
        results[0].CreatedAt.Should().BeAfter(results[1].CreatedAt);
        results[0].AppointmentId.Should().Be(_seededScheduledAppointmentId);
        results[1].AppointmentId.Should().Be(_seededCompletedAppointmentId);
    }

    [Fact]
    public async Task GetByClientAsync_ReturnsEmptyList_WhenNoNotes()
    {
        // Act
        var results = await _sut.GetByClientAsync(_seededClientId);

        // Assert
        results.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // UpdateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UpdatesAllFields()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();

        var dto = new UpdateSessionNoteDto(
            Notes: "Patient showed good progress.",
            AdherenceScore: 85,
            MeasurementsTaken: "Weight: 72 kg",
            PlanAdjustments: "Increased protein target.",
            FollowUpActions: "Schedule in 4 weeks.");

        var beforeUpdate = DateTime.UtcNow;

        // Act
        var success = await _sut.UpdateAsync(created.Id, dto, UserId);

        // Assert — return value
        success.Should().BeTrue();

        // Assert — persisted values
        var updated = await _sut.GetByIdAsync(created.Id);
        updated.Should().NotBeNull();
        updated!.Notes.Should().Be("Patient showed good progress.");
        updated.AdherenceScore.Should().Be(85);
        updated.MeasurementsTaken.Should().Be("Weight: 72 kg");
        updated.PlanAdjustments.Should().Be("Increased protein target.");
        updated.FollowUpActions.Should().Be("Schedule in 4 weeks.");
        updated.UpdatedAt.Should().NotBeNull();
        updated.UpdatedAt!.Value.Should().BeOnOrAfter(beforeUpdate);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        // Arrange
        var dto = new UpdateSessionNoteDto(
            Notes: "Should not persist.",
            AdherenceScore: 50,
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: null);

        // Act
        var success = await _sut.UpdateAsync(99999, dto, UserId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_LogsAuditEntry()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        _auditLogService.ClearReceivedCalls();

        var dto = new UpdateSessionNoteDto(
            Notes: "Some notes.",
            AdherenceScore: 70,
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: null);

        // Act
        await _sut.UpdateAsync(created.Id, dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "SessionNoteUpdated",
            "SessionNote",
            created.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNoteIsFinalized()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        await _sut.FinalizeAsync(created.Id, UserId);

        var dto = new UpdateSessionNoteDto(
            Notes: "Should not persist.",
            AdherenceScore: 50,
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: null);

        // Act
        var success = await _sut.UpdateAsync(created.Id, dto, UserId);

        // Assert
        success.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // FinalizeAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FinalizeAsync_SetsIsDraftFalseAndUpdatedAt()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        created.IsDraft.Should().BeTrue();

        var beforeFinalize = DateTime.UtcNow;

        // Act
        var success = await _sut.FinalizeAsync(created.Id, UserId);

        // Assert
        success.Should().BeTrue();

        var finalized = await _sut.GetByIdAsync(created.Id);
        finalized.Should().NotBeNull();
        finalized!.IsDraft.Should().BeFalse();
        finalized.UpdatedAt.Should().NotBeNull();
        finalized.UpdatedAt!.Value.Should().BeOnOrAfter(beforeFinalize);
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var success = await _sut.FinalizeAsync(99999, UserId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeAsync_LogsAuditEntry()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        _auditLogService.ClearReceivedCalls();

        // Act
        await _sut.FinalizeAsync(created.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "SessionNoteFinalized",
            "SessionNote",
            created.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task FinalizeAsync_ReturnsFalse_WhenAlreadyFinalized()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        await _sut.FinalizeAsync(created.Id, UserId);

        // Act
        var secondResult = await _sut.FinalizeAsync(created.Id, UserId);

        // Assert
        secondResult.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // SoftDeleteAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteAsync_SetsDeleteFields_AndAuditLog()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();
        _auditLogService.ClearReceivedCalls();
        _notificationDispatcher.ClearReceivedCalls();

        var beforeDelete = DateTime.UtcNow;

        // Act
        var success = await _sut.SoftDeleteAsync(created.Id, UserId);

        // Assert — return value
        success.Should().BeTrue();

        // Assert — entity state via direct DbContext read (bypassing query filter)
        var entity = await _dbContext.SessionNotes
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(sn => sn.Id == created.Id);

        entity.Should().NotBeNull();
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.DeletedAt!.Value.Should().BeOnOrAfter(beforeDelete);
        entity.DeletedBy.Should().Be(UserId);

        await _auditLogService.Received(1).LogAsync(
            UserId,
            "SessionNoteSoftDeleted",
            "SessionNote",
            created.Id.ToString(),
            Arg.Any<string>());

        await _notificationDispatcher.Received(1).DispatchAsync(
            Arg.Is<EntityChangeNotification>(n =>
                n.EntityType == "SessionNote" &&
                n.ChangeType == EntityChangeType.Deleted));
    }

    [Fact]
    public async Task SoftDeleteAsync_ReturnsFalse_WhenNotFound()
    {
        // Act
        var success = await _sut.SoftDeleteAsync(99999, UserId);

        // Assert
        success.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_FilteredFromGetById()
    {
        // Arrange
        var created = await CreateDraftForCompletedAppointmentAsync();

        // Act
        await _sut.SoftDeleteAsync(created.Id, UserId);

        // Assert — query filter hides the soft-deleted note
        var result = await _sut.GetByIdAsync(created.Id);
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetMissingNotesAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetMissingNotesAsync_ReturnsCompletedAppointmentsWithoutNotes()
    {
        // Arrange — no notes exist yet; seeded completed appointment has no note

        // Act
        var results = await _sut.GetMissingNotesAsync();

        // Assert
        results.Should().NotBeEmpty();
        results.Should().Contain(sn =>
            sn.AppointmentId == _seededCompletedAppointmentId &&
            sn.ClientId == _seededClientId &&
            sn.IsDraft == true);
    }

    [Fact]
    public async Task GetMissingNotesAsync_ExcludesAppointmentsWithNotes()
    {
        // Arrange — create a note for the completed appointment
        await CreateDraftForCompletedAppointmentAsync();

        // Act
        var results = await _sut.GetMissingNotesAsync();

        // Assert — completed appointment now has a note, so it must not appear
        results.Should().NotContain(sn => sn.AppointmentId == _seededCompletedAppointmentId);
    }

    [Fact]
    public async Task GetMissingNotesAsync_ExcludesNonCompletedAppointments()
    {
        // Arrange — seeded scheduled appointment has no note; it must still be excluded
        // because only Completed appointments without notes should appear.

        // Act
        var results = await _sut.GetMissingNotesAsync();

        // Assert
        results.Should().NotContain(sn => sn.AppointmentId == _seededScheduledAppointmentId);
    }

    // ---------------------------------------------------------------------------
    // IDisposable
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
