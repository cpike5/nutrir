using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class TimeZoneService : ITimeZoneService
{
    private readonly AuthenticationStateProvider _authStateProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<TimeZoneService> _logger;
    private TimeZoneInfo? _cachedTimeZone;

    private const string DefaultTimeZoneId = "America/Toronto";

    public TimeZoneService(
        AuthenticationStateProvider authStateProvider,
        UserManager<ApplicationUser> userManager,
        ILogger<TimeZoneService> logger)
    {
        _authStateProvider = authStateProvider;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        if (_cachedTimeZone is not null)
            return;

        try
        {
            var authState = await _authStateProvider.GetAuthenticationStateAsync();
            var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId is not null)
            {
                var user = await _userManager.FindByIdAsync(userId);
                if (user is not null && !string.IsNullOrEmpty(user.TimeZoneId))
                {
                    if (TimeZoneInfo.TryFindSystemTimeZoneById(user.TimeZoneId, out var userTz))
                    {
                        _cachedTimeZone = userTz;
                        return;
                    }

                    _logger.LogWarning("Unknown timezone ID {TimeZoneId} for user {UserId}, falling back to default", user.TimeZoneId, userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve user timezone, falling back to default {DefaultTz}", DefaultTimeZoneId);
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZoneId, out var defaultTz))
            defaultTz = TimeZoneInfo.Utc;

        _cachedTimeZone = defaultTz;
    }

    public DateTime UserNow => ToUserLocal(DateTime.UtcNow);

    public DateTime ToUserLocal(DateTime utcDateTime)
    {
        var tz = GetTimeZone();
        var utc = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }

    public DateTime ToUtc(DateTime localDateTime)
    {
        var tz = GetTimeZone();
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private TimeZoneInfo GetTimeZone()
    {
        if (_cachedTimeZone is not null)
            return _cachedTimeZone;

        // InitializeAsync was not called — fall back to default safely
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(DefaultTimeZoneId, out var defaultTz))
            defaultTz = TimeZoneInfo.Utc;

        _cachedTimeZone = defaultTz;
        return _cachedTimeZone;
    }
}
