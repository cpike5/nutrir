namespace Nutrir.Core.Models;

public class MaintenanceState
{
    public bool IsEnabled { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EstimatedEndAt { get; set; }
    public string? Message { get; set; }
    public string? EnabledBy { get; set; }
}
