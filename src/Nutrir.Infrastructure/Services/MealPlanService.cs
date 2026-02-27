using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class MealPlanService : IMealPlanService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<MealPlanService> _logger;

    public MealPlanService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        INotificationDispatcher notificationDispatcher,
        ILogger<MealPlanService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    public async Task<MealPlanDetailDto?> GetByIdAsync(int id)
    {
        var entity = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(mp => mp.Id == id);

        if (entity is null) return null;

        var client = await _dbContext.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == entity.ClientId);
        var createdByName = await GetUserNameAsync(entity.CreatedByUserId);

        return MapToDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<List<MealPlanSummaryDto>> GetListAsync(int? clientId = null, MealPlanStatus? status = null)
    {
        var query = _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .AsQueryable();

        if (clientId.HasValue)
            query = query.Where(mp => mp.ClientId == clientId.Value);

        if (status.HasValue)
            query = query.Where(mp => mp.Status == status.Value);

        var entities = await query
            .OrderByDescending(mp => mp.CreatedAt)
            .ToListAsync();

        return await MapToSummaryListAsync(entities);
    }

    public async Task<MealPlanDetailDto> CreateAsync(CreateMealPlanDto dto, string userId)
    {
        var entity = new MealPlan
        {
            ClientId = dto.ClientId,
            CreatedByUserId = userId,
            Title = dto.Title,
            Description = dto.Description,
            Status = MealPlanStatus.Draft,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            CalorieTarget = dto.CalorieTarget,
            ProteinTargetG = dto.ProteinTargetG,
            CarbsTargetG = dto.CarbsTargetG,
            FatTargetG = dto.FatTargetG,
            Notes = dto.Notes,
            Instructions = dto.Instructions,
            CreatedAt = DateTime.UtcNow
        };

        var dayCount = Math.Clamp(dto.NumberOfDays, 1, 7);
        for (var i = 1; i <= dayCount; i++)
        {
            entity.Days.Add(new MealPlanDay
            {
                DayNumber = i,
                Label = $"Day {i}"
            });
        }

        _dbContext.MealPlans.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Meal plan created: {MealPlanId} by {UserId}", entity.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanCreated",
            "MealPlan",
            entity.Id.ToString(),
            $"Created meal plan for client {entity.ClientId}");

        await TryDispatchAsync("MealPlan", entity.Id, EntityChangeType.Created, userId);

        var client = await _dbContext.Clients.FindAsync(entity.ClientId);
        var createdByName = await GetUserNameAsync(userId);

        return MapToDetailDto(entity, client?.FirstName ?? "", client?.LastName ?? "", createdByName);
    }

    public async Task<bool> UpdateMetadataAsync(int id, CreateMealPlanDto dto, string userId)
    {
        var entity = await _dbContext.MealPlans.FindAsync(id);
        if (entity is null) return false;

        entity.ClientId = dto.ClientId;
        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.StartDate = dto.StartDate;
        entity.EndDate = dto.EndDate;
        entity.CalorieTarget = dto.CalorieTarget;
        entity.ProteinTargetG = dto.ProteinTargetG;
        entity.CarbsTargetG = dto.CarbsTargetG;
        entity.FatTargetG = dto.FatTargetG;
        entity.Notes = dto.Notes;
        entity.Instructions = dto.Instructions;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Meal plan metadata updated: {MealPlanId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanUpdated",
            "MealPlan",
            id.ToString(),
            "Updated meal plan metadata");

        await TryDispatchAsync("MealPlan", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> SaveContentAsync(SaveMealPlanContentDto dto, string userId)
    {
        var entity = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(mp => mp.Id == dto.MealPlanId);

        if (entity is null) return false;

        // Remove existing children
        foreach (var day in entity.Days)
        {
            foreach (var slot in day.MealSlots)
            {
                _dbContext.MealItems.RemoveRange(slot.Items);
            }
            _dbContext.MealSlots.RemoveRange(day.MealSlots);
        }
        _dbContext.MealPlanDays.RemoveRange(entity.Days);

        // Add new children
        foreach (var dayDto in dto.Days)
        {
            var day = new MealPlanDay
            {
                MealPlanId = entity.Id,
                DayNumber = dayDto.DayNumber,
                Label = dayDto.Label,
                Notes = dayDto.Notes
            };

            foreach (var slotDto in dayDto.MealSlots)
            {
                var slot = new MealSlot
                {
                    MealType = slotDto.MealType,
                    CustomName = slotDto.CustomName,
                    SortOrder = slotDto.SortOrder,
                    Notes = slotDto.Notes
                };

                foreach (var itemDto in slotDto.Items)
                {
                    slot.Items.Add(new MealItem
                    {
                        FoodName = itemDto.FoodName,
                        Quantity = itemDto.Quantity,
                        Unit = itemDto.Unit,
                        CaloriesKcal = itemDto.CaloriesKcal,
                        ProteinG = itemDto.ProteinG,
                        CarbsG = itemDto.CarbsG,
                        FatG = itemDto.FatG,
                        Notes = itemDto.Notes,
                        SortOrder = itemDto.SortOrder
                    });
                }

                day.MealSlots.Add(slot);
            }

            entity.Days.Add(day);
        }

        entity.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Meal plan content saved: {MealPlanId} by {UserId}", dto.MealPlanId, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanContentSaved",
            "MealPlan",
            dto.MealPlanId.ToString(),
            "Saved content for meal plan");

        await TryDispatchAsync("MealPlan", dto.MealPlanId, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> UpdateStatusAsync(int id, MealPlanStatus newStatus, string userId)
    {
        var entity = await _dbContext.MealPlans.FindAsync(id);
        if (entity is null) return false;

        var oldStatus = entity.Status;
        entity.Status = newStatus;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Meal plan status changed: {MealPlanId} {OldStatus} -> {NewStatus} by {UserId}",
            id, oldStatus, newStatus, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanStatusChanged",
            "MealPlan",
            id.ToString(),
            $"Status changed from {oldStatus} to {newStatus}");

        await TryDispatchAsync("MealPlan", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> DuplicateAsync(int id, string userId)
    {
        var source = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstOrDefaultAsync(mp => mp.Id == id);

        if (source is null) return false;

        var copy = new MealPlan
        {
            ClientId = source.ClientId,
            CreatedByUserId = userId,
            Title = $"{source.Title} (Copy)",
            Description = source.Description,
            Status = MealPlanStatus.Draft,
            StartDate = source.StartDate,
            EndDate = source.EndDate,
            CalorieTarget = source.CalorieTarget,
            ProteinTargetG = source.ProteinTargetG,
            CarbsTargetG = source.CarbsTargetG,
            FatTargetG = source.FatTargetG,
            Notes = source.Notes,
            Instructions = source.Instructions,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var day in source.Days.OrderBy(d => d.DayNumber))
        {
            var dayClone = new MealPlanDay
            {
                DayNumber = day.DayNumber,
                Label = day.Label,
                Notes = day.Notes
            };

            foreach (var slot in day.MealSlots.OrderBy(s => s.SortOrder))
            {
                var slotClone = new MealSlot
                {
                    MealType = slot.MealType,
                    CustomName = slot.CustomName,
                    SortOrder = slot.SortOrder,
                    Notes = slot.Notes
                };

                foreach (var item in slot.Items.OrderBy(i => i.SortOrder))
                {
                    slotClone.Items.Add(new MealItem
                    {
                        FoodName = item.FoodName,
                        Quantity = item.Quantity,
                        Unit = item.Unit,
                        CaloriesKcal = item.CaloriesKcal,
                        ProteinG = item.ProteinG,
                        CarbsG = item.CarbsG,
                        FatG = item.FatG,
                        Notes = item.Notes,
                        SortOrder = item.SortOrder
                    });
                }

                dayClone.MealSlots.Add(slotClone);
            }

            copy.Days.Add(dayClone);
        }

        _dbContext.MealPlans.Add(copy);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Meal plan duplicated: {SourceId} -> {CopyId} by {UserId}", id, copy.Id, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanDuplicated",
            "MealPlan",
            copy.Id.ToString(),
            $"Duplicated meal plan from source {id}");

        await TryDispatchAsync("MealPlan", copy.Id, EntityChangeType.Created, userId);

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id, string userId)
    {
        var entity = await _dbContext.MealPlans.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Meal plan soft-deleted: {MealPlanId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(
            userId,
            "MealPlanSoftDeleted",
            "MealPlan",
            id.ToString(),
            "Soft-deleted meal plan");

        await TryDispatchAsync("MealPlan", id, EntityChangeType.Deleted, userId);

        return true;
    }

    public async Task<List<MealPlanSummaryDto>> GetByClientAsync(int clientId, int count = 5)
    {
        var entities = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .Where(mp => mp.ClientId == clientId)
            .OrderByDescending(mp => mp.CreatedAt)
            .Take(count)
            .ToListAsync();

        return await MapToSummaryListAsync(entities);
    }

    public async Task<int> GetActiveCountAsync()
    {
        return await _dbContext.MealPlans
            .CountAsync(mp => mp.Status == MealPlanStatus.Active);
    }

    private async Task TryDispatchAsync(string entityType, int entityId, EntityChangeType changeType, string practitionerUserId)
    {
        try
        {
            await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
                entityType, entityId, changeType, practitionerUserId, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
                changeType, entityType, entityId);
        }
    }

    private async Task<string?> GetUserNameAsync(string userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is ApplicationUser appUser)
            return !string.IsNullOrEmpty(appUser.DisplayName)
                ? appUser.DisplayName
                : $"{appUser.FirstName} {appUser.LastName}".Trim();
        return null;
    }

    private async Task<List<MealPlanSummaryDto>> MapToSummaryListAsync(List<MealPlan> entities)
    {
        if (entities.Count == 0) return [];

        var clientIds = entities.Select(mp => mp.ClientId).Distinct().ToList();
        var clients = await _dbContext.Clients
            .IgnoreQueryFilters()
            .Where(c => clientIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => new { c.FirstName, c.LastName });

        var userIds = entities.Select(mp => mp.CreatedByUserId).Distinct().ToList();
        var users = await _dbContext.Users
            .Where(u => userIds.Contains(u.Id))
            .OfType<ApplicationUser>()
            .ToDictionaryAsync(u => u.Id, u =>
                !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : $"{u.FirstName} {u.LastName}".Trim());

        return entities.Select(mp =>
        {
            var client = clients.GetValueOrDefault(mp.ClientId);
            var userName = users.GetValueOrDefault(mp.CreatedByUserId);
            var totalItems = mp.Days.SelectMany(d => d.MealSlots).SelectMany(s => s.Items).Count();

            return new MealPlanSummaryDto(
                mp.Id, mp.Title, mp.Status,
                mp.ClientId, client?.FirstName ?? "", client?.LastName ?? "",
                mp.CreatedByUserId, userName,
                mp.StartDate, mp.EndDate,
                mp.CalorieTarget, mp.ProteinTargetG,
                mp.CarbsTargetG, mp.FatTargetG,
                mp.Days.Count, totalItems,
                mp.CreatedAt, mp.UpdatedAt);
        }).ToList();
    }

    private static MealPlanDetailDto MapToDetailDto(
        MealPlan entity, string clientFirstName, string clientLastName, string? createdByName)
    {
        var days = entity.Days
            .OrderBy(d => d.DayNumber)
            .Select(d =>
            {
                var slots = d.MealSlots
                    .OrderBy(s => s.SortOrder)
                    .Select(s =>
                    {
                        var items = s.Items
                            .OrderBy(i => i.SortOrder)
                            .Select(i => new MealItemDto(
                                i.Id, i.FoodName, i.Quantity, i.Unit,
                                i.CaloriesKcal, i.ProteinG, i.CarbsG, i.FatG,
                                i.Notes, i.SortOrder))
                            .ToList();

                        return new MealSlotDto(
                            s.Id, s.MealType, s.CustomName, s.SortOrder, s.Notes,
                            items,
                            items.Sum(i => i.CaloriesKcal),
                            items.Sum(i => i.ProteinG),
                            items.Sum(i => i.CarbsG),
                            items.Sum(i => i.FatG));
                    })
                    .ToList();

                return new MealPlanDayDto(
                    d.Id, d.DayNumber, d.Label, d.Notes,
                    slots,
                    slots.Sum(s => s.TotalCalories),
                    slots.Sum(s => s.TotalProtein),
                    slots.Sum(s => s.TotalCarbs),
                    slots.Sum(s => s.TotalFat));
            })
            .ToList();

        return new MealPlanDetailDto(
            entity.Id, entity.Title, entity.Description, entity.Status,
            entity.ClientId, clientFirstName, clientLastName,
            entity.CreatedByUserId, createdByName,
            entity.StartDate, entity.EndDate,
            entity.CalorieTarget, entity.ProteinTargetG,
            entity.CarbsTargetG, entity.FatTargetG,
            entity.Notes, entity.Instructions,
            days,
            entity.CreatedAt, entity.UpdatedAt);
    }
}
