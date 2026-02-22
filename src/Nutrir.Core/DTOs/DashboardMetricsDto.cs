namespace Nutrir.Core.DTOs;

public record DashboardMetricsDto(
    int TotalActiveClients,
    int PendingConsentCount,
    int NewClientsThisMonth);
