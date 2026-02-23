using Nutrir.Core.Interfaces;
using Nutrir.Core.Models;

namespace Nutrir.Infrastructure.Services;

public class MaintenanceService : IMaintenanceService
{
    private readonly object _lock = new();
    private MaintenanceState _state = new();

    public MaintenanceState GetState()
    {
        lock (_lock)
        {
            return new MaintenanceState
            {
                IsEnabled = _state.IsEnabled,
                StartedAt = _state.StartedAt,
                EstimatedEndAt = _state.EstimatedEndAt,
                Message = _state.Message,
                EnabledBy = _state.EnabledBy
            };
        }
    }

    public void Enable(string? message = null, int? estimatedMinutes = null, string? enabledBy = null)
    {
        lock (_lock)
        {
            _state = new MaintenanceState
            {
                IsEnabled = true,
                StartedAt = DateTime.UtcNow,
                EstimatedEndAt = estimatedMinutes.HasValue
                    ? DateTime.UtcNow.AddMinutes(estimatedMinutes.Value)
                    : null,
                Message = message,
                EnabledBy = enabledBy
            };
        }
    }

    public void Disable()
    {
        lock (_lock)
        {
            _state = new MaintenanceState();
        }
    }
}
