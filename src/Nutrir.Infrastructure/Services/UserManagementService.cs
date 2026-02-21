using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class UserManagementService : IUserManagementService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IAuditLogService auditLogService,
        ILogger<UserManagementService> logger)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<UserListItemDto>> GetUsersAsync(
        string? searchTerm = null,
        string? roleFilter = null,
        bool? isActiveFilter = null)
    {
        var query = _userManager.Users.AsQueryable();

        if (isActiveFilter.HasValue)
        {
            query = query.Where(u => u.IsActive == isActiveFilter.Value);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                u.DisplayName.ToLower().Contains(term) ||
                (u.Email != null && u.Email.ToLower().Contains(term)));
        }

        var users = await query
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var result = new List<UserListItemDto>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var role = roles.FirstOrDefault() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(roleFilter) &&
                !string.Equals(role, roleFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(new UserListItemDto(
                user.Id,
                user.FirstName,
                user.LastName,
                user.DisplayName,
                user.Email ?? string.Empty,
                role,
                user.IsActive,
                user.LastLoginDate));
        }

        return result;
    }

    public async Task<UserDetailDto?> GetUserByIdAsync(string userId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.FirstOrDefault() ?? string.Empty;

        return new UserDetailDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.DisplayName,
            user.Email ?? string.Empty,
            role,
            user.IsActive,
            user.CreatedDate,
            user.LastLoginDate,
            user.TwoFactorEnabled);
    }

    public async Task<bool> UpdateProfileAsync(
        string userId,
        string firstName,
        string lastName,
        string displayName,
        string email)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("UpdateProfile failed: user {UserId} not found", userId);
            return false;
        }

        var previousEmail = user.Email;
        user.FirstName = firstName;
        user.LastName = lastName;
        user.DisplayName = displayName;

        if (!string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var setEmailResult = await _userManager.SetEmailAsync(user, email);
            if (!setEmailResult.Succeeded)
            {
                _logger.LogWarning(
                    "UpdateProfile failed to set email for user {UserId}: {Errors}",
                    userId, string.Join("; ", setEmailResult.Errors.Select(e => e.Description)));
                return false;
            }

            var setUserNameResult = await _userManager.SetUserNameAsync(user, email);
            if (!setUserNameResult.Succeeded)
            {
                _logger.LogWarning(
                    "UpdateProfile failed to set username for user {UserId}: {Errors}",
                    userId, string.Join("; ", setUserNameResult.Errors.Select(e => e.Description)));
                return false;
            }
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "UpdateProfile failed for user {UserId}: {Errors}",
                userId, string.Join("; ", result.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            userId,
            "ProfileUpdated",
            "User",
            userId,
            $"Profile updated. Name: {firstName} {lastName}, Display: {displayName}, Email: {email}");

        _logger.LogInformation("Profile updated for user {UserId}", userId);
        return true;
    }

    public async Task<bool> ChangeRoleAsync(string userId, string newRole, string changedByUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("ChangeRole failed: user {UserId} not found", userId);
            return false;
        }

        if (!await _roleManager.RoleExistsAsync(newRole))
        {
            _logger.LogWarning("ChangeRole failed: role {Role} does not exist", newRole);
            return false;
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var previousRole = currentRoles.FirstOrDefault() ?? "None";

        if (currentRoles.Count > 0)
        {
            var removeResult = await _userManager.RemoveFromRolesAsync(user, currentRoles);
            if (!removeResult.Succeeded)
            {
                _logger.LogWarning(
                    "ChangeRole failed to remove existing roles for user {UserId}: {Errors}",
                    userId, string.Join("; ", removeResult.Errors.Select(e => e.Description)));
                return false;
            }
        }

        var addResult = await _userManager.AddToRoleAsync(user, newRole);
        if (!addResult.Succeeded)
        {
            _logger.LogWarning(
                "ChangeRole failed to add role {Role} for user {UserId}: {Errors}",
                newRole, userId, string.Join("; ", addResult.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            changedByUserId,
            "RoleChanged",
            "User",
            userId,
            $"Role changed from {previousRole} to {newRole}");

        _logger.LogInformation(
            "Role changed for user {UserId} from {PreviousRole} to {NewRole} by {ChangedByUserId}",
            userId, previousRole, newRole, changedByUserId);

        return true;
    }

    public async Task<bool> DeactivateAsync(string userId, string deactivatedByUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("Deactivate failed: user {UserId} not found", userId);
            return false;
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("Deactivate failed: user {UserId} is already inactive", userId);
            return false;
        }

        user.IsActive = false;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Deactivate failed for user {UserId}: {Errors}",
                userId, string.Join("; ", result.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            deactivatedByUserId,
            "UserDeactivated",
            "User",
            userId,
            "User account deactivated");

        _logger.LogInformation(
            "User {UserId} deactivated by {DeactivatedByUserId}",
            userId, deactivatedByUserId);

        return true;
    }

    public async Task<bool> ReactivateAsync(string userId, string reactivatedByUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("Reactivate failed: user {UserId} not found", userId);
            return false;
        }

        if (user.IsActive)
        {
            _logger.LogWarning("Reactivate failed: user {UserId} is already active", userId);
            return false;
        }

        user.IsActive = true;
        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "Reactivate failed for user {UserId}: {Errors}",
                userId, string.Join("; ", result.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            reactivatedByUserId,
            "UserReactivated",
            "User",
            userId,
            "User account reactivated");

        _logger.LogInformation(
            "User {UserId} reactivated by {ReactivatedByUserId}",
            userId, reactivatedByUserId);

        return true;
    }

    public async Task<bool> ResetPasswordAsync(string userId, string newPassword, string resetByUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("ResetPassword failed: user {UserId} not found", userId);
            return false;
        }

        var removeResult = await _userManager.RemovePasswordAsync(user);
        if (!removeResult.Succeeded)
        {
            _logger.LogWarning(
                "ResetPassword failed to remove password for user {UserId}: {Errors}",
                userId, string.Join("; ", removeResult.Errors.Select(e => e.Description)));
            return false;
        }

        var addResult = await _userManager.AddPasswordAsync(user, newPassword);
        if (!addResult.Succeeded)
        {
            _logger.LogWarning(
                "ResetPassword failed to set new password for user {UserId}: {Errors}",
                userId, string.Join("; ", addResult.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            resetByUserId,
            "PasswordReset",
            "User",
            userId,
            "Password was reset by administrator");

        _logger.LogInformation(
            "Password reset for user {UserId} by {ResetByUserId}",
            userId, resetByUserId);

        return true;
    }

    public async Task<bool> ForceMfaAsync(string userId, string forcedByUserId)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            _logger.LogWarning("ForceMfa failed: user {UserId} not found", userId);
            return false;
        }

        if (user.TwoFactorEnabled)
        {
            _logger.LogWarning("ForceMfa: user {UserId} already has MFA enabled", userId);
            return false;
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "ForceMfa failed for user {UserId}: {Errors}",
                userId, string.Join("; ", result.Errors.Select(e => e.Description)));
            return false;
        }

        await _auditLogService.LogAsync(
            forcedByUserId,
            "MfaForced",
            "User",
            userId,
            "MFA was force-enabled by administrator");

        _logger.LogInformation(
            "MFA force-enabled for user {UserId} by {ForcedByUserId}",
            userId, forcedByUserId);

        return true;
    }
}
