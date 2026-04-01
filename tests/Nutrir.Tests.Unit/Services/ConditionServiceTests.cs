using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// ConditionService tests. Methods that use EF.Functions.ILike (SearchAsync,
/// GetOrCreateAsync, GetByNameAsync) are PostgreSQL-specific and tested via
/// integration tests. This file tests methods that work with SQLite.
/// </summary>
public class ConditionServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly ConditionService _sut;

    private const string UserId = "test-user-cond-svc-001";

    public ConditionServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);
        _auditLogService = Substitute.For<IAuditLogService>();
        var logger = Substitute.For<ILogger<ConditionService>>();
        _sut = new ConditionService(_dbContextFactory, _auditLogService, logger);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // SearchAsync — guard clause
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        var result = await _sut.SearchAsync("");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchAsync_WithWhitespace_ReturnsEmptyList()
    {
        var result = await _sut.SearchAsync("  ");

        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsCondition()
    {
        var entity = new Condition { Name = "Asthma" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetByIdAsync(entity.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Asthma");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
    {
        var result = await _sut.GetByIdAsync(9999);

        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetAllAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsAllConditionsOrderedByName()
    {
        _dbContext.Conditions.Add(new Condition { Name = "Zollinger-Ellison" });
        _dbContext.Conditions.Add(new Condition { Name = "Anemia" });
        _dbContext.Conditions.Add(new Condition { Name = "Diabetes" });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetAllAsync();

        result.Should().HaveCount(3);
        result.Select(c => c.Name).Should().BeInAscendingOrder();
    }

    // ---------------------------------------------------------------------------
    // UpdateAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WhenExists_UpdatesFieldsAndReturnsTrue()
    {
        var entity = new Condition { Name = "Old Name", IcdCode = "X00", Category = "General" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.UpdateAsync(entity.Id, "New Name", "Y99", "Specific", UserId);

        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var updated = await _dbContext.Conditions.FindAsync(entity.Id);
        updated!.Name.Should().Be("New Name");
        updated.IcdCode.Should().Be("Y99");
        updated.Category.Should().Be("Specific");
    }

    [Fact]
    public async Task UpdateAsync_WhenNotFound_ReturnsFalse()
    {
        var result = await _sut.UpdateAsync(9999, "Name", null, null, UserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_LogsAudit()
    {
        var entity = new Condition { Name = "Asthma" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        await _sut.UpdateAsync(entity.Id, "Asthma (Severe)", null, null, UserId);

        await _auditLogService.Received(1).LogAsync(
            UserId, "ConditionLookupUpdated", "Condition", entity.Id.ToString(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateAsync_TrimsName()
    {
        var entity = new Condition { Name = "Original" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        await _sut.UpdateAsync(entity.Id, "  Trimmed  ", null, null, UserId);

        _dbContext.ChangeTracker.Clear();
        var updated = await _dbContext.Conditions.FindAsync(entity.Id);
        updated!.Name.Should().Be("Trimmed");
    }

    // ---------------------------------------------------------------------------
    // DeleteAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_WhenExists_SoftDeletesAndReturnsTrue()
    {
        var entity = new Condition { Name = "To Delete" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.DeleteAsync(entity.Id, UserId);

        result.Should().BeTrue();
        _dbContext.ChangeTracker.Clear();
        var deleted = _dbContext.Conditions.IgnoreQueryFilters().First(c => c.Id == entity.Id);
        deleted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAsync_WhenNotFound_ReturnsFalse()
    {
        var result = await _sut.DeleteAsync(9999, UserId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_LogsAudit()
    {
        var entity = new Condition { Name = "Audit Test" };
        _dbContext.Conditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        await _sut.DeleteAsync(entity.Id, UserId);

        await _auditLogService.Received(1).LogAsync(
            UserId, "ConditionLookupDeleted", "Condition", entity.Id.ToString(), Arg.Any<string>());
    }
}
