using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class FoodServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly FoodService _sut;

    private const string UserId = "test-user-food-001";

    public FoodServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);
        _auditLogService = Substitute.For<IAuditLogService>();

        _sut = new FoodService(
            _dbContextFactory,
            _auditLogService,
            NullLogger<FoodService>.Instance);

        SeedData();
    }

    private void SeedData()
    {
        _dbContext.Foods.AddRange(
            new Food
            {
                Name = "Chicken Breast (grilled)",
                ServingSize = 150m,
                ServingSizeUnit = "g",
                CaloriesKcal = 165m,
                ProteinG = 31m,
                CarbsG = 0m,
                FatG = 3.6m,
                Tags = new[] { "high-protein", "low-carb" },
                CreatedAt = DateTime.UtcNow
            },
            new Food
            {
                Name = "Salmon Fillet (baked)",
                ServingSize = 150m,
                ServingSizeUnit = "g",
                CaloriesKcal = 220m,
                ProteinG = 25m,
                CarbsG = 0m,
                FatG = 13m,
                Tags = new[] { "high-protein", "mediterranean" },
                Notes = "Rich in omega-3",
                CreatedAt = DateTime.UtcNow
            },
            new Food
            {
                Name = "Brown Rice (cooked)",
                ServingSize = 100m,
                ServingSizeUnit = "g",
                CaloriesKcal = 112m,
                ProteinG = 3m,
                CarbsG = 23m,
                FatG = 1m,
                Tags = new[] { "high-carb", "low-gi" },
                CreatedAt = DateTime.UtcNow
            },
            new Food
            {
                Name = "Deleted Food",
                ServingSize = 100m,
                ServingSizeUnit = "g",
                CaloriesKcal = 100m,
                ProteinG = 10m,
                CarbsG = 10m,
                FatG = 5m,
                Tags = Array.Empty<string>(),
                IsDeleted = true,
                DeletedAt = DateTime.UtcNow,
                DeletedBy = UserId,
                CreatedAt = DateTime.UtcNow
            }
        );
        _dbContext.SaveChanges();
    }

    // ---------------------------------------------------------------------------
    // Search
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_ReturnsMatchingResults_CaseInsensitive()
    {
        // SQLite uses LIKE instead of ILike, so we test via the service with a direct query
        // In SQLite, LIKE is case-insensitive by default for ASCII
        var results = await _sut.SearchAsync("chicken");
        results.Should().HaveCount(1);
        results[0].Name.Should().Be("Chicken Breast (grilled)");
    }

    [Fact]
    public async Task SearchAsync_ReturnsMultipleMatches()
    {
        var results = await _sut.SearchAsync("ed)");
        // "Salmon Fillet (baked)" and "Brown Rice (cooked)" both contain "ed)"
        results.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task SearchAsync_RespectsLimit()
    {
        var results = await _sut.SearchAsync("", limit: 2);
        results.Should().HaveCount(2);
    }

    [Fact]
    public async Task SearchAsync_ExcludesSoftDeleted()
    {
        var results = await _sut.SearchAsync("Deleted");
        results.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // GetById
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_ReturnsFood_WhenExists()
    {
        var all = await _sut.GetAllAsync();
        var first = all.First();

        var result = await _sut.GetByIdAsync(first.Id);

        result.Should().NotBeNull();
        result!.Name.Should().Be(first.Name);
        result.Tags.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _sut.GetByIdAsync(9999);
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetAll
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsNonDeletedFoods_OrderedByName()
    {
        var results = await _sut.GetAllAsync();

        results.Should().HaveCount(3);
        results.Select(f => f.Name).Should().BeInAscendingOrder();
        results.Should().NotContain(f => f.Name == "Deleted Food");
    }

    // ---------------------------------------------------------------------------
    // Create
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_AddsFoodAndReturnsDto()
    {
        var dto = new CreateFoodDto(
            "Test Food", 200m, "g", 300m, 20m, 30m, 10m,
            new[] { "test-tag" }, "Test notes");

        var result = await _sut.CreateAsync(dto, UserId);

        result.Name.Should().Be("Test Food");
        result.ServingSize.Should().Be(200m);
        result.CaloriesKcal.Should().Be(300m);
        result.Tags.Should().Contain("test-tag");
        result.Notes.Should().Be("Test notes");
        result.Id.Should().BeGreaterThan(0);

        await _auditLogService.Received(1).LogAsync(
            UserId, "FoodCreated", "Food", result.Id.ToString(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Update
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_UpdatesFoodAndReturnsTrue()
    {
        var all = await _sut.GetAllAsync();
        var target = all.First();

        var dto = new UpdateFoodDto(
            "Updated Name", 250m, "mL", 400m, 25m, 35m, 15m,
            new[] { "updated" }, "Updated notes");

        var success = await _sut.UpdateAsync(target.Id, dto, UserId);
        success.Should().BeTrue();

        var updated = await _sut.GetByIdAsync(target.Id);
        updated!.Name.Should().Be("Updated Name");
        updated.ServingSize.Should().Be(250m);
        updated.CaloriesKcal.Should().Be(400m);
    }

    [Fact]
    public async Task UpdateAsync_ReturnsFalse_WhenNotFound()
    {
        var dto = new UpdateFoodDto("x", 1m, "g", 1m, 1m, 1m, 1m, [], null);
        var result = await _sut.UpdateAsync(9999, dto, UserId);
        result.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // SoftDelete
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteAsync_MarksAsDeleted()
    {
        var created = await _sut.CreateAsync(
            new CreateFoodDto("To Delete", 100m, "g", 100m, 10m, 10m, 5m, [], null),
            UserId);

        var success = await _sut.SoftDeleteAsync(created.Id, UserId);
        success.Should().BeTrue();

        var result = await _sut.GetByIdAsync(created.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task SoftDeleteAsync_ReturnsFalse_WhenNotFound()
    {
        var result = await _sut.SoftDeleteAsync(9999, UserId);
        result.Should().BeFalse();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
