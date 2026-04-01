using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class RetentionTrackerTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;
    private readonly RetentionTracker _sut;

    private const string NutritionistId = "test-nutritionist-retention-001";

    public RetentionTrackerTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);
        var logger = Substitute.For<ILogger<RetentionTracker>>();
        _sut = new RetentionTracker(_dbContextFactory, logger);
        SeedNutritionist();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    private void SeedNutritionist()
    {
        _dbContext.Users.Add(new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@retention.test",
            NormalizedUserName = "NUTRITIONIST@RETENTION.TEST",
            Email = "nutritionist@retention.test",
            NormalizedEmail = "NUTRITIONIST@RETENTION.TEST",
            FirstName = "Test",
            LastName = "Nutritionist",
            DisplayName = "Test Nutritionist",
            CreatedDate = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private Client CreateClient(int retentionYears = 7)
    {
        var client = new Client
        {
            FirstName = "Test",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            RetentionYears = retentionYears,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();
        return client;
    }

    // ---------------------------------------------------------------------------
    // UpdateLastInteractionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateLastInteractionAsync_UpdatesLastInteractionDate()
    {
        var client = CreateClient();

        await _sut.UpdateLastInteractionAsync(client.Id);

        var updated = await _dbContext.Clients.FindAsync(client.Id);
        updated!.LastInteractionDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateLastInteractionAsync_RecalculatesRetentionExpiresAt()
    {
        var client = CreateClient(retentionYears: 5);

        await _sut.UpdateLastInteractionAsync(client.Id);

        var updated = await _dbContext.Clients.FindAsync(client.Id);
        updated!.RetentionExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddYears(5), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateLastInteractionAsync_UpdatesUpdatedAt()
    {
        var client = CreateClient();

        await _sut.UpdateLastInteractionAsync(client.Id);

        var updated = await _dbContext.Clients.FindAsync(client.Id);
        updated!.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task UpdateLastInteractionAsync_WithNonExistentClientId_DoesNotThrow()
    {
        var act = () => _sut.UpdateLastInteractionAsync(99999);

        await act.Should().NotThrowAsync();
    }
}
