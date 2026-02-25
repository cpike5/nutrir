using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IUserManagementService
{
    Task<List<UserListItemDto>> GetUsersAsync(
        string? searchTerm = null,
        string? roleFilter = null,
        bool? isActiveFilter = null);

    Task<UserDetailDto?> GetUserByIdAsync(string userId);

    Task<bool> UpdateProfileAsync(
        string userId,
        string firstName,
        string lastName,
        string displayName,
        string email);

    Task<bool> ChangeRoleAsync(string userId, string newRole, string changedByUserId);

    Task<bool> DeactivateAsync(string userId, string deactivatedByUserId);

    Task<bool> ReactivateAsync(string userId, string reactivatedByUserId);

    Task<bool> ResetPasswordAsync(string userId, string newPassword, string resetByUserId);

    Task<bool> ForceMfaAsync(string userId, string forcedByUserId);

    Task<CreateUserResultDto> CreateUserAsync(CreateUserDto dto, string createdByUserId);
}
