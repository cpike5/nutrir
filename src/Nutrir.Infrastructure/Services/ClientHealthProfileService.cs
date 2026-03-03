using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ClientHealthProfileService : IClientHealthProfileService
{
    private readonly AppDbContext _dbContext;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ILogger<ClientHealthProfileService> _logger;

    public ClientHealthProfileService(
        AppDbContext dbContext,
        IDbContextFactory<AppDbContext> dbContextFactory,
        IAuditLogService auditLogService,
        INotificationDispatcher notificationDispatcher,
        ILogger<ClientHealthProfileService> logger)
    {
        _dbContext = dbContext;
        _dbContextFactory = dbContextFactory;
        _auditLogService = auditLogService;
        _notificationDispatcher = notificationDispatcher;
        _logger = logger;
    }

    // ========== Allergies ==========

    public async Task<ClientAllergyDto> CreateAllergyAsync(CreateClientAllergyDto dto, string userId)
    {
        await ValidateClientExistsAsync(dto.ClientId);

        var entity = new ClientAllergy
        {
            ClientId = dto.ClientId,
            Name = dto.Name,
            Severity = dto.Severity,
            AllergyType = dto.AllergyType,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Allergy created: {AllergyId} for client {ClientId} by {UserId}",
            entity.Id, entity.ClientId, userId);

        await _auditLogService.LogAsync(userId, "AllergyCreated", "ClientAllergy",
            entity.Id.ToString(), $"Created allergy '{entity.Name}' for client {entity.ClientId}");

        await TryDispatchAsync("ClientAllergy", entity.Id, EntityChangeType.Created, userId);

        return MapToDto(entity);
    }

    public async Task<ClientAllergyDto?> GetAllergyByIdAsync(int id)
    {
        var entity = await _dbContext.ClientAllergies.FindAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<List<ClientAllergyDto>> GetAllergiesByClientIdAsync(int clientId)
    {
        var entities = await _dbContext.ClientAllergies
            .Where(a => a.ClientId == clientId)
            .OrderBy(a => a.Name)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
    }

    public async Task<bool> UpdateAllergyAsync(int id, UpdateClientAllergyDto dto, string userId)
    {
        var entity = await _dbContext.ClientAllergies.FindAsync(id);
        if (entity is null) return false;

        entity.Name = dto.Name;
        entity.Severity = dto.Severity;
        entity.AllergyType = dto.AllergyType;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Allergy updated: {AllergyId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "AllergyUpdated", "ClientAllergy",
            id.ToString(), $"Updated allergy '{entity.Name}'");

        await TryDispatchAsync("ClientAllergy", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> DeleteAllergyAsync(int id, string userId)
    {
        var entity = await _dbContext.ClientAllergies.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Allergy soft-deleted: {AllergyId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "AllergyDeleted", "ClientAllergy",
            id.ToString(), $"Soft-deleted allergy '{entity.Name}'");

        await TryDispatchAsync("ClientAllergy", id, EntityChangeType.Deleted, userId);

        return true;
    }

    // ========== Medications ==========

    public async Task<ClientMedicationDto> CreateMedicationAsync(CreateClientMedicationDto dto, string userId)
    {
        await ValidateClientExistsAsync(dto.ClientId);

        var entity = new ClientMedication
        {
            ClientId = dto.ClientId,
            Name = dto.Name,
            Dosage = dto.Dosage,
            Frequency = dto.Frequency,
            PrescribedFor = dto.PrescribedFor,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Medication created: {MedicationId} for client {ClientId} by {UserId}",
            entity.Id, entity.ClientId, userId);

        await _auditLogService.LogAsync(userId, "MedicationCreated", "ClientMedication",
            entity.Id.ToString(), $"Created medication '{entity.Name}' for client {entity.ClientId}");

        await TryDispatchAsync("ClientMedication", entity.Id, EntityChangeType.Created, userId);

        return MapToDto(entity);
    }

    public async Task<ClientMedicationDto?> GetMedicationByIdAsync(int id)
    {
        var entity = await _dbContext.ClientMedications.FindAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<List<ClientMedicationDto>> GetMedicationsByClientIdAsync(int clientId)
    {
        var entities = await _dbContext.ClientMedications
            .Where(m => m.ClientId == clientId)
            .OrderBy(m => m.Name)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
    }

    public async Task<bool> UpdateMedicationAsync(int id, UpdateClientMedicationDto dto, string userId)
    {
        var entity = await _dbContext.ClientMedications.FindAsync(id);
        if (entity is null) return false;

        entity.Name = dto.Name;
        entity.Dosage = dto.Dosage;
        entity.Frequency = dto.Frequency;
        entity.PrescribedFor = dto.PrescribedFor;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Medication updated: {MedicationId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "MedicationUpdated", "ClientMedication",
            id.ToString(), $"Updated medication '{entity.Name}'");

        await TryDispatchAsync("ClientMedication", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> DeleteMedicationAsync(int id, string userId)
    {
        var entity = await _dbContext.ClientMedications.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Medication soft-deleted: {MedicationId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "MedicationDeleted", "ClientMedication",
            id.ToString(), $"Soft-deleted medication '{entity.Name}'");

        await TryDispatchAsync("ClientMedication", id, EntityChangeType.Deleted, userId);

        return true;
    }

    // ========== Conditions ==========

    public async Task<ClientConditionDto> CreateConditionAsync(CreateClientConditionDto dto, string userId)
    {
        await ValidateClientExistsAsync(dto.ClientId);

        var entity = new ClientCondition
        {
            ClientId = dto.ClientId,
            Name = dto.Name,
            Code = dto.Code,
            DiagnosisDate = dto.DiagnosisDate,
            Status = dto.Status,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Condition created: {ConditionId} for client {ClientId} by {UserId}",
            entity.Id, entity.ClientId, userId);

        await _auditLogService.LogAsync(userId, "ConditionCreated", "ClientCondition",
            entity.Id.ToString(), $"Created condition '{entity.Name}' for client {entity.ClientId}");

        await TryDispatchAsync("ClientCondition", entity.Id, EntityChangeType.Created, userId);

        return MapToDto(entity);
    }

    public async Task<ClientConditionDto?> GetConditionByIdAsync(int id)
    {
        var entity = await _dbContext.ClientConditions.FindAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<List<ClientConditionDto>> GetConditionsByClientIdAsync(int clientId)
    {
        var entities = await _dbContext.ClientConditions
            .Where(c => c.ClientId == clientId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
    }

    public async Task<bool> UpdateConditionAsync(int id, UpdateClientConditionDto dto, string userId)
    {
        var entity = await _dbContext.ClientConditions.FindAsync(id);
        if (entity is null) return false;

        entity.Name = dto.Name;
        entity.Code = dto.Code;
        entity.DiagnosisDate = dto.DiagnosisDate;
        entity.Status = dto.Status;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Condition updated: {ConditionId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "ConditionUpdated", "ClientCondition",
            id.ToString(), $"Updated condition '{entity.Name}'");

        await TryDispatchAsync("ClientCondition", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> DeleteConditionAsync(int id, string userId)
    {
        var entity = await _dbContext.ClientConditions.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Condition soft-deleted: {ConditionId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "ConditionDeleted", "ClientCondition",
            id.ToString(), $"Soft-deleted condition '{entity.Name}'");

        await TryDispatchAsync("ClientCondition", id, EntityChangeType.Deleted, userId);

        return true;
    }

    // ========== Dietary Restrictions ==========

    public async Task<ClientDietaryRestrictionDto> CreateDietaryRestrictionAsync(CreateClientDietaryRestrictionDto dto, string userId)
    {
        await ValidateClientExistsAsync(dto.ClientId);

        var entity = new ClientDietaryRestriction
        {
            ClientId = dto.ClientId,
            RestrictionType = dto.RestrictionType,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dietary restriction created: {RestrictionId} for client {ClientId} by {UserId}",
            entity.Id, entity.ClientId, userId);

        await _auditLogService.LogAsync(userId, "DietaryRestrictionCreated", "ClientDietaryRestriction",
            entity.Id.ToString(), $"Created dietary restriction '{entity.RestrictionType}' for client {entity.ClientId}");

        await TryDispatchAsync("ClientDietaryRestriction", entity.Id, EntityChangeType.Created, userId);

        return MapToDto(entity);
    }

    public async Task<ClientDietaryRestrictionDto?> GetDietaryRestrictionByIdAsync(int id)
    {
        var entity = await _dbContext.ClientDietaryRestrictions.FindAsync(id);
        return entity is null ? null : MapToDto(entity);
    }

    public async Task<List<ClientDietaryRestrictionDto>> GetDietaryRestrictionsByClientIdAsync(int clientId)
    {
        var entities = await _dbContext.ClientDietaryRestrictions
            .Where(d => d.ClientId == clientId)
            .OrderBy(d => d.RestrictionType)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
    }

    public async Task<bool> UpdateDietaryRestrictionAsync(int id, UpdateClientDietaryRestrictionDto dto, string userId)
    {
        var entity = await _dbContext.ClientDietaryRestrictions.FindAsync(id);
        if (entity is null) return false;

        entity.RestrictionType = dto.RestrictionType;
        entity.Notes = dto.Notes;
        entity.UpdatedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dietary restriction updated: {RestrictionId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "DietaryRestrictionUpdated", "ClientDietaryRestriction",
            id.ToString(), $"Updated dietary restriction '{entity.RestrictionType}'");

        await TryDispatchAsync("ClientDietaryRestriction", id, EntityChangeType.Updated, userId);

        return true;
    }

    public async Task<bool> DeleteDietaryRestrictionAsync(int id, string userId)
    {
        var entity = await _dbContext.ClientDietaryRestrictions.FindAsync(id);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.DeletedAt = DateTime.UtcNow;
        entity.DeletedBy = userId;

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Dietary restriction soft-deleted: {RestrictionId} by {UserId}", id, userId);

        await _auditLogService.LogAsync(userId, "DietaryRestrictionDeleted", "ClientDietaryRestriction",
            id.ToString(), $"Soft-deleted dietary restriction '{entity.RestrictionType}'");

        await TryDispatchAsync("ClientDietaryRestriction", id, EntityChangeType.Deleted, userId);

        return true;
    }

    // ========== Summary ==========

    public async Task<ClientHealthProfileSummaryDto> GetHealthProfileSummaryAsync(int clientId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var allergiesTask = db.ClientAllergies
            .Where(a => a.ClientId == clientId)
            .OrderBy(a => a.Name)
            .ToListAsync();

        var medicationsTask = db.ClientMedications
            .Where(m => m.ClientId == clientId)
            .OrderBy(m => m.Name)
            .ToListAsync();

        var conditionsTask = db.ClientConditions
            .Where(c => c.ClientId == clientId)
            .OrderBy(c => c.Name)
            .ToListAsync();

        var restrictionsTask = db.ClientDietaryRestrictions
            .Where(d => d.ClientId == clientId)
            .OrderBy(d => d.RestrictionType)
            .ToListAsync();

        await Task.WhenAll(allergiesTask, medicationsTask, conditionsTask, restrictionsTask);

        return new ClientHealthProfileSummaryDto(
            clientId,
            allergiesTask.Result.Select(MapToDto).ToList(),
            medicationsTask.Result.Select(MapToDto).ToList(),
            conditionsTask.Result.Select(MapToDto).ToList(),
            restrictionsTask.Result.Select(MapToDto).ToList());
    }

    // ========== Private Helpers ==========

    private async Task ValidateClientExistsAsync(int clientId)
    {
        var exists = await _dbContext.Clients.AnyAsync(c => c.Id == clientId);
        if (!exists)
            throw new InvalidOperationException($"Client with ID {clientId} does not exist.");
    }

    private async Task TryDispatchAsync(string entityType, int entityId, EntityChangeType changeType, string userId)
    {
        try
        {
            await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
                entityType, entityId, changeType, userId, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
                changeType, entityType, entityId);
        }
    }

    private static ClientAllergyDto MapToDto(ClientAllergy entity) => new(
        entity.Id, entity.ClientId, entity.Name, entity.Severity,
        entity.AllergyType, entity.CreatedAt, entity.UpdatedAt);

    private static ClientMedicationDto MapToDto(ClientMedication entity) => new(
        entity.Id, entity.ClientId, entity.Name, entity.Dosage,
        entity.Frequency, entity.PrescribedFor, entity.CreatedAt, entity.UpdatedAt);

    private static ClientConditionDto MapToDto(ClientCondition entity) => new(
        entity.Id, entity.ClientId, entity.Name, entity.Code,
        entity.DiagnosisDate, entity.Status, entity.Notes, entity.CreatedAt, entity.UpdatedAt);

    private static ClientDietaryRestrictionDto MapToDto(ClientDietaryRestriction entity) => new(
        entity.Id, entity.ClientId, entity.RestrictionType, entity.Notes,
        entity.CreatedAt, entity.UpdatedAt);
}
