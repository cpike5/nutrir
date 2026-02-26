namespace Nutrir.Core.Interfaces;

/// <summary>
/// Converts between UTC and the current user's local timezone.
/// Call <see cref="InitializeAsync"/> once before using conversion methods.
/// </summary>
public interface ITimeZoneService
{
    /// <summary>
    /// Resolves the current user's timezone asynchronously. Must be called
    /// before using <see cref="ToUserLocal"/>, <see cref="ToUtc"/>, or <see cref="UserNow"/>.
    /// Safe to call multiple times — only resolves on the first call.
    /// </summary>
    Task InitializeAsync();

    /// <summary>Converts a UTC DateTime to the current user's local time.</summary>
    DateTime ToUserLocal(DateTime utcDateTime);

    /// <summary>Converts a user-local DateTime to UTC.</summary>
    DateTime ToUtc(DateTime localDateTime);

    /// <summary>Gets the current date/time in the user's timezone.</summary>
    DateTime UserNow { get; }
}
