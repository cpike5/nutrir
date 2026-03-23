namespace Nutrir.Core.Exceptions;

public class ConsentRequiredException : Exception
{
    public ConsentRequiredException(int clientId)
        : base($"Client {clientId} has not given consent. Consent is required before creating appointments.") { }
}
