namespace Nutrir.Core.Entities;

public class InviteCode
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string TargetRole { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public bool IsUsed { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string CreatedById { get; set; } = string.Empty;

    public ApplicationUser CreatedBy { get; set; } = null!;

    public string? RedeemedById { get; set; }

    public ApplicationUser? RedeemedBy { get; set; }

    public DateTime? RedeemedAt { get; set; }
}
