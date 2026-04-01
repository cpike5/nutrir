using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nutrir.Core.DTOs;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// Unit tests for <see cref="FoodService"/>.
///
/// Note: <see cref="FoodService.SearchAsync"/> uses <c>EF.Functions.ILike</c>, which is a
/// PostgreSQL-specific function. Tests for that method that rely on real database matching are
/// marked Skip because the in-memory SQLite provider does not support <c>ILike</c> and will
/// throw <see cref="InvalidOperationException"/>. The remaining behaviour (null/empty guard,
/// non-search CRUD) is verified against the shared SQLite test database.
/// </summary>
public class FoodServiceTests : IDisposable
{
    private readonly Nutrir.Infrastructure.Data.AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly FoodService _sut;

    private const string UserId = "user-food-test-001";

    public FoodServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _sut = new FoodService(
            _dbContextFactory,
            NullLogger<FoodService>.Instance);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private async Task<FoodDto> SeedFoodAsync(
        string name = "Chicken Breast",
        decimal servingSize = 100m,
        string servingSizeUnit = "g",
        decimal calories = 165m,
        decimal protein = 31m,
        decimal carbs = 0m,
        decimal fat = 3.6m,
        string[]? tags = null,
        string? notes = null)
    {
        return await _sut.CreateAsync(new CreateFoodDto(
            name,
            servingSize,
            servingSizeUnit,
            calories,
            protein,
            carbs,
            fat,
            tags ?? [],
            notes), UserId);
    }

    // ---------------------------------------------------------------------------
    // SearchAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithNullOrEmptyQuery_ReturnsEmptyList()
    {
        // Arrange — seed something so we know the DB has data
        await SeedFoodAsync("Chicken Breast");

        // Act
        var resultNull = await _sut.SearchAsync(null!);
        var resultEmpty = await _sut.SearchAsync(string.Empty);
        var resultWhitespace = await _sut.SearchAsync("   ");

        // Assert — guard clause fires before any DB call
        resultNull.Should().BeEmpty();
        resultEmpty.Should().BeEmpty();
        resultWhitespace.Should().BeEmpty();
    }

    [Fact(Skip = "EF.Functions.ILike is PostgreSQL-specific and not supported by the SQLite test provider.")]
    public async Task SearchAsync_WithMatchingQuery_ReturnsResults()
    {
        await SeedFoodAsync("Chicken Breast");
        var results = await _sut.SearchAsync("chicken");
        results.Should().NotBeEmpty();
        results.Should().Contain(f => f.Name == "Chicken Breast");
    }

    [Fact(Skip = "EF.Functions.ILike is PostgreSQL-specific and not supported by the SQLite test provider.")]
    public async Task SearchAsync_IsCaseInsensitive()
    {
        await SeedFoodAsync("Chicken Breast");
        var results = await _sut.SearchAsync("chicken");
        results.Should().Contain(f => f.Name == "Chicken Breast");
    }

    [Fact(Skip = "EF.Functions.ILike is PostgreSQL-specific and not supported by the SQLite test provider.")]
    public async Task SearchAsync_RespectsLimit()
    {
        for (var i = 1; i <= 5; i++)
            await SeedFoodAsync($"Food Item {i:D2}");

        var results = await _sut.SearchAsync("food", limit: 3);
        results.Should().HaveCount(3);
    }

    [Fact(Skip = "EF.Functions.ILike is PostgreSQL-specific and not supported by the SQLite test provider.")]
    public async Task SearchAsync_DoesNotReturnSoftDeletedFoods()
    {
        var food = await SeedFoodAsync("Deleted Food");
        await _sut.DeleteAsync(food.Id, UserId);

        var results = await _sut.SearchAsync("deleted");
        results.Should().NotContain(f => f.Id == food.Id);
    }

    // ---------------------------------------------------------------------------
    // CreateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_PersistsAllFields()
    {
        // Arrange
        var dto = new CreateFoodDto(
            Name: "Brown Rice",
            ServingSize: 185m,
            ServingSizeUnit: "g",
            CaloriesKcal: 216m,
            ProteinG: 5m,
            CarbsG: 45m,
            FatG: 1.8m,
            Tags: ["grain", "whole-grain"],
            Notes: "Cooked weight");

        // Act
        var result = await _sut.CreateAsync(dto, UserId);

        // Assert — returned DTO
        result.Id.Should().BeGreaterThan(0);
        result.Name.Should().Be("Brown Rice");
        result.ServingSize.Should().Be(185m);
        result.ServingSizeUnit.Should().Be("g");
        result.CaloriesKcal.Should().Be(216m);
        result.ProteinG.Should().Be(5m);
        result.CarbsG.Should().Be(45m);
        result.FatG.Should().Be(1.8m);
        result.Tags.Should().BeEquivalentTo(["grain", "whole-grain"]);
        result.Notes.Should().Be("Cooked weight");

        // Assert — round-trip via GetByIdAsync
        var fetched = await _sut.GetByIdAsync(result.Id);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("Brown Rice");
        fetched.Notes.Should().Be("Cooked weight");
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsDto()
    {
        // Arrange
        var created = await SeedFoodAsync("Salmon Fillet");

        // Act
        var result = await _sut.GetByIdAsync(created.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Name.Should().Be("Salmon Fillet");
    }

    [Fact]
    public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(99999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WhenSoftDeleted_ReturnsNull()
    {
        // Arrange
        var created = await SeedFoodAsync("Soon Deleted");
        await _sut.DeleteAsync(created.Id, UserId);

        // Act
        var result = await _sut.GetByIdAsync(created.Id);

        // Assert — global query filter excludes soft-deleted entities
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // UpdateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WhenExists_UpdatesFields()
    {
        // Arrange
        var created = await SeedFoodAsync("Old Name");

        var updateDto = new UpdateFoodDto(
            Name: "New Name",
            ServingSize: 200m,
            ServingSizeUnit: "ml",
            CaloriesKcal: 50m,
            ProteinG: 2m,
            CarbsG: 10m,
            FatG: 0.5m,
            Tags: ["updated"],
            Notes: "Updated notes");

        // Act
        var success = await _sut.UpdateAsync(created.Id, updateDto, UserId);

        // Assert
        success.Should().BeTrue();

        var updated = await _sut.GetByIdAsync(created.Id);
        updated.Should().NotBeNull();
        updated!.Name.Should().Be("New Name");
        updated.ServingSize.Should().Be(200m);
        updated.ServingSizeUnit.Should().Be("ml");
        updated.CaloriesKcal.Should().Be(50m);
        updated.Tags.Should().BeEquivalentTo(["updated"]);
        updated.Notes.Should().Be("Updated notes");
    }

    // ---------------------------------------------------------------------------
    // GetAllAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyNonDeletedFoods()
    {
        // Arrange
        var kept = await SeedFoodAsync("Kept Food");
        var deleted = await SeedFoodAsync("Deleted Food");
        await _sut.DeleteAsync(deleted.Id, UserId);

        // Act
        var results = await _sut.GetAllAsync();

        // Assert
        results.Should().Contain(f => f.Id == kept.Id);
        results.Should().NotContain(f => f.Id == deleted.Id);
    }

    // ---------------------------------------------------------------------------
    // DeleteAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_SetsIsDeletedAndDeletedAt()
    {
        // Arrange
        var created = await SeedFoodAsync("To Be Deleted");
        var beforeDelete = DateTime.UtcNow;

        // Act
        var success = await _sut.DeleteAsync(created.Id, UserId);

        // Assert — return value
        success.Should().BeTrue();

        // Assert — entity state via direct DbContext (bypasses soft-delete query filter)
        var entity = await _dbContext.Foods
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(f => f.Id == created.Id);

        entity.Should().NotBeNull();
        entity!.IsDeleted.Should().BeTrue();
        entity.DeletedAt.Should().NotBeNull();
        entity.DeletedAt!.Value.Should().BeOnOrAfter(beforeDelete);
        entity.DeletedBy.Should().Be(UserId);
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
