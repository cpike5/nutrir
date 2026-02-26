using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class AuditSourceProvider : IAuditSourceProvider
{
    public AuditSource CurrentSource { get; private set; } = AuditSource.Web;

    public void SetSource(AuditSource source) => CurrentSource = source;
}
