using System.Collections.Frozen;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Services;

public static class AppointmentStatusTransitions
{
    private static readonly FrozenDictionary<AppointmentStatus, FrozenSet<AppointmentStatus>> Transitions =
        new Dictionary<AppointmentStatus, FrozenSet<AppointmentStatus>>
        {
            [AppointmentStatus.Scheduled] = new[]
            {
                AppointmentStatus.Confirmed,
                AppointmentStatus.Cancelled,
                AppointmentStatus.LateCancellation
            }.ToFrozenSet(),

            [AppointmentStatus.Confirmed] = new[]
            {
                AppointmentStatus.Completed,
                AppointmentStatus.NoShow,
                AppointmentStatus.Cancelled,
                AppointmentStatus.LateCancellation
            }.ToFrozenSet(),

            [AppointmentStatus.Completed] = FrozenSet<AppointmentStatus>.Empty,
            [AppointmentStatus.NoShow] = FrozenSet<AppointmentStatus>.Empty,
            [AppointmentStatus.LateCancellation] = FrozenSet<AppointmentStatus>.Empty,
            [AppointmentStatus.Cancelled] = FrozenSet<AppointmentStatus>.Empty
        }.ToFrozenDictionary();

    public static bool IsValidTransition(AppointmentStatus from, AppointmentStatus to)
    {
        return Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }

    public static IReadOnlyList<AppointmentStatus> GetAllowedTransitions(AppointmentStatus from)
    {
        return Transitions.TryGetValue(from, out var allowed)
            ? allowed.ToList().AsReadOnly()
            : Array.Empty<AppointmentStatus>().AsReadOnly();
    }
}
