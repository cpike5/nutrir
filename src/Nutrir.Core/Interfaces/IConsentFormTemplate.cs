using Nutrir.Core.Models;

namespace Nutrir.Core.Interfaces;

public interface IConsentFormTemplate
{
    string Version { get; }

    ConsentFormContent Generate(string clientName, string practitionerName, DateTime date);
}
