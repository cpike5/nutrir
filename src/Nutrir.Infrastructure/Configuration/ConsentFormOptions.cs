namespace Nutrir.Infrastructure.Configuration;

public class ConsentFormOptions
{
    public const string SectionName = "ConsentForm";

    public bool RequiredOnClientCreation { get; set; } = true;

    public string PracticeName { get; set; } = "Nutrir Nutrition Practice";

    public string ScannedCopyStoragePath { get; set; } = "uploads/consent-scans";

    /// <summary>
    /// Absolute path to the DOCX template file. Set at startup by the web host.
    /// </summary>
    public string? DocxTemplatePath { get; set; }
}
