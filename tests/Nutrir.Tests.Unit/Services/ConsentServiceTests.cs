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

public class ConsentServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IRetentionTracker _retentionTracker;
    private readonly INotificationDispatcher _notificationDispatcher;

    private readonly ConsentService _sut;

    private const string NutritionistId = "nutritionist-consent-test-001";
    private const string UserId = "acting-user-consent-001";
    private const string PolicyVersion = "2.0";
    private const string Purpose = "Treatment and care";

    // The seeded client Id is captured after SaveChanges so tests don't hard-code a magic number.
    private int _seededClientId;

    public ConsentServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _retentionTracker = Substitute.For<IRetentionTracker>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();

        _sut = new ConsentService(
            _dbContext,
            _auditLogService,
            _retentionTracker,
            _notificationDispatcher,
            NullLogger<ConsentService>.Instance);

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
            UserName = "nutritionist@consenttest.com",
            NormalizedUserName = "NUTRITIONIST@CONSENTTEST.COM",
            Email = "nutritionist@consenttest.com",
            NormalizedEmail = "NUTRITIONIST@CONSENTTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Test",
            LastName = "ConsentClient",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    // ---------------------------------------------------------------------------
    // GrantConsentAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_SetsConsentGivenTrue()
    {
        // Arrange — client starts without consent (set in SeedData)

        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentGiven.Should().BeTrue();
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_SetsConsentTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentTimestamp.Should().NotBeNull();
        persisted.ConsentTimestamp.Should().BeAfter(before);
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_SetsConsentPolicyVersion()
    {
        // Arrange — use a distinctive version to confirm it was written
        const string version = "3.1";

        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, version, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentPolicyVersion.Should().Be(version);
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_CreatesConsentEventRecord()
    {
        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert
        var events = await _dbContext.ConsentEvents
            .Where(e => e.ClientId == _seededClientId)
            .ToListAsync();

        events.Should().ContainSingle(because: "exactly one ConsentEvent should be created");
        var evt = events.Single();
        evt.EventType.Should().Be(ConsentEventType.ConsentGiven);
        evt.ConsentPurpose.Should().Be(Purpose);
        evt.PolicyVersion.Should().Be(PolicyVersion);
        evt.RecordedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_CallsAuditLog()
    {
        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentGranted",
            "Client",
            _seededClientId.ToString(),
            $"Consent granted for purpose '{Purpose}', policy v{PolicyVersion}");
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_CallsRetentionTracker()
    {
        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task GrantConsentAsync_WithExistingClient_DispatchesConsentEventNotification()
    {
        // Act
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Assert — the dispatcher receives a ConsentEvent Created notification scoped to the client.
        // Note: TryDispatchAsync swallows exceptions, so a broken dispatcher won't surface here.
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ConsentEvent" &&
            n.ChangeType == EntityChangeType.Created &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task GrantConsentAsync_WhenConsentAlreadyGranted_SucceedsAndUpdatesFields()
    {
        // Arrange — grant consent once so the client already has ConsentGiven = true
        await _sut.GrantConsentAsync(_seededClientId, Purpose, "1.0", UserId);

        // Act — grant again with a newer policy version; must not throw
        const string newerVersion = "2.0";
        await _sut.GrantConsentAsync(_seededClientId, Purpose, newerVersion, UserId);

        // Assert — the latest policy version is stored and ConsentGiven remains true
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentGiven.Should().BeTrue();
        persisted.ConsentPolicyVersion.Should().Be(newerVersion,
            because: "the second grant should overwrite the policy version with the newer one");
    }

    [Fact]
    public async Task GrantConsentAsync_WhenConsentAlreadyGranted_CreatesSecondConsentEvent()
    {
        // Arrange — grant consent once
        await _sut.GrantConsentAsync(_seededClientId, Purpose, "1.0", UserId);

        // Act — grant again
        await _sut.GrantConsentAsync(_seededClientId, Purpose, "2.0", UserId);

        // Assert — two ConsentGiven events should exist for the client
        var events = await _dbContext.ConsentEvents
            .Where(e => e.ClientId == _seededClientId && e.EventType == ConsentEventType.ConsentGiven)
            .ToListAsync();

        events.Should().HaveCount(2, because: "each call to GrantConsentAsync writes a new ConsentEvent");
    }

    [Fact]
    public async Task GrantConsentAsync_WithMissingClient_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentId = 999_901;

        // Act
        var act = () => _sut.GrantConsentAsync(nonExistentId, Purpose, PolicyVersion, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentId}*");
    }

    [Fact]
    public async Task GrantConsentAsync_WithMissingClient_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_902;

        // Act
        await FluentActions.Invoking(() => _sut.GrantConsentAsync(nonExistentId, Purpose, PolicyVersion, UserId))
            .Should().ThrowAsync<InvalidOperationException>();

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // WithdrawConsentAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_SetsConsentGivenFalse()
    {
        // Arrange — ensure the client has consent before withdrawing
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentGiven.Should().BeFalse();
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_SetsEmailRemindersEnabledFalse()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.EmailRemindersEnabled.Should().BeFalse(
            because: "email reminders must be disabled when a client withdraws consent");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_SetsConsentTimestamp()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);
        var beforeWithdrawal = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == _seededClientId);
        persisted.ConsentTimestamp.Should().NotBeNull();
        persisted.ConsentTimestamp.Should().BeAfter(beforeWithdrawal,
            because: "ConsentTimestamp should reflect the time of the most recent consent event");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_CreatesWithdrawalConsentEvent()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        var withdrawalEvents = await _dbContext.ConsentEvents
            .Where(e => e.ClientId == _seededClientId && e.EventType == ConsentEventType.ConsentWithdrawn)
            .ToListAsync();

        withdrawalEvents.Should().ContainSingle(because: "exactly one withdrawal ConsentEvent should be created");
        var evt = withdrawalEvents.Single();
        evt.RecordedByUserId.Should().Be(UserId);
        evt.PolicyVersion.Should().Be(PolicyVersion,
            because: "the withdrawal event should record the policy version that was active at time of withdrawal");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithReason_RecordsReasonInConsentEventNotes()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);
        const string reason = "Patient no longer wishes to receive treatment";

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId, reason);

        // Assert
        var withdrawalEvent = await _dbContext.ConsentEvents
            .SingleAsync(e => e.ClientId == _seededClientId && e.EventType == ConsentEventType.ConsentWithdrawn);

        withdrawalEvent.Notes.Should().Be(reason,
            because: "the withdrawal reason must be stored in ConsentEvent.Notes");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithoutReason_LeavesConsentEventNotesNull()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Act — no reason supplied
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        var withdrawalEvent = await _dbContext.ConsentEvents
            .SingleAsync(e => e.ClientId == _seededClientId && e.EventType == ConsentEventType.ConsentWithdrawn);

        withdrawalEvent.Notes.Should().BeNull(
            because: "Notes should remain null when no reason is provided");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithReason_CallsAuditLogWithReasonInDetails()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);
        const string reason = "Data privacy concerns";

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId, reason);

        // Assert — the second call to LogAsync is for the withdrawal; we check it includes the reason
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentWithdrawn",
            "Client",
            _seededClientId.ToString(),
            $"Consent withdrawn. Reason: {reason}");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithoutReason_CallsAuditLogWithDefaultMessage()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConsentWithdrawn",
            "Client",
            _seededClientId.ToString(),
            "Consent withdrawn");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_CallsRetentionTracker()
    {
        // Arrange — grant consent first, then clear mock state so assertion targets only the withdrawal
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);
        _retentionTracker.ClearReceivedCalls();

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithExistingClient_DispatchesConsentEventNotification()
    {
        // Arrange
        await _sut.GrantConsentAsync(_seededClientId, Purpose, PolicyVersion, UserId);

        // Clear calls accumulated during grant so the assertion targets only the withdrawal dispatch
        _notificationDispatcher.ClearReceivedCalls();

        // Act
        await _sut.WithdrawConsentAsync(_seededClientId, UserId);

        // Assert — Note: TryDispatchAsync swallows exceptions, so a broken dispatcher won't surface here.
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ConsentEvent" &&
            n.ChangeType == EntityChangeType.Created &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithMissingClient_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentId = 999_903;

        // Act
        var act = () => _sut.WithdrawConsentAsync(nonExistentId, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentId}*");
    }

    [Fact]
    public async Task WithdrawConsentAsync_WithMissingClient_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_904;

        // Act
        await FluentActions.Invoking(() => _sut.WithdrawConsentAsync(nonExistentId, UserId))
            .Should().ThrowAsync<InvalidOperationException>();

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
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
