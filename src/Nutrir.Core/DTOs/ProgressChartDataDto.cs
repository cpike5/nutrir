using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record ProgressChartDataDto(
    MetricType MetricType,
    string Label,
    string? Unit,
    List<ProgressChartPointDto> Points);

public record ProgressChartPointDto(
    DateOnly Date,
    decimal Value);
