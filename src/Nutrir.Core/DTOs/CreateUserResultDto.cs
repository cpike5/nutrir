namespace Nutrir.Core.DTOs;

public record CreateUserResultDto(
    UserDetailDto User,
    string GeneratedPassword);
