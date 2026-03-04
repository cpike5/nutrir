namespace Nutrir.Core.Exceptions;

public class SchedulingConflictException : Exception
{
    public int? ConflictingAppointmentId { get; }
    public DateTime? ConflictingStartTime { get; }
    public DateTime? ConflictingEndTime { get; }
    public string? ConflictingClientName { get; }
    public string Reason { get; }

    public SchedulingConflictException(string reason, string message) : base(message)
    {
        Reason = reason;
    }

    public SchedulingConflictException(
        string reason,
        string message,
        int conflictingAppointmentId,
        DateTime conflictingStartTime,
        DateTime conflictingEndTime,
        string? conflictingClientName) : base(message)
    {
        Reason = reason;
        ConflictingAppointmentId = conflictingAppointmentId;
        ConflictingStartTime = conflictingStartTime;
        ConflictingEndTime = conflictingEndTime;
        ConflictingClientName = conflictingClientName;
    }
}
