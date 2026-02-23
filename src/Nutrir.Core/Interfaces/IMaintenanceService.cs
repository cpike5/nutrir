using Nutrir.Core.Models;

namespace Nutrir.Core.Interfaces;

public interface IMaintenanceService
{
    MaintenanceState GetState();
    void Enable(string? message = null, int? estimatedMinutes = null, string? enabledBy = null);
    void Disable();
}
