namespace Nutrir.Core.DTOs;

public record CreateUserDto(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? Password);
