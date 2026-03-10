namespace Nutrir.Infrastructure.Configuration;

public class AutoArchiveOptions
{
    public const string SectionName = "AutoArchive";

    public int IntervalMinutes { get; set; } = 60;
}
