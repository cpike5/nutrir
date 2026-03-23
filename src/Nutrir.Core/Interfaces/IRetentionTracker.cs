namespace Nutrir.Core.Interfaces;

public interface IRetentionTracker
{
    Task UpdateLastInteractionAsync(int clientId);
}
