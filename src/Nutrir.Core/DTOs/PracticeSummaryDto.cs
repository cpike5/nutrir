namespace Nutrir.Core.DTOs;

public record PracticeSummaryDto(
    int TotalVisits,
    int NewClients,
    int ReturningClients,
    int NoShowCount,
    decimal NoShowRate,
    int CancellationCount,
    decimal CancellationRate,
    int ActiveClients,
    List<AppointmentsByTypeDto> AppointmentsByType,
    List<TrendBucketDto> TrendData);

public record AppointmentsByTypeDto(string Type, int Count);

public record TrendBucketDto(DateTime PeriodStart, string Label, int Visits, int NoShows, int Cancellations);
