using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IAuditSourceProvider
{
    AuditSource CurrentSource { get; }
    void SetSource(AuditSource source);
}
