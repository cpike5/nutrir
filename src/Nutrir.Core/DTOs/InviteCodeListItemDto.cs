namespace Nutrir.Core.DTOs;

public record InviteCodeListItemDto(
    int Id,
    string Code,
    string TargetRole,
    DateTime ExpiresAt,
    bool IsUsed,
    DateTime CreatedAt,
    string CreatedByName,
    string? RedeemedByName,
    DateTime? RedeemedAt);
