using Nutrir.Core.DTOs;

namespace Nutrir.Core.Interfaces;

public interface IInviteCodeService
{
    Task<InviteCodeListItemDto> GenerateAsync(string createdByUserId, string targetRole, int expirationDays = 7);

    Task<InviteCodeValidationResult> ValidateAsync(string code);

    Task RedeemAsync(string code, string userId);

    Task<List<InviteCodeListItemDto>> GetAllAsync();
}
