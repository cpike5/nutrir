namespace Nutrir.Core.Interfaces;

public interface IAuditLogService
{
    Task LogAsync(
        string userId,
        string action,
        string entityType,
        string? entityId = null,
        string? details = null,
        string? ipAddress = null);
}
