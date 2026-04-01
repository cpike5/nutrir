using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class FoodService : IFoodService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<FoodService> _logger;

    public FoodService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<FoodService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<List<FoodDto>> SearchAsync(string query, int limit = 15)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var pattern = $"%{query.Trim()}%";

        var results = await db.Foods
            .Where(f => EF.Functions.ILike(f.Name, pattern))
            .OrderBy(f => f.Name)
            .Take(limit)
            .Select(f => ToDto(f))
            .ToListAsync();

        return results;
    }

    public async Task<FoodDto?> GetByIdAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var food = await db.Foods
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id);

        return food is null ? null : ToDto(food);
    }

    public async Task<List<FoodDto>> GetAllAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var results = await db.Foods
            .AsNoTracking()
            .OrderBy(f => f.Name)
            .Select(f => ToDto(f))
            .ToListAsync();

        return results;
    }

    public async Task<FoodDto> CreateAsync(CreateFoodDto dto, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var food = new Food
        {
            Name = dto.Name,
            ServingSize = dto.ServingSize,
            ServingSizeUnit = dto.ServingSizeUnit,
            CaloriesKcal = dto.CaloriesKcal,
            ProteinG = dto.ProteinG,
            CarbsG = dto.CarbsG,
            FatG = dto.FatG,
            Tags = dto.Tags,
            Notes = dto.Notes
        };

        db.Foods.Add(food);
        await db.SaveChangesAsync();

        _logger.LogInformation("Food {FoodName} created with Id {FoodId} by user {UserId}",
            food.Name, food.Id, userId);

        return ToDto(food);
    }

    public async Task<bool> UpdateAsync(int id, UpdateFoodDto dto, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var food = await db.Foods.FirstOrDefaultAsync(f => f.Id == id);
        if (food is null)
            return false;

        food.Name = dto.Name;
        food.ServingSize = dto.ServingSize;
        food.ServingSizeUnit = dto.ServingSizeUnit;
        food.CaloriesKcal = dto.CaloriesKcal;
        food.ProteinG = dto.ProteinG;
        food.CarbsG = dto.CarbsG;
        food.FatG = dto.FatG;
        food.Tags = dto.Tags;
        food.Notes = dto.Notes;

        await db.SaveChangesAsync();

        _logger.LogInformation("Food {FoodId} updated by user {UserId}", id, userId);

        return true;
    }

    public async Task<bool> DeleteAsync(int id, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var food = await db.Foods.FirstOrDefaultAsync(f => f.Id == id);
        if (food is null)
            return false;

        food.IsDeleted = true;
        food.DeletedAt = DateTime.UtcNow;
        food.DeletedBy = userId;

        await db.SaveChangesAsync();

        _logger.LogInformation("Food {FoodId} soft-deleted by user {UserId}", id, userId);

        return true;
    }

    private static FoodDto ToDto(Food f) => new(
        f.Id,
        f.Name,
        f.ServingSize,
        f.ServingSizeUnit,
        f.CaloriesKcal,
        f.ProteinG,
        f.CarbsG,
        f.FatG,
        f.Tags,
        f.Notes);
}
