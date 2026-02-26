using Nutrir.Core.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Nutrir.Infrastructure.Services;

public static class ConsentFormPdfRenderer
{
    private const string PrimaryColor = "#2d6a4f";
    private const string TextColor = "#2a2d2b";
    private const string MutedColor = "#636865";

    public static byte[] Render(ConsentFormContent content)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.MarginHorizontal(60);
                page.MarginVertical(50);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(TextColor));

                page.Header().Element(c => ComposeHeader(c, content));
                page.Content().Element(c => ComposeContent(c, content));
                page.Footer().Element(c => ComposeFooter(c, content));
            });
        });

        return document.GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, ConsentFormContent content)
    {
        container.Column(column =>
        {
            column.Item().BorderBottom(2).BorderColor(PrimaryColor).PaddingBottom(10).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(content.PracticeName)
                        .FontSize(18).Bold().FontColor(PrimaryColor);
                    col.Item().Text(content.Title)
                        .FontSize(11).FontColor(MutedColor);
                });
            });

            column.Item().PaddingTop(12).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Client: ").Bold();
                    text.Span(content.ClientName);
                });
                row.RelativeItem().Text(text =>
                {
                    text.Span("Date: ").Bold();
                    text.Span(content.Date.ToString("MMMM d, yyyy"));
                });
            });

            column.Item().PaddingBottom(8).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Practitioner: ").Bold();
                    text.Span(content.PractitionerName);
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, ConsentFormContent content)
    {
        container.PaddingTop(8).Column(column =>
        {
            foreach (var section in content.Sections)
            {
                column.Item().PaddingTop(10).Text(section.Heading)
                    .FontSize(11).Bold().FontColor(PrimaryColor);

                foreach (var paragraph in section.Paragraphs)
                {
                    column.Item().PaddingTop(4).Text(paragraph)
                        .FontSize(10).LineHeight(1.4f);
                }
            }

            // Signature block
            column.Item().PaddingTop(24).BorderTop(1).BorderColor("#cccccc").PaddingTop(12).Column(sig =>
            {
                sig.Item().Text(content.SignatureBlockText)
                    .FontSize(10).Italic().LineHeight(1.4f);

                sig.Item().PaddingTop(24).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().BorderBottom(1).BorderColor(TextColor).Height(1).Width(200);
                        col.Item().PaddingTop(4).Text("Client Signature").FontSize(9).FontColor(MutedColor);
                    });

                    row.ConstantItem(40);

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().BorderBottom(1).BorderColor(TextColor).Height(1).Width(150);
                        col.Item().PaddingTop(4).Text("Date").FontSize(9).FontColor(MutedColor);
                    });
                });

                sig.Item().PaddingTop(16).Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().BorderBottom(1).BorderColor(TextColor).Height(1).Width(200);
                        col.Item().PaddingTop(4).Text("Practitioner Signature").FontSize(9).FontColor(MutedColor);
                    });

                    row.ConstantItem(40);

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().BorderBottom(1).BorderColor(TextColor).Height(1).Width(150);
                        col.Item().PaddingTop(4).Text("Date").FontSize(9).FontColor(MutedColor);
                    });
                });
            });
        });
    }

    private static void ComposeFooter(IContainer container, ConsentFormContent content)
    {
        container.BorderTop(1).BorderColor("#e0e0e0").PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span($"{content.PracticeName} — Consent Form v{content.FormVersion}")
                    .FontSize(8).FontColor(MutedColor);
            });
            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("Page ").FontSize(8).FontColor(MutedColor);
                text.CurrentPageNumber().FontSize(8).FontColor(MutedColor);
                text.Span(" of ").FontSize(8).FontColor(MutedColor);
                text.TotalPages().FontSize(8).FontColor(MutedColor);
            });
        });
    }
}
