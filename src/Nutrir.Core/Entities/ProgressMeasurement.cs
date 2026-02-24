using Nutrir.Core.Enums;

namespace Nutrir.Core.Entities;

public class ProgressMeasurement
{
    public int Id { get; set; }

    public int ProgressEntryId { get; set; }

    public MetricType MetricType { get; set; }

    public string? CustomMetricName { get; set; }

    public decimal Value { get; set; }

    public string? Unit { get; set; }
}
