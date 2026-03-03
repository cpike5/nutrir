using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ClientDietaryRestriction
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public DietaryRestrictionType RestrictionType { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }
}
