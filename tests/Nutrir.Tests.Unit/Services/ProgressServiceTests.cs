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

public class ProgressServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAuditLogService _auditLogService;
    private readonly IRetentionTracker _retentionTracker;
    private readonly INotificationDispatcher _notificationDispatcher;

    private readonly ProgressService _sut;

    private const string NutritionistId = "nutritionist-progress-test-001";
    private const string UserId = "acting-user-progress-001";

    // The seeded client Id is captured after SaveChanges so tests don't hard-code a magic number.
    private int _seededClientId;

    public ProgressServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _auditLogService = Substitute.For<IAuditLogService>();
        _retentionTracker = Substitute.For<IRetentionTracker>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();

        _sut = new ProgressService(
            _dbContext,
            _dbContextFactory,
            _auditLogService,
            _retentionTracker,
            _notificationDispatcher,
            NullLogger<ProgressService>.Instance);

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
            UserName = "nutritionist@progresstest.com",
            NormalizedUserName = "NUTRITIONIST@PROGRESSTEST.COM",
            Email = "nutritionist@progresstest.com",
            NormalizedEmail = "NUTRITIONIST@PROGRESSTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        // Seed the acting user so CreatedByUserId FK constraints are satisfied
        var actingUser = new ApplicationUser
        {
            Id = UserId,
            UserName = "actinguser@progresstest.com",
            NormalizedUserName = "ACTINGUSER@PROGRESSTEST.COM",
            Email = "actinguser@progresstest.com",
            NormalizedEmail = "ACTINGUSER@PROGRESSTEST.COM",
            FirstName = "Acting",
            LastName = "User",
            DisplayName = "Acting User",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Progress",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Users.Add(actingUser);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    // ---------------------------------------------------------------------------
    // Helper factories
    // ---------------------------------------------------------------------------

    private CreateProgressEntryDto MakeCreateEntryDto(
        DateOnly? entryDate = null,
        string? notes = null,
        List<CreateProgressMeasurementDto>? measurements = null)
    {
        return new CreateProgressEntryDto(
            _seededClientId,
            entryDate ?? DateOnly.FromDateTime(DateTime.UtcNow),
            notes,
            measurements ?? [new CreateProgressMeasurementDto(MetricType.Weight, null, 80m, "kg")]);
    }

    private CreateProgressGoalDto MakeCreateGoalDto(
        GoalType goalType = GoalType.Weight,
        decimal? targetValue = 70m,
        string? title = "Lose Weight")
    {
        return new CreateProgressGoalDto(
            _seededClientId,
            title ?? "Test Goal",
            "Test description",
            goalType,
            targetValue,
            "kg",
            DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(3)));
    }

    // ---------------------------------------------------------------------------
    // ── Entry: Create ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateEntryAsync_WithValidDto_PersistsCorrectFields()
    {
        // Arrange
        var entryDate = new DateOnly(2025, 6, 15);
        var dto = MakeCreateEntryDto(entryDate: entryDate, notes: "Feeling good");

        // Act
        var result = await _sut.CreateEntryAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.ProgressEntries.FirstAsync(e => e.Id == result.Id);
        persisted.ClientId.Should().Be(_seededClientId);
        persisted.EntryDate.Should().Be(entryDate);
        persisted.Notes.Should().Be("Feeling good");
        persisted.CreatedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateEntryAsync_WithMeasurements_PersistsMeasurementsOnEntry()
    {
        // Arrange
        var measurements = new List<CreateProgressMeasurementDto>
        {
            new(MetricType.Weight, null, 80m, "kg"),
            new(MetricType.BMI, null, 25.1m, null)
        };
        var dto = MakeCreateEntryDto(measurements: measurements);

        // Act
        var result = await _sut.CreateEntryAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.ProgressEntries
            .Include(e => e.Measurements)
            .FirstAsync(e => e.Id == result.Id);
        persisted.Measurements.Should().HaveCount(2);
        persisted.Measurements.Should().Contain(m => m.MetricType == MetricType.Weight && m.Value == 80m && m.Unit == "kg");
        persisted.Measurements.Should().Contain(m => m.MetricType == MetricType.BMI && m.Value == 25.1m);
    }

    [Fact]
    public async Task CreateEntryAsync_WithValidDto_ReturnsDetailDtoWithClientAndCreatorNames()
    {
        // Arrange
        var dto = MakeCreateEntryDto();

        // Act — use the acting user who is seeded with DisplayName "Acting User"
        var result = await _sut.CreateEntryAsync(dto, UserId);

        // Assert — client names come from the seeded client; creator name resolves from the seeded user
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("Progress");
        result.CreatedByUserId.Should().Be(UserId);
        result.CreatedByName.Should().Be("Acting User");
    }

    [Fact]
    public async Task CreateEntryAsync_WithNutritionistUserId_ReturnsDetailDtoWithNutritionistDisplayName()
    {
        // Arrange — use the nutritionist's userId so GetUserNameAsync resolves the nutritionist's name
        var dto = MakeCreateEntryDto();

        // Act
        var result = await _sut.CreateEntryAsync(dto, NutritionistId);

        // Assert
        result.CreatedByName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task CreateEntryAsync_WithValidDto_CallsAuditLogWithProgressEntryCreatedAction()
    {
        // Arrange
        var dto = MakeCreateEntryDto();

        // Act
        var result = await _sut.CreateEntryAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressEntryCreated",
            "ProgressEntry",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains(_seededClientId.ToString())));
    }

    [Fact]
    public async Task CreateEntryAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = MakeCreateEntryDto();

        // Act
        await _sut.CreateEntryAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateEntryAsync_WithValidDto_DispatchesCreatedNotificationWithCorrectFields()
    {
        // Arrange
        var dto = MakeCreateEntryDto();

        // Act
        var result = await _sut.CreateEntryAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressEntry" &&
            n.ChangeType == EntityChangeType.Created &&
            n.ClientId == _seededClientId &&
            n.EntityId == result.Id));
    }

    // ---------------------------------------------------------------------------
    // ── Entry: GetById ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetEntryByIdAsync_WhenEntryExists_ReturnsDetailDtoWithMeasurementsAndClientNames()
    {
        // Arrange
        var measurements = new List<CreateProgressMeasurementDto>
        {
            new(MetricType.Weight, null, 75m, "kg")
        };
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(measurements: measurements), UserId);

        // Act
        var result = await _sut.GetEntryByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.ClientId.Should().Be(_seededClientId);
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("Progress");
        result.Measurements.Should().ContainSingle(m => m.MetricType == MetricType.Weight && m.Value == 75m);
    }

    [Fact]
    public async Task GetEntryByIdAsync_WhenEntryDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetEntryByIdAsync(999_901);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetEntryByIdAsync_WhenEntryIsSoftDeleted_ReturnsNull()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        await _sut.SoftDeleteEntryAsync(created.Id, UserId);

        // Act — soft-deleted entries should be excluded by the query filter
        var result = await _sut.GetEntryByIdAsync(created.Id);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // ── Entry: GetByClient ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetEntriesByClientAsync_WithMultipleEntries_ReturnsEntriesOrderedByEntryDateDescending()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 1, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 6, 15)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 3, 10)), UserId);

        // Act
        var results = await _sut.GetEntriesByClientAsync(_seededClientId);

        // Assert
        results.Should().HaveCount(3);
        results[0].EntryDate.Should().Be(new DateOnly(2025, 6, 15));
        results[1].EntryDate.Should().Be(new DateOnly(2025, 3, 10));
        results[2].EntryDate.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task GetEntriesByClientAsync_WhenNoEntriesExist_ReturnsEmptyList()
    {
        // Act
        var results = await _sut.GetEntriesByClientAsync(_seededClientId);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEntriesByClientAsync_WithMeasurements_IncludesMeasurementCountInSummary()
    {
        // Arrange
        var measurements = new List<CreateProgressMeasurementDto>
        {
            new(MetricType.Weight, null, 80m, "kg"),
            new(MetricType.BMI, null, 24m, null),
            new(MetricType.WaistCircumference, null, 90m, "cm")
        };
        await _sut.CreateEntryAsync(MakeCreateEntryDto(measurements: measurements), UserId);

        // Act
        var results = await _sut.GetEntriesByClientAsync(_seededClientId);

        // Assert
        results.Should().ContainSingle();
        results[0].MeasurementCount.Should().Be(3);
    }

    [Fact]
    public async Task GetEntriesByClientAsync_WithSoftDeletedEntry_ExcludesSoftDeletedEntries()
    {
        // Arrange
        var entry1 = await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 1, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 2, 1)), UserId);
        await _sut.SoftDeleteEntryAsync(entry1.Id, UserId);

        // Act
        var results = await _sut.GetEntriesByClientAsync(_seededClientId);

        // Assert
        results.Should().HaveCount(1);
        results[0].EntryDate.Should().Be(new DateOnly(2025, 2, 1));
    }

    [Fact]
    public async Task GetEntriesByClientAsync_WithLongNotes_TruncatesNotePreviewAt80Chars()
    {
        // Arrange — notes longer than 80 characters should be truncated
        var longNotes = new string('A', 90);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(notes: longNotes), UserId);

        // Act
        var results = await _sut.GetEntriesByClientAsync(_seededClientId);

        // Assert
        results.Should().ContainSingle();
        results[0].NotePreview.Should().HaveLength(83); // 80 chars + "..."
        results[0].NotePreview.Should().EndWith("...");
        results[0].NotePreview.Should().StartWith(new string('A', 80));
    }

    // ---------------------------------------------------------------------------
    // ── Entry: Update ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_UpdatesEntryDateNotesAndSetsUpdatedAt()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 1, 1), notes: "Old notes"), UserId);
        var newDate = new DateOnly(2025, 7, 4);
        var updateDto = new UpdateProgressEntryDto(newDate, "New notes", []);

        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var result = await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ProgressEntries.FirstAsync(e => e.Id == created.Id);
        persisted.EntryDate.Should().Be(newDate);
        persisted.Notes.Should().Be("New notes");
        persisted.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_ReplacesMeasurements()
    {
        // Arrange — create with one measurement
        var original = new List<CreateProgressMeasurementDto>
        {
            new(MetricType.Weight, null, 80m, "kg")
        };
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(measurements: original), UserId);

        var newMeasurements = new List<CreateProgressMeasurementDto>
        {
            new(MetricType.BMI, null, 24.5m, null),
            new(MetricType.BodyFatPercentage, null, 18m, "%")
        };
        var updateDto = new UpdateProgressEntryDto(created.EntryDate, null, newMeasurements);

        // Act
        await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert — old Weight measurement gone, new ones present
        var persisted = await _dbContext.ProgressEntries
            .Include(e => e.Measurements)
            .FirstAsync(e => e.Id == created.Id);
        persisted.Measurements.Should().HaveCount(2);
        persisted.Measurements.Should().NotContain(m => m.MetricType == MetricType.Weight);
        persisted.Measurements.Should().Contain(m => m.MetricType == MetricType.BMI);
        persisted.Measurements.Should().Contain(m => m.MetricType == MetricType.BodyFatPercentage);
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_ReturnsTrue()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        var updateDto = new UpdateProgressEntryDto(created.EntryDate, "Updated", []);

        // Act
        var result = await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var updateDto = new UpdateProgressEntryDto(new DateOnly(2025, 1, 1), null, []);

        // Act
        var result = await _sut.UpdateEntryAsync(999_902, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_CallsAuditLogWithProgressEntryUpdatedAction()
    {
        // Arrange
        var entryDate = new DateOnly(2025, 4, 20);
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: entryDate), UserId);
        _auditLogService.ClearReceivedCalls();
        var updateDto = new UpdateProgressEntryDto(entryDate, null, []);

        // Act
        await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressEntryUpdated",
            "ProgressEntry",
            created.Id.ToString(),
            Arg.Is<string>(s => s.Contains(entryDate.ToString())));
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_CallsRetentionTracker()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        _retentionTracker.ClearReceivedCalls();
        var updateDto = new UpdateProgressEntryDto(created.EntryDate, null, []);

        // Act
        await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateEntryAsync_WhenEntryExists_DispatchesUpdatedNotification()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        _notificationDispatcher.ClearReceivedCalls();
        var updateDto = new UpdateProgressEntryDto(created.EntryDate, null, []);

        // Act
        await _sut.UpdateEntryAsync(created.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressEntry" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.ClientId == _seededClientId));
    }

    // ---------------------------------------------------------------------------
    // ── Entry: SoftDelete ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteEntryAsync_WhenEntryExists_SetsIsDeletedDeletedAtAndDeletedBy()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.SoftDeleteEntryAsync(created.Id, UserId);

        // Assert — bypass query filter to inspect soft-deleted entity
        var persisted = await _dbContext.ProgressEntries
            .IgnoreQueryFilters()
            .FirstAsync(e => e.Id == created.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeAfter(before);
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task SoftDeleteEntryAsync_WhenEntryExists_ReturnsTrue()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);

        // Act
        var result = await _sut.SoftDeleteEntryAsync(created.Id, UserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteEntryAsync_WhenEntryDoesNotExist_ReturnsFalse()
    {
        // Act
        var result = await _sut.SoftDeleteEntryAsync(999_903, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteEntryAsync_WhenEntryExists_CallsAuditLogWithProgressEntrySoftDeletedAction()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        _auditLogService.ClearReceivedCalls();

        // Act
        await _sut.SoftDeleteEntryAsync(created.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressEntrySoftDeleted",
            "ProgressEntry",
            created.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Soft-deleted")));
    }

    [Fact]
    public async Task SoftDeleteEntryAsync_WhenEntryExists_DispatchesDeletedNotification()
    {
        // Arrange
        var created = await _sut.CreateEntryAsync(MakeCreateEntryDto(), UserId);
        _notificationDispatcher.ClearReceivedCalls();

        // Act
        await _sut.SoftDeleteEntryAsync(created.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressEntry" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.ClientId == _seededClientId));
    }

    // ---------------------------------------------------------------------------
    // ── Goal: Create ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateGoalAsync_WithValidDto_PersistsGoalWithActiveStatusAndCorrectFields()
    {
        // Arrange
        var targetDate = new DateOnly(2025, 12, 31);
        var dto = new CreateProgressGoalDto(
            _seededClientId,
            "Reach ideal weight",
            "Lose 10 kg over 6 months",
            GoalType.Weight,
            70m,
            "kg",
            targetDate);

        // Act
        var result = await _sut.CreateGoalAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.ProgressGoals.FirstAsync(g => g.Id == result.Id);
        persisted.ClientId.Should().Be(_seededClientId);
        persisted.Title.Should().Be("Reach ideal weight");
        persisted.Description.Should().Be("Lose 10 kg over 6 months");
        persisted.GoalType.Should().Be(GoalType.Weight);
        persisted.TargetValue.Should().Be(70m);
        persisted.TargetUnit.Should().Be("kg");
        persisted.TargetDate.Should().Be(targetDate);
        persisted.Status.Should().Be(GoalStatus.Active);
        persisted.CreatedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateGoalAsync_WithValidDto_ReturnsDetailDtoWithClientAndCreatorNames()
    {
        // Arrange
        var dto = MakeCreateGoalDto();

        // Act
        var result = await _sut.CreateGoalAsync(dto, NutritionistId);

        // Assert
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("Progress");
        result.CreatedByUserId.Should().Be(NutritionistId);
        result.CreatedByName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task CreateGoalAsync_WithValidDto_CallsAuditLogWithProgressGoalCreatedAction()
    {
        // Arrange
        var dto = MakeCreateGoalDto();

        // Act
        var result = await _sut.CreateGoalAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressGoalCreated",
            "ProgressGoal",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains(_seededClientId.ToString())));
    }

    [Fact]
    public async Task CreateGoalAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = MakeCreateGoalDto();

        // Act
        await _sut.CreateGoalAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateGoalAsync_WithValidDto_DispatchesCreatedNotificationWithCorrectEntityType()
    {
        // Arrange
        var dto = MakeCreateGoalDto();

        // Act
        var result = await _sut.CreateGoalAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressGoal" &&
            n.ChangeType == EntityChangeType.Created &&
            n.ClientId == _seededClientId &&
            n.EntityId == result.Id));
    }

    // ---------------------------------------------------------------------------
    // ── Goal: GetById ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetGoalByIdAsync_WhenGoalExists_ReturnsDetailDtoWithClientNames()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), NutritionistId);

        // Act
        var result = await _sut.GetGoalByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.ClientId.Should().Be(_seededClientId);
        result.ClientFirstName.Should().Be("Alice");
        result.ClientLastName.Should().Be("Progress");
        result.Status.Should().Be(GoalStatus.Active);
    }

    [Fact]
    public async Task GetGoalByIdAsync_WhenGoalDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _sut.GetGoalByIdAsync(999_904);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // ── Goal: GetByClient ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetGoalsByClientAsync_WithMultipleGoals_ReturnsGoalsOrderedByCreatedAtDescending()
    {
        // Arrange — create three goals; CreatedAt is set by the service to UtcNow, so they
        // will naturally be ordered by insertion. We verify ordering is descending.
        var first = await _sut.CreateGoalAsync(MakeCreateGoalDto(title: "Goal 1"), UserId);
        var second = await _sut.CreateGoalAsync(MakeCreateGoalDto(title: "Goal 2"), UserId);
        var third = await _sut.CreateGoalAsync(MakeCreateGoalDto(title: "Goal 3"), UserId);

        // Act
        var results = await _sut.GetGoalsByClientAsync(_seededClientId);

        // Assert — most recently created goal should be first
        results.Should().HaveCount(3);
        // IDs should be in descending creation order
        results[0].Id.Should().Be(third.Id);
        results[1].Id.Should().Be(second.Id);
        results[2].Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task GetGoalsByClientAsync_WhenNoGoalsExist_ReturnsEmptyList()
    {
        // Act
        var results = await _sut.GetGoalsByClientAsync(_seededClientId);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetGoalsByClientAsync_WithSoftDeletedGoal_ExcludesSoftDeletedGoals()
    {
        // Arrange
        var goal1 = await _sut.CreateGoalAsync(MakeCreateGoalDto(title: "Keep"), UserId);
        var goal2 = await _sut.CreateGoalAsync(MakeCreateGoalDto(title: "Delete"), UserId);
        await _sut.SoftDeleteGoalAsync(goal2.Id, UserId);

        // Act
        var results = await _sut.GetGoalsByClientAsync(_seededClientId);

        // Assert
        results.Should().ContainSingle();
        results[0].Id.Should().Be(goal1.Id);
    }

    // ---------------------------------------------------------------------------
    // ── Goal: Update ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalExists_UpdatesAllFieldsAndSetsUpdatedAt()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        var newTargetDate = new DateOnly(2026, 6, 30);
        var updateDto = new UpdateProgressGoalDto(
            "Revised Goal",
            "New description",
            GoalType.BodyComposition,
            25m,
            "%",
            newTargetDate);

        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        var result = await _sut.UpdateGoalAsync(created.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ProgressGoals.FindAsync(created.Id);
        persisted!.Title.Should().Be("Revised Goal");
        persisted.Description.Should().Be("New description");
        persisted.GoalType.Should().Be(GoalType.BodyComposition);
        persisted.TargetValue.Should().Be(25m);
        persisted.TargetUnit.Should().Be("%");
        persisted.TargetDate.Should().Be(newTargetDate);
        persisted.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalExists_ReturnsTrue()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        var updateDto = new UpdateProgressGoalDto("Updated", null, GoalType.Weight, null, null, null);

        // Act
        var result = await _sut.UpdateGoalAsync(created.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var updateDto = new UpdateProgressGoalDto("Title", null, GoalType.Weight, null, null, null);

        // Act
        var result = await _sut.UpdateGoalAsync(999_905, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalExists_CallsAuditLogWithProgressGoalUpdatedAction()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _auditLogService.ClearReceivedCalls();
        var updateDto = new UpdateProgressGoalDto("New title", null, GoalType.Weight, null, null, null);

        // Act
        await _sut.UpdateGoalAsync(created.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressGoalUpdated",
            "ProgressGoal",
            created.Id.ToString(),
            "Updated progress goal");
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalExists_CallsRetentionTracker()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _retentionTracker.ClearReceivedCalls();
        var updateDto = new UpdateProgressGoalDto("New title", null, GoalType.Weight, null, null, null);

        // Act
        await _sut.UpdateGoalAsync(created.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateGoalAsync_WhenGoalExists_DispatchesUpdatedNotification()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _notificationDispatcher.ClearReceivedCalls();
        var updateDto = new UpdateProgressGoalDto("New title", null, GoalType.Weight, null, null, null);

        // Act
        await _sut.UpdateGoalAsync(created.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressGoal" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.ClientId == _seededClientId));
    }

    // ---------------------------------------------------------------------------
    // ── Goal: UpdateStatus ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateGoalStatusAsync_WhenGoalExists_UpdatesStatusAndSetsUpdatedAt()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Achieved, UserId);

        // Assert
        var persisted = await _dbContext.ProgressGoals.FindAsync(created.Id);
        persisted!.Status.Should().Be(GoalStatus.Achieved);
        persisted.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_WhenGoalExists_ReturnsTrue()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);

        // Act
        var result = await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Achieved, UserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_WhenGoalDoesNotExist_ReturnsFalse()
    {
        // Act
        var result = await _sut.UpdateGoalStatusAsync(999_906, GoalStatus.Achieved, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_WhenGoalExists_CallsAuditLogWithStatusChangedActionAndOldToNewDetail()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _auditLogService.ClearReceivedCalls();

        // Act
        await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Achieved, UserId);

        // Assert — detail string must contain both old (Active) and new (Achieved) status
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressGoalStatusChanged",
            "ProgressGoal",
            created.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Active") && s.Contains("Achieved")));
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_WhenGoalExists_DispatchesUpdatedNotification()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _notificationDispatcher.ClearReceivedCalls();

        // Act
        await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Achieved, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressGoal" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_ActiveToAchieved_PersistsAchievedStatus()
    {
        // Arrange — goal starts Active (set by CreateGoalAsync)
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);

        // Act
        await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Achieved, UserId);

        // Assert
        var persisted = await _dbContext.ProgressGoals.FindAsync(created.Id);
        persisted!.Status.Should().Be(GoalStatus.Achieved);
    }

    [Fact]
    public async Task UpdateGoalStatusAsync_ActiveToAbandoned_PersistsAbandonedStatus()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);

        // Act
        await _sut.UpdateGoalStatusAsync(created.Id, GoalStatus.Abandoned, UserId);

        // Assert
        var persisted = await _dbContext.ProgressGoals.FindAsync(created.Id);
        persisted!.Status.Should().Be(GoalStatus.Abandoned);
    }

    // ---------------------------------------------------------------------------
    // ── Goal: SoftDelete ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteGoalAsync_WhenGoalExists_SetsIsDeletedDeletedAtAndDeletedBy()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.SoftDeleteGoalAsync(created.Id, UserId);

        // Assert — bypass query filter to inspect soft-deleted entity
        var persisted = await _dbContext.ProgressGoals
            .IgnoreQueryFilters()
            .FirstAsync(g => g.Id == created.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeAfter(before);
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task SoftDeleteGoalAsync_WhenGoalExists_ReturnsTrue()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);

        // Act
        var result = await _sut.SoftDeleteGoalAsync(created.Id, UserId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteGoalAsync_WhenGoalDoesNotExist_ReturnsFalse()
    {
        // Act
        var result = await _sut.SoftDeleteGoalAsync(999_907, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteGoalAsync_WhenGoalExists_CallsAuditLogWithProgressGoalSoftDeletedAction()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _auditLogService.ClearReceivedCalls();

        // Act
        await _sut.SoftDeleteGoalAsync(created.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ProgressGoalSoftDeleted",
            "ProgressGoal",
            created.Id.ToString(),
            "Soft-deleted progress goal");
    }

    [Fact]
    public async Task SoftDeleteGoalAsync_WhenGoalExists_DispatchesDeletedNotification()
    {
        // Arrange
        var created = await _sut.CreateGoalAsync(MakeCreateGoalDto(), UserId);
        _notificationDispatcher.ClearReceivedCalls();

        // Act
        await _sut.SoftDeleteGoalAsync(created.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ProgressGoal" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.ClientId == _seededClientId));
    }

    // ---------------------------------------------------------------------------
    // ── Chart Data ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetChartDataAsync_WithMatchingMeasurements_ReturnsDataPointsOrderedByEntryDateAscending()
    {
        // Arrange — three entries on different dates, all with Weight measurements
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            entryDate: new DateOnly(2025, 3, 1),
            measurements: [new(MetricType.Weight, null, 82m, "kg")]), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            entryDate: new DateOnly(2025, 1, 1),
            measurements: [new(MetricType.Weight, null, 85m, "kg")]), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            entryDate: new DateOnly(2025, 6, 1),
            measurements: [new(MetricType.Weight, null, 78m, "kg")]), UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.Weight);

        // Assert
        result.Should().NotBeNull();
        result!.Points.Should().HaveCount(3);
        result.Points[0].Date.Should().Be(new DateOnly(2025, 1, 1));
        result.Points[1].Date.Should().Be(new DateOnly(2025, 3, 1));
        result.Points[2].Date.Should().Be(new DateOnly(2025, 6, 1));
        result.Points[0].Value.Should().Be(85m);
        result.Points[1].Value.Should().Be(82m);
        result.Points[2].Value.Should().Be(78m);
    }

    [Fact]
    public async Task GetChartDataAsync_WhenNoMeasurementsMatchMetricType_ReturnsNull()
    {
        // Arrange — create entry with Weight, then ask for BMI
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            measurements: [new(MetricType.Weight, null, 80m, "kg")]), UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.BMI);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetChartDataAsync_WithWeightMetric_ReturnsCorrectLabel()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            measurements: [new(MetricType.Weight, null, 80m, "kg")]), UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.Weight);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Weight");
    }

    [Fact]
    public async Task GetChartDataAsync_WithMeasurements_ReturnsUnitFromFirstMatchingMeasurement()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            measurements: [new(MetricType.Weight, null, 80m, "kg")]), UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.Weight);

        // Assert
        result.Should().NotBeNull();
        result!.Unit.Should().Be("kg");
    }

    [Fact]
    public async Task GetChartDataAsync_WithSoftDeletedEntry_ExcludesSoftDeletedEntries()
    {
        // Arrange — create two entries, soft-delete one
        var entry1 = await _sut.CreateEntryAsync(MakeCreateEntryDto(
            entryDate: new DateOnly(2025, 1, 1),
            measurements: [new(MetricType.Weight, null, 85m, "kg")]), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            entryDate: new DateOnly(2025, 2, 1),
            measurements: [new(MetricType.Weight, null, 83m, "kg")]), UserId);
        await _sut.SoftDeleteEntryAsync(entry1.Id, UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.Weight);

        // Assert
        result.Should().NotBeNull();
        result!.Points.Should().ContainSingle();
        result.Points[0].Value.Should().Be(83m);
    }

    [Fact]
    public async Task GetChartDataAsync_WithBodyFatMetric_ReturnsCorrectLabel()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(
            measurements: [new(MetricType.BodyFatPercentage, null, 18m, "%")]), UserId);

        // Act
        var result = await _sut.GetChartDataAsync(_seededClientId, MetricType.BodyFatPercentage);

        // Assert
        result.Should().NotBeNull();
        result!.Label.Should().Be("Body Fat %");
    }

    // ---------------------------------------------------------------------------
    // ── Dashboard: GetRecent ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentByClientAsync_WithMoreEntriesThanCount_ReturnsOnlyRequestedCount()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 1, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 2, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 3, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 4, 1)), UserId);

        // Act
        var results = await _sut.GetRecentByClientAsync(_seededClientId, count: 2);

        // Assert
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRecentByClientAsync_WithMultipleEntries_OrdersByEntryDateDescending()
    {
        // Arrange
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 1, 1)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 6, 15)), UserId);
        await _sut.CreateEntryAsync(MakeCreateEntryDto(entryDate: new DateOnly(2025, 3, 10)), UserId);

        // Act
        var results = await _sut.GetRecentByClientAsync(_seededClientId, count: 3);

        // Assert
        results[0].EntryDate.Should().Be(new DateOnly(2025, 6, 15));
        results[1].EntryDate.Should().Be(new DateOnly(2025, 3, 10));
        results[2].EntryDate.Should().Be(new DateOnly(2025, 1, 1));
    }

    [Fact]
    public async Task GetRecentByClientAsync_WhenNoEntriesExist_ReturnsEmptyList()
    {
        // Act
        var results = await _sut.GetRecentByClientAsync(_seededClientId, count: 3);

        // Assert
        results.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // ── Goal Achievement Detection ──
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateEntryAsync_WhenMeasurementMeetsActiveGoalTarget_LogsGoalAchievementSuggestedAudit()
    {
        // Arrange — create an active Weight goal targeting 70 kg
        var goalDto = new CreateProgressGoalDto(
            _seededClientId,
            "Reach 70kg",
            null,
            GoalType.Weight,
            70m,
            "kg",
            null);
        await _sut.CreateGoalAsync(goalDto, UserId);

        _auditLogService.ClearReceivedCalls();

        // Create an entry where Weight is at or below the target (70 <= 70 = achieved)
        var entryDto = MakeCreateEntryDto(measurements: [new(MetricType.Weight, null, 70m, "kg")]);

        // Act
        await _sut.CreateEntryAsync(entryDto, UserId);

        // Assert — GoalAchievementSuggested should have been logged
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "GoalAchievementSuggested",
            "ProgressGoal",
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Weight") && s.Contains("70")));
    }

    [Fact]
    public async Task CreateEntryAsync_WhenMeasurementDoesNotMeetGoalTarget_DoesNotLogGoalAchievementSuggested()
    {
        // Arrange — active Weight goal targeting 70 kg; measurement is 80 kg (above target, not achieved)
        var goalDto = new CreateProgressGoalDto(
            _seededClientId,
            "Reach 70kg",
            null,
            GoalType.Weight,
            70m,
            "kg",
            null);
        await _sut.CreateGoalAsync(goalDto, UserId);

        _auditLogService.ClearReceivedCalls();

        // Entry with Weight = 80, which does NOT meet the <= 70 target
        var entryDto = MakeCreateEntryDto(measurements: [new(MetricType.Weight, null, 80m, "kg")]);

        // Act
        await _sut.CreateEntryAsync(entryDto, UserId);

        // Assert — GoalAchievementSuggested should not have been logged
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(),
            "GoalAchievementSuggested",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task CreateEntryAsync_WithNoActiveGoals_CompletesWithoutError()
    {
        // Arrange — no goals seeded, so CheckGoalAchievementsAsync takes the early-return path

        // Act
        var act = async () =>
        {
            var dto = MakeCreateEntryDto(measurements: [new(MetricType.Weight, null, 80m, "kg")]);
            await _sut.CreateEntryAsync(dto, UserId);
        };

        // Assert — must complete without throwing
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
