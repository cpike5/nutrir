namespace Nutrir.Core.Exceptions;

public class InvalidStatusTransitionException : Exception
{
    public InvalidStatusTransitionException(string from, string to)
        : base($"Cannot transition appointment from {from} to {to}.") { }
}
