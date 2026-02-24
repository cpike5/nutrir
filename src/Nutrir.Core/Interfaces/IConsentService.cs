namespace Nutrir.Core.Interfaces;

public interface IConsentService
{
    Task GrantConsentAsync(int clientId, string purpose, string policyVersion, string userId);
    Task WithdrawConsentAsync(int clientId, string userId, string? reason = null);
}
