using FluentAssertions;
using Microsoft.Data.Sqlite;
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

public class AuditLogServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly IAuditSourceProvider _auditSourceProvider;
    private readonly AuditLogService _sut;

    private const string UserId = "audit-test-user-001";

    public AuditLogServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditSourceProvider = Substitute.For<IAuditSourceProvider>();
        _auditSourceProvider.CurrentSource.Returns(AuditSource.Web);

        var factory = new SharedConnectionContextFactory(_connection);

        _sut = new AuditLogService(
            factory,
            _auditSourceProvider,
            NullLogger<AuditLogService>.Instance);

        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        // Seed a user for QueryAsync display name resolution
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = UserId,
            UserName = "auditor@test.com",
            NormalizedUserName = "AUDITOR@TEST.COM",
            Email = "auditor@test.com",
            NormalizedEmail = "AUDITOR@TEST.COM",
            FirstName = "Audit",
            LastName = "Tester",
            DisplayName = "Audit Tester",
            CreatedDate = DateTime.UtcNow
        });

        // Seed audit log entries with varied data for query/filter tests
        _dbContext.AuditLogEntries.AddRange(
            new AuditLogEntry
            {
                UserId = UserId,
                Action = "ClientCreated",
                EntityType = "Client",
                EntityId = "1",
                Details = "Created new client record",
                IpAddress = "192.168.1.1",
                Timestamp = DateTime.UtcNow.AddDays(-10),
                Source = AuditSource.Web
            },
            new AuditLogEntry
            {
                UserId = UserId,
                Action = "ClientUpdated",
                EntityType = "Client",
                EntityId = "1",
                Details = "Updated email address",
                IpAddress = "192.168.1.2",
                Timestamp = DateTime.UtcNow.AddDays(-5),
                Source = AuditSource.Cli
            },
            new AuditLogEntry
            {
                UserId = UserId,
                Action = "AppointmentCreated",
                EntityType = "Appointment",
                EntityId = "10",
                Details = "Scheduled initial consultation",
                Timestamp = DateTime.UtcNow.AddDays(-3),
                Source = AuditSource.Web
            },
            new AuditLogEntry
            {
                UserId = "other-user-002",
                Action = "MealPlanCreated",
                EntityType = "MealPlan",
                EntityId = "5",
                Details = "Created meal plan draft",
                Timestamp = DateTime.UtcNow.AddDays(-1),
                Source = AuditSource.AiAssistant
            }
        );

        _dbContext.SaveChanges();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // LogAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task LogAsync_PersistsAuditEntryWithAllFields()
    {
        // Arrange
        var beforeLog = DateTime.UtcNow.AddSeconds(-1);

        // Act
        await _sut.LogAsync(
            UserId,
            "ConsentGiven",
            "Client",
            entityId: "42",
            details: "Consent recorded",
            ipAddress: "10.0.0.1");

        // Assert
        var entry = _dbContext.AuditLogEntries
            .OrderByDescending(e => e.Id)
            .First(e => e.Action == "ConsentGiven");

        entry.UserId.Should().Be(UserId);
        entry.Action.Should().Be("ConsentGiven");
        entry.EntityType.Should().Be("Client");
        entry.EntityId.Should().Be("42");
        entry.Details.Should().Be("Consent recorded");
        entry.IpAddress.Should().Be("10.0.0.1");
        entry.Source.Should().Be(AuditSource.Web);
        entry.Timestamp.Should().BeAfter(beforeLog);
    }

    [Fact]
    public async Task LogAsync_WithOptionalNulls_PersistsEntry()
    {
        // Act
        await _sut.LogAsync(UserId, "Login", "User");

        // Assert
        var entry = _dbContext.AuditLogEntries
            .OrderByDescending(e => e.Id)
            .First(e => e.Action == "Login");

        entry.EntityId.Should().BeNull();
        entry.Details.Should().BeNull();
        entry.IpAddress.Should().BeNull();
    }

    [Fact]
    public async Task LogAsync_UsesAuditSourceProvider()
    {
        // Arrange
        _auditSourceProvider.CurrentSource.Returns(AuditSource.AiAssistant);

        // Act
        await _sut.LogAsync(UserId, "AiAction", "MealPlan");

        // Assert
        var entry = _dbContext.AuditLogEntries
            .OrderByDescending(e => e.Id)
            .First(e => e.Action == "AiAction");

        entry.Source.Should().Be(AuditSource.AiAssistant);
    }

    // ---------------------------------------------------------------------------
    // GetRecentAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentAsync_ReturnsEntriesOrderedByTimestampDescending()
    {
        // Act
        var results = await _sut.GetRecentAsync(10);

        // Assert
        results.Should().HaveCount(4, because: "four entries were seeded");
        results.First().Action.Should().Be("MealPlanCreated",
            because: "it has the most recent timestamp");
        results.Last().Action.Should().Be("ClientCreated",
            because: "it has the oldest timestamp");
    }

    [Fact]
    public async Task GetRecentAsync_RespectsCountParameter()
    {
        // Act
        var results = await _sut.GetRecentAsync(2);

        // Assert
        results.Should().HaveCount(2, because: "count was limited to 2");
    }

    [Fact]
    public async Task GetRecentAsync_DefaultsToTenEntries()
    {
        // Act
        var results = await _sut.GetRecentAsync();

        // Assert — only 4 seeded, so we get 4
        results.Should().HaveCount(4);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsDtoWithCorrectFields()
    {
        // Act
        var results = await _sut.GetRecentAsync(10);

        // Assert — verify DTO mapping on the most recent entry
        var latest = results.First();
        latest.Action.Should().Be("MealPlanCreated");
        latest.EntityType.Should().Be("MealPlan");
        latest.EntityId.Should().Be("5");
        latest.UserId.Should().Be("other-user-002");
        latest.Source.Should().Be(AuditSource.AiAssistant);
    }

    // ---------------------------------------------------------------------------
    // QueryAsync — filtering
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_WithNoFilters_ReturnsAllEntries()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest());

        // Assert
        result.TotalCount.Should().Be(4);
        result.Items.Should().HaveCount(4);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task QueryAsync_FilterByAction_ReturnsMatchingEntries()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Action: "ClientCreated"));

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().Action.Should().Be("ClientCreated");
    }

    [Fact]
    public async Task QueryAsync_FilterByEntityType_ReturnsMatchingEntries()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(EntityType: "Client"));

        // Assert
        result.TotalCount.Should().Be(2, because: "two Client entries were seeded");
        result.Items.Should().OnlyContain(i => i.EntityType == "Client");
    }

    [Fact]
    public async Task QueryAsync_FilterBySource_ReturnsMatchingEntries()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Source: AuditSource.Cli));

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().Action.Should().Be("ClientUpdated");
    }

    [Fact]
    public async Task QueryAsync_FilterByDateRange_ReturnsMatchingEntries()
    {
        // Arrange — from 6 days ago to 2 days ago should get ClientUpdated and AppointmentCreated
        var from = DateTime.UtcNow.AddDays(-6);
        var to = DateTime.UtcNow.AddDays(-2);

        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(From: from, To: to));

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(i => i.Action == "ClientUpdated");
        result.Items.Should().Contain(i => i.Action == "AppointmentCreated");
    }

    [Fact]
    public async Task QueryAsync_FilterByFromDateOnly_ExcludesOlderEntries()
    {
        // Arrange — from 4 days ago should exclude ClientCreated (-10d) and ClientUpdated (-5d)
        var from = DateTime.UtcNow.AddDays(-4);

        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(From: from));

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(i => i.Action == "AppointmentCreated");
        result.Items.Should().Contain(i => i.Action == "MealPlanCreated");
    }

    [Fact]
    public async Task QueryAsync_FilterByToDateOnly_ExcludesNewerEntries()
    {
        // Arrange — To 4 days ago should exclude AppointmentCreated (-3d) and MealPlanCreated (-1d)
        var to = DateTime.UtcNow.AddDays(-4);

        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(To: to));

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().Contain(i => i.Action == "ClientCreated");
        result.Items.Should().Contain(i => i.Action == "ClientUpdated");
    }

    [Fact]
    public async Task QueryAsync_FilterBySearchTerm_MatchesDetailsField()
    {
        // Act — search for "consultation" which appears in the Details of AppointmentCreated
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(SearchTerm: "consultation"));

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().Action.Should().Be("AppointmentCreated");
    }

    [Fact]
    public async Task QueryAsync_FilterBySearchTerm_IsCaseInsensitive()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(SearchTerm: "MEALPLAN"));

        // Assert — matches EntityType "MealPlan"
        result.TotalCount.Should().Be(1);
        result.Items.Single().EntityType.Should().Be("MealPlan");
    }

    [Fact]
    public async Task QueryAsync_FilterBySearchTerm_MatchesUserId()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(SearchTerm: "other-user"));

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().UserId.Should().Be("other-user-002");
    }

    // ---------------------------------------------------------------------------
    // QueryAsync — pagination
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_Pagination_ReturnsCorrectPage()
    {
        // Act — page 2 with page size 2
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Page: 2, PageSize: 2));

        // Assert
        result.TotalCount.Should().Be(4, because: "total count reflects all matching entries");
        result.Items.Should().HaveCount(2, because: "page size is 2 and there are 4 total entries");
        result.Page.Should().Be(2);
        result.PageSize.Should().Be(2);
    }

    [Fact]
    public async Task QueryAsync_Pagination_LastPageReturnsFewerItems()
    {
        // Act — page 2 with page size 3 against 4 entries should return 1 item
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Page: 2, PageSize: 3));

        // Assert
        result.TotalCount.Should().Be(4);
        result.Items.Should().HaveCount(1, because: "only 1 entry remains on the last page");
    }

    [Fact]
    public async Task QueryAsync_Pagination_OrderedByTimestampDescending()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(PageSize: 10));

        // Assert
        result.Items.Should().BeInDescendingOrder(i => i.Timestamp);
    }

    // ---------------------------------------------------------------------------
    // QueryAsync — user display name resolution
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_ResolvesUserDisplayNames()
    {
        // Act
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Action: "ClientCreated"));

        // Assert — the seeded user has DisplayName "Audit Tester"
        var item = result.Items.Single();
        item.UserDisplayName.Should().Be("Audit Tester");
    }

    [Fact]
    public async Task QueryAsync_UnknownUser_FallsBackToUserId()
    {
        // Act — "other-user-002" is not in the Users table
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(Action: "MealPlanCreated"));

        // Assert
        var item = result.Items.Single();
        item.UserDisplayName.Should().Be("other-user-002",
            because: "unknown users fall back to their UserId");
    }

    // ---------------------------------------------------------------------------
    // QueryAsync — combined filters
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task QueryAsync_CombinedFilters_AppliesAllFilters()
    {
        // Act — filter by entity type Client AND action ClientUpdated
        var result = await _sut.QueryAsync(new AuditLogQueryRequest(
            Action: "ClientUpdated",
            EntityType: "Client",
            Source: AuditSource.Cli));

        // Assert
        result.TotalCount.Should().Be(1);
        result.Items.Single().Action.Should().Be("ClientUpdated");
        result.Items.Single().Source.Should().Be(AuditSource.Cli);
    }

    // ---------------------------------------------------------------------------
    // GetDistinctActionsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDistinctActionsAsync_ReturnsUniqueActionsSortedAlphabetically()
    {
        // Act
        var actions = await _sut.GetDistinctActionsAsync();

        // Assert
        actions.Should().HaveCount(4);
        actions.Should().BeInAscendingOrder();
        actions.Should().Contain("ClientCreated");
        actions.Should().Contain("ClientUpdated");
        actions.Should().Contain("AppointmentCreated");
        actions.Should().Contain("MealPlanCreated");
    }

    // ---------------------------------------------------------------------------
    // GetDistinctEntityTypesAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDistinctEntityTypesAsync_ReturnsUniqueEntityTypesSortedAlphabetically()
    {
        // Act
        var types = await _sut.GetDistinctEntityTypesAsync();

        // Assert
        types.Should().HaveCount(3, because: "Client, Appointment, and MealPlan are the distinct types");
        types.Should().BeInAscendingOrder();
        types.Should().Contain("Client");
        types.Should().Contain("Appointment");
        types.Should().Contain("MealPlan");
    }

}

/// <summary>
/// Tests for AuditLogService with an empty database (no seeded audit entries).
/// Separate class avoids the immutability constraint on AuditLogEntry deletion.
/// </summary>
public class AuditLogServiceEmptyDatabaseTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly AuditLogService _sut;

    public AuditLogServiceEmptyDatabaseTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        var auditSourceProvider = Substitute.For<IAuditSourceProvider>();
        auditSourceProvider.CurrentSource.Returns(AuditSource.Web);

        var factory = new SharedConnectionContextFactory(_connection);

        _sut = new AuditLogService(
            factory,
            auditSourceProvider,
            NullLogger<AuditLogService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetRecentAsync_WithNoEntries_ReturnsEmptyList()
    {
        var results = await _sut.GetRecentAsync();
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAsync_WithNoEntries_ReturnsEmptyResult()
    {
        var result = await _sut.QueryAsync(new AuditLogQueryRequest());

        result.TotalCount.Should().Be(0);
        result.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDistinctActionsAsync_WithNoEntries_ReturnsEmptyList()
    {
        var actions = await _sut.GetDistinctActionsAsync();
        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDistinctEntityTypesAsync_WithNoEntries_ReturnsEmptyList()
    {
        var types = await _sut.GetDistinctEntityTypesAsync();
        types.Should().BeEmpty();
    }
}
