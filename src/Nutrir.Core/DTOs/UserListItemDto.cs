namespace Nutrir.Core.DTOs;

public record UserListItemDto(
    string Id,
    string FirstName,
    string LastName,
    string DisplayName,
    string Email,
    string Role,
    bool IsActive,
    DateTime? LastLoginDate);
