namespace Nutrir.Core.DTOs;

public record CreateInviteCodeDto(
    string TargetRole,
    int ExpirationDays = 7);
