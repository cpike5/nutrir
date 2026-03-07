using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class IntakeForm
{
    public int Id { get; set; }

    public int? ClientId { get; set; }

    public int? AppointmentId { get; set; }

    public string Token { get; set; } = string.Empty;

    public IntakeFormStatus Status { get; set; }

    public string ClientEmail { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime? SubmittedAt { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public string? ReviewedByUserId { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }

    public string? DeletedBy { get; set; }

    public List<IntakeFormResponse> Responses { get; set; } = [];
}
