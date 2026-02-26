namespace Nutrir.Infrastructure.Configuration;

public class AiRateLimitOptions
{
    public const string SectionName = "AiRateLimits";
    public int RequestsPerMinute { get; set; } = 30;
    public int RequestsPerDay { get; set; } = 500;
}
