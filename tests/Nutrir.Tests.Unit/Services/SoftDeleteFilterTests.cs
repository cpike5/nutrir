using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class SoftDeleteFilterTests : IDisposable
{
    private readonly Nutrir.Infrastructure.Data.AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    // A seeded ApplicationUser is required to satisfy the FK on Client.PrimaryNutritionistId
    private const string NutritionistId = "soft-delete-nutritionist-001";

    public SoftDeleteFilterTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        SeedNutritionist();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedNutritionist()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "soft-delete@test.com",
            NormalizedUserName = "SOFT-DELETE@TEST.COM",
            Email = "soft-delete@test.com",
            NormalizedEmail = "SOFT-DELETE@TEST.COM",
            FirstName = "Soft",
            LastName = "Delete",
            DisplayName = "Soft Delete",
            CreatedDate = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.SaveChanges();
    }

    private Client MakeClient(string firstName, string lastName) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        PrimaryNutritionistId = NutritionistId,
        ConsentGiven = false,
        CreatedAt = DateTime.UtcNow
    };

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Query_ExcludesSoftDeleted()
    {
        // Arrange — seed 3 clients; soft-delete one of them
        var active1 = MakeClient("Alice", "Active");
        var active2 = MakeClient("Bob", "Active");
        var deleted = MakeClient("Charlie", "Deleted");

        _dbContext.Clients.AddRange(active1, active2, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.DeletedBy = NutritionistId;
        await _dbContext.SaveChangesAsync();

        // Act — standard query respects the HasQueryFilter
        var clients = await _dbContext.Clients.ToListAsync();

        // Assert
        clients.Should().HaveCount(2, because: "the soft-deleted client must be filtered out");
        clients.Should().NotContain(c => c.LastName == "Deleted");
    }

    [Fact]
    public async Task IgnoreQueryFilters_IncludesSoftDeleted()
    {
        // Arrange — seed 3 clients; soft-delete one of them
        var active1 = MakeClient("Diana", "Active");
        var active2 = MakeClient("Eve", "Active");
        var deleted = MakeClient("Frank", "Deleted");

        _dbContext.Clients.AddRange(active1, active2, deleted);
        await _dbContext.SaveChangesAsync();

        deleted.IsDeleted = true;
        deleted.DeletedAt = DateTime.UtcNow;
        deleted.DeletedBy = NutritionistId;
        await _dbContext.SaveChangesAsync();

        // Act — bypass the query filter explicitly
        var allClients = await _dbContext.Clients.IgnoreQueryFilters().ToListAsync();

        // Assert
        allClients.Should().HaveCount(3, because: "IgnoreQueryFilters must bypass the soft-delete filter");
        allClients.Should().Contain(c => c.LastName == "Deleted");
    }

    [Fact]
    public async Task SoftDelete_SetsFields()
    {
        // Arrange
        var client = MakeClient("Grace", "SoftDeleteFields");
        _dbContext.Clients.Add(client);
        await _dbContext.SaveChangesAsync();

        var deletedBy = NutritionistId;
        var deletedAt = DateTime.UtcNow;

        // Act — apply soft-delete fields manually (as a service would)
        client.IsDeleted = true;
        client.DeletedAt = deletedAt;
        client.DeletedBy = deletedBy;
        await _dbContext.SaveChangesAsync();

        // Re-load bypassing the query filter to inspect the persisted state
        var persisted = await _dbContext.Clients
            .IgnoreQueryFilters()
            .FirstAsync(c => c.Id == client.Id);

        // Assert
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedBy.Should().Be(deletedBy);
        persisted.DeletedAt.Should().BeCloseTo(deletedAt, precision: TimeSpan.FromSeconds(1));
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
