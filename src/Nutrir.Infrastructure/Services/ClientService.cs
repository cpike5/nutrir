using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly IConsentService _consentService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        IConsentService consentService,
        INotificationDispatcher notificationDispatcher,
        ILogger<ClientService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _consentService = consentService;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    public async Task<ClientDto> CreateAsync(ClientDto dto, string createdByUserId)
    {
        if (!dto.ConsentGiven)
        {
            throw new InvalidOperationException("Client consent must be obtained before creating a client record.");
        }

        var entity = new Client
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Phone = dto.Phone,
            DateOfBirth = dto.DateOfBirth,
            PrimaryNutritionistId = dto.PrimaryNutritionistId,
            ConsentGiven = false, // Will be set by ConsentService
            ConsentTimestamp = null,
            ConsentPolicyVersion = null,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Clients.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Create the initial consent event and update client consent fields atomically
        await _consentService.GrantConsentAsync(
            entity.Id,
            "Treatment and care",
            dto.ConsentPolicyVersion ?? "1.0",
            createdByUserId);

        _logger.LogInformation(
            "Client created: {ClientId} by {UserId}",
            entity.Id, createdByUserId);

        await _auditLogService.LogAsync(
            createdByUserId,
            "ClientCreated",
            "Client",
            entity.Id.ToString(),
            "Created client record");

        await TryDispatchAsync("Client", entity.Id, EntityChangeType.Created, entity.PrimaryNutritionistId);

        // Re-read entity to get updated consent fields
        await _dbContext.Entry(entity).ReloadAsync();
        return MapToDto(entity);
    }

    public async Task<ClientDto?> GetByIdAsync(int id)
    {
        var entity = await _dbContext.Clients.FindAsync(id);

        if (entity is null) return null;

        var nutritionistName = await GetNutritionistNameAsync(entity.PrimaryNutritionistId);
        return MapToDto(entity, nutritionistName);
    }

    public async Task<List<ClientDto>> GetListAsync(string? searchTerm = null)
    {
        var query = _dbContext.Clients.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var terms = searchTerm.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            foreach (var term in terms)
            {
                query = query.Where(c =>
                    c.FirstName.ToLower().Contains(term) ||
                    c.LastName.ToLower().Contains(term) ||
                    (c.Email != null && c.Email.ToLower().Contains(term)));
            }
        }

        var entities = await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync();

        var nutritionistIds = entities.Select(c => c.PrimaryNutritionistId).Distinct().ToList();
        var nutritionists = await _dbContext.Users
            .Where(u => nutritionistIds.Contains(u.Id))
            .OfType<ApplicationUser>()
            .ToDictionaryAsync(u => u.Id, u =>
                !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName : $"{u.FirstName} {u.LastName}".Trim());

        var clientIds = entities.Select(c => c.Id).ToList();
        var lastAppointments = await _dbContext.Appointments
            .Where(a => clientIds.Contains(a.ClientId) && !a.IsDeleted)
            .GroupBy(a => a.ClientId)
            .Select(g => new { ClientId = g.Key, LastDate = g.Max(a => a.StartTime) })
            .ToDictionaryAsync(x => x.ClientId, x => x.LastDate);

        return entities.Select(e => MapToDto(
            e,
            nutritionists.GetValueOrDefault(e.PrimaryNutritionistId),
            lastAppointments.GetValueOrDefault(e.Id))).ToList();
    }

    public async Task<bool> UpdateAsync(int id, ClientDto dto, string updatedByUserId)
    {
        var entity = await _dbContext.Clients.FindAsync(id);

        if (entity is null)
        {
            return false;
        }

        entity.FirstName = dto.FirstName;
        entity.LastName = dto.LastName;
        entity.Email = dto.Email;
        entity.Phone = dto.Phone;
        entity.DateOfBirth = dto.DateOfBirth;
        entity.PrimaryNutritionistId = dto.PrimaryNutritionistId;
        // Consent fields are immutable via UpdateAsync — use IConsentService instead
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Client updated: {ClientId} by {UserId}",
            id, updatedByUserId);

        await _auditLogService.LogAsync(
            updatedByUserId,
            "ClientUpdated",
            "Client",
            id.ToString(),
            "Updated client record");

        await TryDispatchAsync("Client", id, EntityChangeType.Updated, entity.PrimaryNutritionistId);

        return true;
    }

    public async Task<bool> SoftDeleteAsync(int id, string deletedByUserId)
    {
        var entity = await _dbContext.Clients.FindAsync(id);

        if (entity is null)
        {
            return false;
        }

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = deletedByUserId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Client soft-deleted: {ClientId} by {UserId}",
            id, deletedByUserId);

        await _auditLogService.LogAsync(
            deletedByUserId,
            "ClientSoftDeleted",
            "Client",
            id.ToString(),
            "Soft-deleted client record");

        await TryDispatchAsync("Client", id, EntityChangeType.Deleted, entity.PrimaryNutritionistId);

        return true;
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

    private async Task<string?> GetNutritionistNameAsync(string userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user is ApplicationUser appUser)
            return !string.IsNullOrEmpty(appUser.DisplayName)
                ? appUser.DisplayName
                : $"{appUser.FirstName} {appUser.LastName}".Trim();
        return null;
    }

    private static ClientDto MapToDto(Client entity, string? nutritionistName = null, DateTime? lastAppointmentDate = null)
    {
        return new ClientDto(
            entity.Id,
            entity.FirstName,
            entity.LastName,
            entity.Email,
            entity.Phone,
            entity.DateOfBirth,
            entity.PrimaryNutritionistId,
            nutritionistName,
            entity.ConsentGiven,
            entity.ConsentTimestamp,
            entity.ConsentPolicyVersion,
            entity.Notes,
            entity.IsDeleted,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.DeletedAt,
            lastAppointmentDate);
    }
}
