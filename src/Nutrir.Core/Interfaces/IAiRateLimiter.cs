namespace Nutrir.Core.Interfaces;

public interface IAiRateLimiter
{
    (bool Allowed, string? Message) CheckAndRecord(string userId);
}
