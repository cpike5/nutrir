namespace Nutrir.Infrastructure.Configuration;

public class AiRetentionOptions
{
    public const string SectionName = "AiRetention";

    public int ContentStripIntervalMinutes { get; set; } = 30;
    public int ContentStripThresholdHours { get; set; } = 8;
    public int PurgeIntervalMinutes { get; set; } = 1440;
    public int PurgeThresholdDays { get; set; } = 90;
}
