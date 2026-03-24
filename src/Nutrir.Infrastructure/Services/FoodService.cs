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
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<FoodService> _logger;

    public FoodService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        ILogger<FoodService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<FoodSearchResultDto>> SearchAsync(string query, int limit = 15)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var lowerQuery = query.ToLower();
        var results = await db.Foods
            .Where(f => f.Name.ToLower().Contains(lowerQuery))
            .OrderBy(f => f.Name)
            .Take(limit)
            .Select(f => new FoodSearchResultDto(
                f.Id, f.Name, f.ServingSize, f.ServingSizeUnit,
                f.CaloriesKcal, f.ProteinG, f.CarbsG, f.FatG))
            .ToListAsync();

        return results;
    }

    public async Task<FoodDto?> GetByIdAsync(int id)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Foods.FirstOrDefaultAsync(f => f.Id == id);
        if (entity is null) return null;

        return MapToDto(entity);
    }

    public async Task<List<FoodDto>> GetAllAsync()
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        return await db.Foods
            .OrderBy(f => f.Name)
            .Select(f => new FoodDto(
                f.Id, f.Name, f.ServingSize, f.ServingSizeUnit,
                f.CaloriesKcal, f.ProteinG, f.CarbsG, f.FatG,
                f.Tags, f.Notes, f.CreatedAt))
            .ToListAsync();
    }

    public async Task<FoodDto> CreateAsync(CreateFoodDto dto, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = new Food
        {
            Name = dto.Name,
            ServingSize = dto.ServingSize,
            ServingSizeUnit = dto.ServingSizeUnit,
            CaloriesKcal = dto.CaloriesKcal,
            ProteinG = dto.ProteinG,
            CarbsG = dto.CarbsG,
            FatG = dto.FatG,
            Tags = dto.Tags,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        db.Foods.Add(entity);
        await db.SaveChangesAsync();

        _logger.LogInformation("Food created: {FoodId} '{FoodName}' by {UserId}", entity.Id, entity.Name, userId);

        await _auditLogService.LogAsync(
            userId,
            "FoodCreated",
            "Food",
            entity.Id.ToString(),
            $"Created food: {entity.Name}");

        return MapToDto(entity);
    }

    public async Task<bool> UpdateAsync(int id, UpdateFoodDto dto, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Foods.FirstOrDefaultAsync(f => f.Id == id);
        if (entity is null) return false;

        entity.Name = dto.Name;
        entity.ServingSize = dto.ServingSize;
        entity.ServingSizeUnit = dto.ServingSizeUnit;
        entity.CaloriesKcal = dto.CaloriesKcal;
        entity.ProteinG = dto.ProteinG;
        entity.CarbsG = dto.CarbsG;
        entity.FatG = dto.FatG;
        entity.Tags = dto.Tags;
        entity.Notes = dto.Notes;

        await db.SaveChangesAsync();

        _logger.LogInformation("Food updated: {FoodId} '{FoodName}' by {UserId}", id, entity.Name, userId);

        await _auditLogService.LogAsync(
            userId,
            "FoodUpdated",
            "Food",
            id.ToString(),
            $"Updated food: {entity.Name}");

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var entity = await db.Foods.FirstOrDefaultAsync(f => f.Id == id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await db.SaveChangesAsync();

        _logger.LogInformation("Food soft-deleted: {FoodId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "FoodSoftDeleted",
            "Food",
            id.ToString(),
            $"Soft-deleted food: {entity.Name}");

        return true;
    }

    private static FoodDto MapToDto(Food entity) => new(
        entity.Id, entity.Name, entity.ServingSize, entity.ServingSizeUnit,
        entity.CaloriesKcal, entity.ProteinG, entity.CarbsG, entity.FatG,
        entity.Tags, entity.Notes, entity.CreatedAt);
}
