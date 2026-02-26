namespace Nutrir.Core.Interfaces;

/// <summary>
/// Converts between UTC and the current user's local timezone.
/// </summary>
public interface ITimeZoneService
{
    /// <summary>Converts a UTC DateTime to the current user's local time.</summary>
    DateTime ToUserLocal(DateTime utcDateTime);

    /// <summary>Converts a user-local DateTime to UTC.</summary>
    DateTime ToUtc(DateTime localDateTime);

    /// <summary>Gets the current date/time in the user's timezone.</summary>
    DateTime UserNow { get; }
}
