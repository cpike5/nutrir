using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Nutrir.Core.Models;

namespace Nutrir.Infrastructure.Services;

public static class ConsentFormDocxRenderer
{
    /// <summary>
    /// Opens the reference .docx template from the provided path,
    /// replaces {{ClientName}}, {{Date}}, {{PractitionerName}} tokens,
    /// and returns the populated document as a byte array.
    /// Falls back to programmatic generation if the template file is not found.
    /// </summary>
    public static byte[] Render(ConsentFormContent content, string? templatePath = null)
    {
        if (!string.IsNullOrEmpty(templatePath) && File.Exists(templatePath))
        {
            return RenderFromTemplate(content, templatePath);
        }

        return RenderProgrammatic(content);
    }

    private static readonly Dictionary<string, Func<ConsentFormContent, string>> Tokens = new()
    {
        ["{{ClientName}}"] = c => c.ClientName,
        ["{{Date}}"] = c => c.Date.ToString("MMMM d, yyyy"),
        ["{{PractitionerName}}"] = c => c.PractitionerName,
        ["{{PracticeName}}"] = c => c.PracticeName,
    };

    private static byte[] RenderFromTemplate(ConsentFormContent content, string templatePath)
    {
        using var memoryStream = new MemoryStream();

        // Copy template to memory stream
        using (var fileStream = File.OpenRead(templatePath))
        {
            fileStream.CopyTo(memoryStream);
        }
        memoryStream.Position = 0;

        using (var doc = WordprocessingDocument.Open(memoryStream, true))
        {
            var body = doc.MainDocumentPart?.Document.Body;
            if (body is null) return RenderProgrammatic(content);

            // Word often splits {{Token}} across multiple runs.
            // Work paragraph-by-paragraph: concatenate all run text, replace tokens,
            // then collapse into a single run preserving the first run's formatting.
            foreach (var paragraph in body.Descendants<Paragraph>())
            {
                ReplaceParagraphTokens(paragraph, content);
            }

            doc.MainDocumentPart!.Document.Save();
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Concatenates all Text nodes in a paragraph, checks for tokens, and if found
    /// collapses the runs into properly split segments so replacements are clean.
    /// </summary>
    private static void ReplaceParagraphTokens(Paragraph paragraph, ConsentFormContent content)
    {
        var runs = paragraph.Descendants<Run>().ToList();
        if (runs.Count == 0) return;

        // Build the full concatenated text for this paragraph
        var fullText = string.Concat(runs.SelectMany(r => r.Descendants<Text>()).Select(t => t.Text));

        // Check if any token exists in the concatenated text
        var hasToken = false;
        foreach (var token in Tokens.Keys)
        {
            if (fullText.Contains(token))
            {
                hasToken = true;
                break;
            }
        }

        if (!hasToken) return;

        // Perform all replacements
        foreach (var (token, valueFunc) in Tokens)
        {
            fullText = fullText.Replace(token, valueFunc(content));
        }

        // Preserve formatting from the first run
        var firstRun = runs[0];
        var runProps = firstRun.RunProperties?.CloneNode(true) as RunProperties;

        // Remove all existing runs from the paragraph
        foreach (var run in runs)
        {
            run.Remove();
        }

        // Insert a single new run with the replaced text
        var newRun = new Run();
        if (runProps is not null)
        {
            newRun.AppendChild(runProps);
        }
        newRun.AppendChild(new Text(fullText) { Space = SpaceProcessingModeValues.Preserve });

        // Insert after paragraph properties (or at start)
        var paragraphProps = paragraph.GetFirstChild<ParagraphProperties>();
        if (paragraphProps is not null)
        {
            paragraphProps.InsertAfterSelf(newRun);
        }
        else
        {
            paragraph.PrependChild(newRun);
        }
    }

    private static byte[] RenderProgrammatic(ConsentFormContent content)
    {
        using var memoryStream = new MemoryStream();

        using (var doc = WordprocessingDocument.Create(memoryStream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            // Title
            body.AppendChild(CreateParagraph(content.PracticeName, bold: true, fontSize: 28, color: "2D6A4F"));
            body.AppendChild(CreateParagraph(content.Title, fontSize: 22, color: "636865"));
            body.AppendChild(CreateParagraph(""));

            // Client / practitioner info
            body.AppendChild(CreateParagraph($"Client: {content.ClientName}", bold: true));
            body.AppendChild(CreateParagraph($"Practitioner: {content.PractitionerName}", bold: true));
            body.AppendChild(CreateParagraph($"Date: {content.Date:MMMM d, yyyy}", bold: true));
            body.AppendChild(CreateParagraph(""));

            // Sections
            foreach (var section in content.Sections)
            {
                body.AppendChild(CreateParagraph(section.Heading, bold: true, fontSize: 22, color: "2D6A4F"));

                foreach (var paragraph in section.Paragraphs)
                {
                    body.AppendChild(CreateParagraph(paragraph));
                }

                body.AppendChild(CreateParagraph(""));
            }

            // Signature block
            body.AppendChild(CreateParagraph(""));
            body.AppendChild(CreateParagraph(content.SignatureBlockText, italic: true));
            body.AppendChild(CreateParagraph(""));
            body.AppendChild(CreateParagraph(""));
            body.AppendChild(CreateParagraph("_________________________________          _______________"));
            body.AppendChild(CreateParagraph("Client Signature                                           Date", fontSize: 18, color: "636865"));
            body.AppendChild(CreateParagraph(""));
            body.AppendChild(CreateParagraph("_________________________________          _______________"));
            body.AppendChild(CreateParagraph("Practitioner Signature                                   Date", fontSize: 18, color: "636865"));
            body.AppendChild(CreateParagraph(""));
            body.AppendChild(CreateParagraph($"{content.PracticeName} — Consent Form v{content.FormVersion}", fontSize: 16, color: "636865"));

            mainPart.Document.Save();
        }

        return memoryStream.ToArray();
    }

    private static Paragraph CreateParagraph(string text, bool bold = false, bool italic = false, int fontSize = 20, string? color = null)
    {
        var run = new Run();
        var runProperties = new RunProperties();

        runProperties.AppendChild(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
        runProperties.AppendChild(new FontSize { Val = fontSize.ToString() });

        if (bold) runProperties.AppendChild(new Bold());
        if (italic) runProperties.AppendChild(new Italic());
        if (color is not null) runProperties.AppendChild(new Color { Val = color });

        run.PrependChild(runProperties);
        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });

        var paragraph = new Paragraph();
        paragraph.AppendChild(run);

        // Add spacing between paragraphs
        var paragraphProperties = new ParagraphProperties();
        paragraphProperties.AppendChild(new SpacingBetweenLines { After = "80" });
        paragraph.PrependChild(paragraphProperties);

        return paragraph;
    }
}
