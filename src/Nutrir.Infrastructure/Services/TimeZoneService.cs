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

        try
        {
            var authState = _authStateProvider.GetAuthenticationStateAsync().GetAwaiter().GetResult();
            var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (userId is not null)
            {
                var user = _userManager.FindByIdAsync(userId).GetAwaiter().GetResult();
                if (user is not null && !string.IsNullOrEmpty(user.TimeZoneId))
                {
                    _cachedTimeZone = TimeZoneInfo.FindSystemTimeZoneById(user.TimeZoneId);
                    return _cachedTimeZone;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve user timezone, falling back to default {DefaultTz}", DefaultTimeZoneId);
        }

        _cachedTimeZone = TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        return _cachedTimeZone;
    }
}
