using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ConsentForm
{
    public int Id { get; set; }

    public int ClientId { get; set; }

    public string FormVersion { get; set; } = string.Empty;

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    public string GeneratedByUserId { get; set; } = string.Empty;

    public ConsentSignatureMethod SignatureMethod { get; set; }

    public bool IsSigned { get; set; }

    public DateTime? SignedAt { get; set; }

    public string? SignedByUserId { get; set; }

    public string? ScannedCopyPath { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
