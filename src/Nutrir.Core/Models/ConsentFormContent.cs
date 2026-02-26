namespace Nutrir.Core.Models;

public class ConsentFormContent
{
    public string Title { get; set; } = string.Empty;

    public string PracticeName { get; set; } = string.Empty;

    public string FormVersion { get; set; } = string.Empty;

    public string ClientName { get; set; } = string.Empty;

    public string PractitionerName { get; set; } = string.Empty;

    public DateTime Date { get; set; }

    public List<ConsentSection> Sections { get; set; } = [];

    public string SignatureBlockText { get; set; } = string.Empty;
}

public class ConsentSection
{
    public string Heading { get; set; } = string.Empty;

    public List<string> Paragraphs { get; set; } = [];
}
