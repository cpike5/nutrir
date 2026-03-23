namespace Nutrir.Core.Exceptions;

public class ConcurrentEditException : Exception
{
    public ConcurrentEditException()
        : base("This record was modified by another user. Please reload and try again.") { }

    public ConcurrentEditException(string entityType, int entityId)
        : base($"The {entityType} (ID: {entityId}) was modified by another user. Please reload and try again.") { }
}
