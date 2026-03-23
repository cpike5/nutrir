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

/// <summary>
/// Minimal IDbContextFactory implementation that always returns the same pre-created context.
/// This avoids NSubstitute ValueTask return-type inference issues.
/// </summary>
file sealed class FixedClientContextFactory(AppDbContext context) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => context;
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(context);
}

public class ClientServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IConsentService _consentService;
    private readonly INotificationDispatcher _notificationDispatcher;

    private readonly ClientService _sut;

    private const string NutritionistId = "nutritionist-client-test-001";
    private const string UserId = "acting-user-001";

    public ClientServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _consentService = Substitute.For<IConsentService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();

        // FixedClientContextFactory always returns the same shared test context without
        // triggering NSubstitute/ValueTask return-type inference issues.
        var dbContextFactory = new FixedClientContextFactory(_dbContext);

        _sut = new ClientService(
            _dbContext,
            dbContextFactory,
            _auditLogService,
            _consentService,
            _notificationDispatcher,
            NullLogger<ClientService>.Instance);

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
            UserName = "nutritionist@clienttest.com",
            NormalizedUserName = "NUTRITIONIST@CLIENTTEST.COM",
            Email = "nutritionist@clienttest.com",
            NormalizedEmail = "NUTRITIONIST@CLIENTTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.SaveChanges();
    }

    private Client MakeClient(
        string firstName,
        string lastName,
        string? email = null,
        bool consentGiven = true,
        bool isDeleted = false) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        PrimaryNutritionistId = NutritionistId,
        ConsentGiven = consentGiven,
        ConsentTimestamp = consentGiven ? DateTime.UtcNow : null,
        CreatedAt = DateTime.UtcNow,
        IsDeleted = isDeleted,
        DeletedAt = isDeleted ? DateTime.UtcNow : null,
        DeletedBy = isDeleted ? UserId : null
    };

    private ClientDto BuildCreateDto(
        bool consentGiven = true,
        string? consentPolicyVersion = null) => new(
            Id: 0,
            FirstName: "New",
            LastName: "Client",
            Email: "new.client@example.com",
            Phone: "555-0100",
            DateOfBirth: new DateOnly(1990, 5, 15),
            PrimaryNutritionistId: NutritionistId,
            PrimaryNutritionistName: null,
            ConsentGiven: consentGiven,
            ConsentTimestamp: consentGiven ? DateTime.UtcNow : null,
            ConsentPolicyVersion: consentPolicyVersion,
            Notes: "Initial notes",
            IsDeleted: false,
            CreatedAt: default,
            UpdatedAt: null,
            DeletedAt: null);

    private ClientDto BuildUpdateDto(int id, string firstName = "Updated", string lastName = "Name") => new(
        Id: id,
        FirstName: firstName,
        LastName: lastName,
        Email: "updated@example.com",
        Phone: "555-0200",
        DateOfBirth: new DateOnly(1985, 3, 10),
        PrimaryNutritionistId: NutritionistId,
        PrimaryNutritionistName: null,
        ConsentGiven: true,
        ConsentTimestamp: DateTime.UtcNow,
        ConsentPolicyVersion: "1.0",
        Notes: "Updated notes",
        IsDeleted: false,
        CreatedAt: default,
        UpdatedAt: null,
        DeletedAt: null,
        EmailRemindersEnabled: true);

    // ---------------------------------------------------------------------------
    // CreateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsClientDto()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true);

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        result.Should().NotBeNull();
        result.FirstName.Should().Be(dto.FirstName);
        result.LastName.Should().Be(dto.LastName);
        result.Email.Should().Be(dto.Email);
        result.PrimaryNutritionistId.Should().Be(NutritionistId);
        result.Id.Should().BeGreaterThan(0, because: "the database should assign a positive identity value");
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_PersistsClientToDatabase()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true);

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == result.Id);
        persisted.Should().NotBeNull();
        persisted!.FirstName.Should().Be(dto.FirstName);
        persisted.LastName.Should().Be(dto.LastName);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_SetsCreatedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var dto = BuildCreateDto(consentGiven: true);

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == result.Id);
        persisted.CreatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_CallsGrantConsentAsync()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true, consentPolicyVersion: "2.0");

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        await _consentService.Received(1).GrantConsentAsync(
            result.Id,
            "Treatment and care",
            "2.0",
            UserId);
    }

    [Fact]
    public async Task CreateAsync_WithNullConsentPolicyVersion_DefaultsToVersion1()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true, consentPolicyVersion: null);

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        await _consentService.Received(1).GrantConsentAsync(
            result.Id,
            "Treatment and care",
            "1.0",
            UserId);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_CallsAuditLog()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true);

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ClientCreated",
            "Client",
            result.Id.ToString(),
            "Created client record");
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_DispatchesNotification()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: true);

        // Act
        await _sut.CreateAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "Client" &&
            n.ChangeType == EntityChangeType.Created));
    }

    [Fact]
    public async Task CreateAsync_WithConsentNotGiven_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: false);

        // Act
        var act = () => _sut.CreateAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*consent*");
    }

    [Fact]
    public async Task CreateAsync_WithConsentNotGiven_DoesNotPersistClient()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: false);
        var countBefore = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();

        // Act
        try { await _sut.CreateAsync(dto, UserId); } catch (InvalidOperationException) { }

        // Assert
        var countAfter = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();
        countAfter.Should().Be(countBefore, because: "no client should be saved when consent is not given");
    }

    [Fact]
    public async Task CreateAsync_WithConsentNotGiven_DoesNotCallAuditLog()
    {
        // Arrange
        var dto = BuildCreateDto(consentGiven: false);

        // Act
        try { await _sut.CreateAsync(dto, UserId); } catch (InvalidOperationException) { }

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WithExistingId_ReturnsClientDto()
    {
        // Arrange
        var client = MakeClient("Alice", "Existing", email: "alice@example.com");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(client.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(client.Id);
        result.FirstName.Should().Be("Alice");
        result.LastName.Should().Be("Existing");
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingId_IncludesNutritionistName()
    {
        // Arrange
        var client = MakeClient("Bob", "WithNutritionist");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(client.Id);

        // Assert
        result.Should().NotBeNull();
        result!.PrimaryNutritionistName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const int nonExistentId = 999_999;

        // Act
        var result = await _sut.GetByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithSoftDeletedClient_ReturnsDto()
    {
        // Arrange — FindAsync does NOT apply the global query filter, so soft-deleted
        // clients are still returned by GetByIdAsync.
        var client = MakeClient("Charlie", "SoftDeleted", isDeleted: false);
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        client.IsDeleted = true;
        client.DeletedAt = DateTime.UtcNow;
        client.DeletedBy = UserId;
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(client.Id);

        // Assert — FindAsync bypasses the query filter; the DTO is returned with IsDeleted = true
        result.Should().NotBeNull(because: "FindAsync does not apply the global soft-delete query filter");
        result!.IsDeleted.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // GetListAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_WithNoSearchTerm_ReturnsAllActiveClients()
    {
        // Arrange
        var client1 = MakeClient("Diana", "Active");
        var client2 = MakeClient("Eve", "Active");
        _dbContext.Clients.AddRange(client1, client2);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync();

        // Assert
        results.Should().HaveCountGreaterThanOrEqualTo(2);
        results.Should().Contain(r => r.FirstName == "Diana");
        results.Should().Contain(r => r.FirstName == "Eve");
    }

    [Fact]
    public async Task GetListAsync_WithNoSearchTerm_ExcludesSoftDeletedClients()
    {
        // Arrange
        var active = MakeClient("Frank", "Active");
        var deleted = MakeClient("Grace", "Deleted");
        _dbContext.Clients.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        // Soft-delete Grace after saving so the query filter applies on next query
        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.DeletedBy = UserId;
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync();

        // Assert
        results.Should().NotContain(r => r.FirstName == "Grace",
            because: "the global query filter must exclude soft-deleted clients from LINQ queries");
    }

    [Fact]
    public async Task GetListAsync_WithSearchTerm_FiltersOnFirstName()
    {
        // Arrange
        var match = MakeClient("Henry", "FilterTest", email: "henry@example.com");
        var noMatch = MakeClient("Zara", "FilterTest", email: "zara@example.com");
        _dbContext.Clients.AddRange(match, noMatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync("henry");

        // Assert
        results.Should().Contain(r => r.FirstName == "Henry");
        results.Should().NotContain(r => r.FirstName == "Zara");
    }

    [Fact]
    public async Task GetListAsync_WithSearchTerm_FiltersOnLastName()
    {
        // Arrange
        var match = MakeClient("Irene", "Unique-LastName");
        var noMatch = MakeClient("Jack", "Different");
        _dbContext.Clients.AddRange(match, noMatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync("Unique-LastName");

        // Assert
        results.Should().Contain(r => r.LastName == "Unique-LastName");
        results.Should().NotContain(r => r.LastName == "Different");
    }

    [Fact]
    public async Task GetListAsync_WithSearchTerm_FiltersOnEmail()
    {
        // Arrange
        var match = MakeClient("Karen", "EmailMatch", email: "uniqueemail@example.com");
        var noMatch = MakeClient("Leo", "NoEmailMatch", email: "other@example.com");
        _dbContext.Clients.AddRange(match, noMatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync("uniqueemail");

        // Assert
        results.Should().Contain(r => r.Email == "uniqueemail@example.com");
        results.Should().NotContain(r => r.Email == "other@example.com");
    }

    [Fact]
    public async Task GetListAsync_WithSearchTerm_IsCaseInsensitive()
    {
        // Arrange
        var client = MakeClient("Mia", "CaseSearch");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync("MIA");

        // Assert
        results.Should().Contain(r => r.FirstName == "Mia",
            because: "search filtering must be case-insensitive");
    }

    [Fact]
    public async Task GetListAsync_WithMultiWordSearchTerm_FiltersOnAllTerms()
    {
        // Arrange — "Noah Anderson" matches both "noah" and "anderson"
        var match = MakeClient("Noah", "Anderson");
        var partialMatch = MakeClient("Noah", "Johnson"); // Matches "noah" but not "anderson"
        _dbContext.Clients.AddRange(match, partialMatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync("noah anderson");

        // Assert
        results.Should().Contain(r => r.FirstName == "Noah" && r.LastName == "Anderson");
        results.Should().NotContain(r => r.LastName == "Johnson");
    }

    [Fact]
    public async Task GetListAsync_WithNoSearchTerm_IsOrderedByLastNameThenFirstName()
    {
        // Arrange
        var c1 = MakeClient("Zach", "Abbott");
        var c2 = MakeClient("Anna", "Abbott");
        var c3 = MakeClient("Peter", "Zee");
        _dbContext.Clients.AddRange(c1, c2, c3);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetListAsync();

        // Assert — results from this test's clients should be in LastName, FirstName order
        var testResults = results.Where(r => r.LastName is "Abbott" or "Zee").ToList();
        testResults.Should().HaveCount(3);
        testResults[0].LastName.Should().Be("Abbott");
        testResults[0].FirstName.Should().Be("Anna");
        testResults[1].LastName.Should().Be("Abbott");
        testResults[1].FirstName.Should().Be("Zach");
        testResults[2].LastName.Should().Be("Zee");
    }

    // ---------------------------------------------------------------------------
    // GetPagedAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_WithDefaultQuery_ReturnsTotalCount()
    {
        // Arrange
        var client1 = MakeClient("Oliver", "Paged");
        var client2 = MakeClient("Paula", "Paged");
        _dbContext.Clients.AddRange(client1, client2);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery();

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.TotalCount.Should().BeGreaterThanOrEqualTo(2);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(25);
    }

    [Fact]
    public async Task GetPagedAsync_WithPageSizeOne_ReturnsOneItem()
    {
        // Arrange
        var client1 = MakeClient("Quinn", "PageSize");
        var client2 = MakeClient("Rachel", "PageSize");
        _dbContext.Clients.AddRange(client1, client2);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(Page: 1, PageSize: 1);

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().HaveCount(1);
        result.TotalCount.Should().BeGreaterThanOrEqualTo(2,
            because: "TotalCount reflects all matching records, not just the page");
    }

    [Fact]
    public async Task GetPagedAsync_SecondPage_ReturnsNextSet()
    {
        // Arrange — seed exactly 3 clients with a distinctive last name so we can isolate them
        var a = MakeClient("Sam", "Paginate");
        var b = MakeClient("Tina", "Paginate");
        var c = MakeClient("Uma", "Paginate");
        _dbContext.Clients.AddRange(a, b, c);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(Page: 2, PageSize: 2, SearchTerm: "Paginate");

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().HaveCount(1, because: "only one item should remain on page 2 of 3");
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetPagedAsync_WithSearchTerm_FiltersResults()
    {
        // Arrange
        var match = MakeClient("Victor", "SearchPaged", email: "v@example.com");
        var noMatch = MakeClient("Wendy", "NoPaged", email: "w@example.com");
        _dbContext.Clients.AddRange(match, noMatch);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(SearchTerm: "SearchPaged");

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().OnlyContain(r => r.LastName == "SearchPaged",
            because: "search filter must apply to paged results");
        result.TotalCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetPagedAsync_WithConsentFilterGiven_ReturnsOnlyConsentedClients()
    {
        // Arrange
        var consented = MakeClient("Xena", "ConsentGiven", consentGiven: true);
        var pending = MakeClient("Yusuf", "ConsentPending", consentGiven: false);
        _dbContext.Clients.AddRange(consented, pending);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(ConsentFilter: "Given");

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().OnlyContain(r => r.ConsentGiven,
            because: "the 'Given' consent filter must exclude clients without consent");
    }

    [Fact]
    public async Task GetPagedAsync_WithConsentFilterPending_ReturnsOnlyPendingClients()
    {
        // Arrange
        var consented = MakeClient("Zoe", "FilterPending", consentGiven: true);
        var pending = MakeClient("Aaron", "FilterPending", consentGiven: false);
        _dbContext.Clients.AddRange(consented, pending);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(ConsentFilter: "Pending");

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().OnlyContain(r => !r.ConsentGiven,
            because: "the 'Pending' consent filter must exclude clients with consent");
    }

    [Fact]
    public async Task GetPagedAsync_WithSortByEmail_ReturnsResultsInEmailOrder()
    {
        // Arrange — distinctive search term to isolate these test clients
        var a = MakeClient("Beth", "EmailSort", email: "aaa@example.com");
        var b = MakeClient("Carl", "EmailSort", email: "zzz@example.com");
        _dbContext.Clients.AddRange(a, b);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(
            SearchTerm: "EmailSort",
            SortColumn: "email",
            SortDirection: SortDirection.Ascending);

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        var emails = result.Items.Select(r => r.Email).ToList();
        emails.Should().BeInAscendingOrder(because: "results should be sorted by email ascending");
    }

    [Fact]
    public async Task GetPagedAsync_WithSortByNameDescending_ReturnsResultsInReverseNameOrder()
    {
        // Arrange
        var a = MakeClient("Anna", "NameSort");
        var b = MakeClient("Zack", "NameSort");
        _dbContext.Clients.AddRange(a, b);
        await _dbContext.SaveChangesAsync();

        var query = new ClientListQuery(
            SearchTerm: "NameSort",
            SortColumn: "name",
            SortDirection: SortDirection.Descending);

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Items.Should().HaveCountGreaterThanOrEqualTo(2);
        var lastNames = result.Items.Select(r => r.LastName).ToList();
        lastNames.Should().BeInDescendingOrder(
            because: "results should be sorted by last name descending");
    }

    [Fact]
    public async Task GetPagedAsync_PageNormalizesToOneWhenZeroOrNegative()
    {
        // Arrange
        var query = new ClientListQuery(Page: 0, PageSize: 10);

        // Act
        var result = await _sut.GetPagedAsync(query);

        // Assert
        result.Page.Should().Be(1, because: "GetPagedAsync normalizes page values below 1 to 1");
    }

    // ---------------------------------------------------------------------------
    // UpdateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WithExistingId_ReturnsTrueAndPersistsChanges()
    {
        // Arrange
        var client = MakeClient("Dave", "Original");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var updateDto = BuildUpdateDto(client.Id, firstName: "Dave", lastName: "Updated");

        // Act
        var result = await _sut.UpdateAsync(client.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == client.Id);
        persisted.LastName.Should().Be("Updated");
    }

    [Fact]
    public async Task UpdateAsync_WithExistingId_SetsUpdatedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var client = MakeClient("Eve", "UpdatedAt");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var updateDto = BuildUpdateDto(client.Id);

        // Act
        await _sut.UpdateAsync(client.Id, updateDto, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == client.Id);
        persisted.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var client = MakeClient("Frank", "AuditUpdate");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var updateDto = BuildUpdateDto(client.Id);

        // Act
        await _sut.UpdateAsync(client.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ClientUpdated",
            "Client",
            client.Id.ToString(),
            "Updated client record");
    }

    [Fact]
    public async Task UpdateAsync_WithExistingId_DispatchesNotification()
    {
        // Arrange
        var client = MakeClient("Grace", "DispatchUpdate");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var updateDto = BuildUpdateDto(client.Id);

        // Act
        await _sut.UpdateAsync(client.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "Client" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.EntityId == client.Id));
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_998;
        var updateDto = BuildUpdateDto(nonExistentId);

        // Act
        var result = await _sut.UpdateAsync(nonExistentId, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_997;
        var updateDto = BuildUpdateDto(nonExistentId);

        // Act
        await _sut.UpdateAsync(nonExistentId, updateDto, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // SoftDeleteAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteAsync_WithExistingId_ReturnsTrueAndSetsIsDeleted()
    {
        // Arrange
        var client = MakeClient("Heidi", "SoftDel");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SoftDeleteAsync(client.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == client.Id);
        persisted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingId_SetsDeletedAtAndDeletedBy()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var client = MakeClient("Ivan", "DeleteFields");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SoftDeleteAsync(client.Id, UserId);

        // Assert
        var persisted = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == client.Id);
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeAfter(before);
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var client = MakeClient("Julia", "AuditDelete");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SoftDeleteAsync(client.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ClientSoftDeleted",
            "Client",
            client.Id.ToString(),
            "Soft-deleted client record");
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingId_DispatchesDeletedNotification()
    {
        // Arrange
        var client = MakeClient("Karl", "DispatchDelete");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SoftDeleteAsync(client.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "Client" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.EntityId == client.Id));
    }

    [Fact]
    public async Task SoftDeleteAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_996;

        // Act
        var result = await _sut.SoftDeleteAsync(nonExistentId, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_995;

        // Act
        await _sut.SoftDeleteAsync(nonExistentId, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task SoftDeleteAsync_DeletedClientIsExcludedFromSubsequentListQuery()
    {
        // Arrange
        var client = MakeClient("Laura", "ExcludeAfterDelete");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.SoftDeleteAsync(client.Id, UserId);
        var listResults = await _sut.GetListAsync();

        // Assert
        listResults.Should().NotContain(r => r.Id == client.Id,
            because: "soft-deleted clients must be excluded from GetListAsync results via the global query filter");
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
