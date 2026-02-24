namespace Nutrir.Core.Entities;

public class Client
{
    public int Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string PrimaryNutritionistId { get; set; } = string.Empty;

    public bool ConsentGiven { get; set; }

    public DateTime? ConsentTimestamp { get; set; }

    public string? ConsentPolicyVersion { get; set; }

    public string? Notes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public List<ConsentEvent> ConsentEvents { get; set; } = [];
}
