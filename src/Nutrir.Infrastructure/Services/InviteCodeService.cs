using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class InviteCodeService : IInviteCodeService
{
    private readonly AppDbContext _dbContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<InviteCodeService> _logger;

    private static readonly char[] UpperLetters = "ABCDEFGHJKLMNPQRSTUVWXYZ".ToCharArray();

    public InviteCodeService(
        AppDbContext dbContext,
        IAuditLogService auditLogService,
        ILogger<InviteCodeService> logger)
    {
        _dbContext = dbContext;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<InviteCodeListItemDto> GenerateAsync(string createdByUserId, string targetRole, int expirationDays = 7)
    {
        var code = GenerateCode();

        var inviteCode = new InviteCode
        {
            Code = code,
            TargetRole = targetRole,
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow,
            CreatedById = createdByUserId
        };

        _dbContext.InviteCodes.Add(inviteCode);
        await _dbContext.SaveChangesAsync();

        await _auditLogService.LogAsync(
            createdByUserId,
            "InviteCode.Generated",
            "InviteCode",
            inviteCode.Id.ToString(),
            $"Code generated for role '{targetRole}', expires {inviteCode.ExpiresAt:u}");

        _logger.LogInformation(
            "Invite code {InviteCodeId} generated for role {TargetRole} by user {UserId}",
            inviteCode.Id, targetRole, createdByUserId);

        var createdBy = await _dbContext.Users
            .Where(u => u.Id == createdByUserId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync() ?? string.Empty;

        return new InviteCodeListItemDto(
            inviteCode.Id,
            inviteCode.Code,
            inviteCode.TargetRole,
            inviteCode.ExpiresAt,
            inviteCode.IsUsed,
            inviteCode.CreatedAt,
            createdBy,
            RedeemedByName: null,
            RedeemedAt: null);
    }

    public async Task<InviteCodeValidationResult> ValidateAsync(string code)
    {
        var inviteCode = await _dbContext.InviteCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(ic => ic.Code == code);

        if (inviteCode is null)
        {
            return new InviteCodeValidationResult(false, InviteCodeValidationStatus.NotFound);
        }

        if (inviteCode.IsUsed)
        {
            return new InviteCodeValidationResult(false, InviteCodeValidationStatus.AlreadyUsed);
        }

        if (inviteCode.ExpiresAt < DateTime.UtcNow)
        {
            return new InviteCodeValidationResult(false, InviteCodeValidationStatus.Expired);
        }

        return new InviteCodeValidationResult(true, InviteCodeValidationStatus.Valid, inviteCode.TargetRole);
    }

    public async Task RedeemAsync(string code, string userId)
    {
        var inviteCode = await _dbContext.InviteCodes
            .FirstOrDefaultAsync(ic => ic.Code == code);

        if (inviteCode is null)
        {
            throw new InvalidOperationException($"Invite code not found.");
        }

        if (inviteCode.IsUsed)
        {
            throw new InvalidOperationException("Invite code has already been used.");
        }

        if (inviteCode.ExpiresAt < DateTime.UtcNow)
        {
            throw new InvalidOperationException("Invite code has expired.");
        }

        inviteCode.IsUsed = true;
        inviteCode.RedeemedById = userId;
        inviteCode.RedeemedAt = DateTime.UtcNow;

        await _dbContext.SaveChangesAsync();

        await _auditLogService.LogAsync(
            userId,
            "InviteCode.Redeemed",
            "InviteCode",
            inviteCode.Id.ToString(),
            $"Code redeemed for role '{inviteCode.TargetRole}'");

        _logger.LogInformation(
            "Invite code {InviteCodeId} redeemed by user {UserId} for role {TargetRole}",
            inviteCode.Id, userId, inviteCode.TargetRole);
    }

    public async Task<List<InviteCodeListItemDto>> GetAllAsync()
    {
        var inviteCodes = await _dbContext.InviteCodes
            .Include(ic => ic.CreatedBy)
            .Include(ic => ic.RedeemedBy)
            .OrderByDescending(ic => ic.CreatedAt)
            .Select(ic => new InviteCodeListItemDto(
                ic.Id,
                ic.Code,
                ic.TargetRole,
                ic.ExpiresAt,
                ic.IsUsed,
                ic.CreatedAt,
                ic.CreatedBy.DisplayName,
                ic.RedeemedBy != null ? ic.RedeemedBy.DisplayName : null,
                ic.RedeemedAt))
            .ToListAsync();

        return inviteCodes;
    }

    private static string GenerateCode()
    {
        Span<char> letters = stackalloc char[3];
        for (var i = 0; i < 3; i++)
        {
            letters[i] = UpperLetters[RandomNumberGenerator.GetInt32(UpperLetters.Length)];
        }

        var digits = RandomNumberGenerator.GetInt32(0, 10000);

        return $"{letters}-{digits:D4}";
    }
}
