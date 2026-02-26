using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Services;

public class AiRateLimiter : IAiRateLimiter
{
    private readonly ConcurrentDictionary<string, UserRateState> _states = new();
    private readonly AiRateLimitOptions _options;
    private DateTime _lastCleanup = DateTime.UtcNow;

    public AiRateLimiter(IOptions<AiRateLimitOptions> options)
    {
        _options = options.Value;
    }

    public (bool Allowed, string? Message) CheckAndRecord(string userId)
    {
        CleanupStaleEntries();

        var state = _states.GetOrAdd(userId, _ => new UserRateState());
        var now = DateTime.UtcNow;

        lock (state)
        {
            // Reset minute window if expired
            if (now - state.MinuteWindowStart > TimeSpan.FromMinutes(1))
            {
                state.MinuteWindowStart = now;
                state.MinuteCount = 0;
            }

            // Reset day window if expired
            if (now - state.DayWindowStart > TimeSpan.FromDays(1))
            {
                state.DayWindowStart = now;
                state.DayCount = 0;
            }

            if (state.MinuteCount >= _options.RequestsPerMinute)
            {
                return (false, $"Rate limit exceeded. Maximum {_options.RequestsPerMinute} requests per minute. Please wait a moment.");
            }

            if (state.DayCount >= _options.RequestsPerDay)
            {
                return (false, $"Daily limit reached. Maximum {_options.RequestsPerDay} requests per day. Please try again tomorrow.");
            }

            state.MinuteCount++;
            state.DayCount++;
            state.LastAccess = now;

            return (true, null);
        }
    }

    private void CleanupStaleEntries()
    {
        var now = DateTime.UtcNow;
        if (now - _lastCleanup < TimeSpan.FromMinutes(5))
            return;

        _lastCleanup = now;
        var staleThreshold = now - TimeSpan.FromHours(1);

        foreach (var kvp in _states)
        {
            if (kvp.Value.LastAccess < staleThreshold)
            {
                _states.TryRemove(kvp.Key, out _);
            }
        }
    }

    private class UserRateState
    {
        public DateTime MinuteWindowStart = DateTime.UtcNow;
        public int MinuteCount;
        public DateTime DayWindowStart = DateTime.UtcNow;
        public int DayCount;
        public DateTime LastAccess = DateTime.UtcNow;
    }
}
