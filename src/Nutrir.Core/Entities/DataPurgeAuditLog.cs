namespace Nutrir.Core.Entities;

public class DataPurgeAuditLog
{
    public int Id { get; set; }

    public DateTime PurgedAt { get; set; } = DateTime.UtcNow;

    public string PurgedByUserId { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public string ClientIdentifier { get; set; } = string.Empty;

    public string PurgedEntities { get; set; } = string.Empty;

    public string Justification { get; set; } = string.Empty;
}
