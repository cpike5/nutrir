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

    private const string UserId = "user-session-note-test-001";
    private int _seededClientId;
    private int _seededAppointmentId;

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

    private void SeedData()
    {
        var user = new ApplicationUser
        {
            Id = UserId,
            UserName = "test@sessionnote.com",
            NormalizedUserName = "TEST@SESSIONNOTE.COM",
            Email = "test@sessionnote.com",
            NormalizedEmail = "TEST@SESSIONNOTE.COM",
            FirstName = "Test",
            LastName = "User",
            DisplayName = "Test User",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Jane",
            LastName = "Doe",
            Email = "jane@example.com",
            DateOfBirth = new DateOnly(1990, 1, 1),
            PrimaryNutritionistId = UserId,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;

        var appointment = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = UserId,
            StartTime = DateTime.UtcNow.AddDays(-1),
            DurationMinutes = 60,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Completed,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();

        _seededAppointmentId = appointment.Id;
    }

    // ---------------------------------------------------------------------------
    // Create + Update with new fields
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_MapsNewFields_Correctly()
    {
        // Arrange — create a draft
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        var updateDto = new UpdateSessionNoteDto(
            SessionType: SessionType.FollowUp,
            Notes: "General notes",
            AdherenceScore: 85,
            PractitionerAssessment: "Client is progressing well",
            ContextualFactors: "Recent travel, mild stress",
            MeasurementsTaken: "Weight: 70kg",
            PlanAdjustments: "Increased protein target",
            FollowUpActions: "Schedule lab work");

        // Act
        var result = await _sut.UpdateAsync(draft.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();

        var updated = await _sut.GetByIdAsync(draft.Id);
        updated.Should().NotBeNull();
        updated!.SessionType.Should().Be(SessionType.FollowUp);
        updated.Notes.Should().Be("General notes");
        updated.AdherenceScore.Should().Be(85);
        updated.PractitionerAssessment.Should().Be("Client is progressing well");
        updated.ContextualFactors.Should().Be("Recent travel, mild stress");
        updated.MeasurementsTaken.Should().Be("Weight: 70kg");
        updated.PlanAdjustments.Should().Be("Increased protein target");
        updated.FollowUpActions.Should().Be("Schedule lab work");
    }

    [Fact]
    public async Task UpdateAsync_NullNewFields_BackwardCompatible()
    {
        // Arrange — create a draft, update without new fields
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        var updateDto = new UpdateSessionNoteDto(
            SessionType: null,
            Notes: "Just notes",
            AdherenceScore: 50,
            PractitionerAssessment: null,
            ContextualFactors: null,
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: null);

        // Act
        var result = await _sut.UpdateAsync(draft.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();

        var updated = await _sut.GetByIdAsync(draft.Id);
        updated.Should().NotBeNull();
        updated!.SessionType.Should().BeNull();
        updated.PractitionerAssessment.Should().BeNull();
        updated.ContextualFactors.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNewFields_InDto()
    {
        // Arrange — create and populate
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        var updateDto = new UpdateSessionNoteDto(
            SessionType: SessionType.InitialConsultation,
            Notes: "Initial session",
            AdherenceScore: null,
            PractitionerAssessment: "Thorough assessment needed",
            ContextualFactors: "New client, anxiety about diet changes",
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: "Review food diary");

        await _sut.UpdateAsync(draft.Id, updateDto, UserId);

        // Act
        var result = await _sut.GetByIdAsync(draft.Id);

        // Assert
        result.Should().NotBeNull();
        result!.SessionType.Should().Be(SessionType.InitialConsultation);
        result.PractitionerAssessment.Should().Be("Thorough assessment needed");
        result.ContextualFactors.Should().Be("New client, anxiety about diet changes");
    }

    [Fact]
    public async Task GetByClientAsync_IncludesSessionType_InSummary()
    {
        // Arrange
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        var updateDto = new UpdateSessionNoteDto(
            SessionType: SessionType.CheckIn,
            Notes: "Quick check-in",
            AdherenceScore: 90,
            PractitionerAssessment: null,
            ContextualFactors: null,
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: null);

        await _sut.UpdateAsync(draft.Id, updateDto, UserId);

        // Act
        var summaries = await _sut.GetByClientAsync(_seededClientId);

        // Assert
        summaries.Should().HaveCount(1);
        summaries[0].SessionType.Should().Be(SessionType.CheckIn);
    }

    [Fact]
    public async Task CreateDraftAsync_StartsWithNullNewFields()
    {
        // Act
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        // Assert
        draft.SessionType.Should().BeNull();
        draft.PractitionerAssessment.Should().BeNull();
        draft.ContextualFactors.Should().BeNull();
    }

    [Fact]
    public async Task FinalizeAsync_PreservesNewFields()
    {
        // Arrange
        var draft = await _sut.CreateDraftAsync(_seededAppointmentId, _seededClientId, UserId);

        var updateDto = new UpdateSessionNoteDto(
            SessionType: SessionType.GroupSession,
            Notes: "Group session notes",
            AdherenceScore: 75,
            PractitionerAssessment: "Group dynamics were positive",
            ContextualFactors: "Holiday season",
            MeasurementsTaken: null,
            PlanAdjustments: null,
            FollowUpActions: "Follow up individually");

        await _sut.UpdateAsync(draft.Id, updateDto, UserId);

        // Act
        var finalized = await _sut.FinalizeAsync(draft.Id, UserId);

        // Assert
        finalized.Should().BeTrue();

        var result = await _sut.GetByIdAsync(draft.Id);
        result.Should().NotBeNull();
        result!.IsDraft.Should().BeFalse();
        result.SessionType.Should().Be(SessionType.GroupSession);
        result.PractitionerAssessment.Should().Be("Group dynamics were positive");
        result.ContextualFactors.Should().Be("Holiday season");
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
