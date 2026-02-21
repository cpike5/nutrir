namespace Nutrir.Core.DTOs;

public record InviteCodeValidationResult(
    bool IsValid,
    InviteCodeValidationStatus Status,
    string? TargetRole = null);

public enum InviteCodeValidationStatus
{
    Valid,
    NotFound,
    Expired,
    AlreadyUsed
}
