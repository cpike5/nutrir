using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ClientAllergy
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string Name { get; set; } = string.Empty;

    public AllergySeverity Severity { get; set; }

    public AllergyType AllergyType { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}
