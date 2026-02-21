using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ClientService> _logger;

    public ClientService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        ILogger<ClientService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<ClientDto> CreateAsync(ClientDto dto, string createdByUserId)
    {
        var entity = new Client
        {
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            Phone = dto.Phone,
            DateOfBirth = dto.DateOfBirth,
            PrimaryNutritionistId = dto.PrimaryNutritionistId,
            ConsentGiven = dto.ConsentGiven,
            ConsentTimestamp = dto.ConsentGiven ? DateTime.UtcNow : null,
            ConsentPolicyVersion = dto.ConsentPolicyVersion,
            Notes = dto.Notes,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Clients.Add(entity);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Client created: {ClientId} by {UserId}",
            entity.Id, createdByUserId);

        await _auditLogService.LogAsync(
            createdByUserId,
            "ClientCreated",
            "Client",
            entity.Id.ToString(),
            $"Created client {entity.FirstName} {entity.LastName}");

        return MapToDto(entity);
    }

    public async Task<ClientDto?> GetByIdAsync(int id)
    {
        var entity = await _dbContext.Clients.FindAsync(id);

        return entity is null ? null : MapToDto(entity);
    }

    public async Task<List<ClientDto>> GetListAsync(string? searchTerm = null)
    {
        var query = _dbContext.Clients.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(c =>
                c.FirstName.ToLower().Contains(term) ||
                c.LastName.ToLower().Contains(term) ||
                (c.Email != null && c.Email.ToLower().Contains(term)));
        }

        var entities = await query
            .OrderBy(c => c.LastName)
            .ThenBy(c => c.FirstName)
            .ToListAsync();

        return entities.Select(MapToDto).ToList();
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
        entity.ConsentGiven = dto.ConsentGiven;
        entity.ConsentTimestamp = dto.ConsentTimestamp;
        entity.ConsentPolicyVersion = dto.ConsentPolicyVersion;
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
            $"Updated client {entity.FirstName} {entity.LastName}");

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

        await _dbContext.SaveChangesAsync();

        _logger.LogInformation(
            "Client soft-deleted: {ClientId} by {UserId}",
            id, deletedByUserId);

        await _auditLogService.LogAsync(
            deletedByUserId,
            "ClientSoftDeleted",
            "Client",
            id.ToString(),
            $"Soft-deleted client {entity.FirstName} {entity.LastName}");

        return true;
    }

    private static ClientDto MapToDto(Client entity)
    {
        return new ClientDto(
            entity.Id,
            entity.FirstName,
            entity.LastName,
            entity.Email,
            entity.Phone,
            entity.DateOfBirth,
            entity.PrimaryNutritionistId,
            entity.ConsentGiven,
            entity.ConsentTimestamp,
            entity.ConsentPolicyVersion,
            entity.Notes,
            entity.IsDeleted,
            entity.CreatedAt,
            entity.UpdatedAt,
            entity.DeletedAt);
    }
}
