using FluentAssertions;
using Nutrir.Core.Models;
using Nutrir.Infrastructure.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace Nutrir.Tests.Unit.Renderers;

public class ConsentFormPdfRendererTests
{
    public ConsentFormPdfRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ---------------------------------------------------------------------------
    // Render — fully populated content
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithFullyPopulatedContent_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var content = new ConsentFormContent
        {
            Title = "Informed Consent for Nutritional Counselling",
            PracticeName = "Healthy Horizons Nutrition",
            FormVersion = "2.1",
            ClientName = "Jane Doe",
            PractitionerName = "Dr. Sarah Green, RD",
            Date = new DateTime(2024, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            SignatureBlockText = "By signing below I confirm I have read and understood the above.",
            Sections =
            [
                new ConsentSection
                {
                    Heading = "Scope of Practice",
                    Paragraphs =
                    [
                        "The practitioner will provide dietary guidance and meal planning.",
                        "This does not constitute medical advice."
                    ]
                },
                new ConsentSection
                {
                    Heading = "Privacy & Confidentiality",
                    Paragraphs =
                    [
                        "All personal health information is kept strictly confidential.",
                        "Data is stored securely and not shared with third parties without consent."
                    ]
                }
            ]
        };

        // Act
        var result = ConsentFormPdfRenderer.Render(content);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a fully-populated consent form should produce a valid PDF");
    }

    // ---------------------------------------------------------------------------
    // Render — minimal / sparse content
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithMinimalContent_ReturnsNonEmptyByteArray()
    {
        // Arrange — empty sections list and blank optional strings
        var content = new ConsentFormContent
        {
            Title = string.Empty,
            PracticeName = string.Empty,
            FormVersion = string.Empty,
            ClientName = string.Empty,
            PractitionerName = string.Empty,
            Date = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            SignatureBlockText = string.Empty,
            Sections = []
        };

        // Act
        var result = ConsentFormPdfRenderer.Render(content);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "rendering with empty content should still produce a valid PDF structure");
    }

    // ---------------------------------------------------------------------------
    // Render — sections with empty paragraph list
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithSectionsHavingNoParagraphs_ReturnsNonEmptyByteArray()
    {
        // Arrange
        var content = new ConsentFormContent
        {
            Title = "Consent Form",
            PracticeName = "Test Practice",
            FormVersion = "1.0",
            ClientName = "John Smith",
            PractitionerName = "Dr. Alice Brown",
            Date = new DateTime(2024, 3, 20, 0, 0, 0, DateTimeKind.Utc),
            SignatureBlockText = "I agree to the terms above.",
            Sections =
            [
                new ConsentSection
                {
                    Heading = "Terms",
                    Paragraphs = []
                }
            ]
        };

        // Act
        var result = ConsentFormPdfRenderer.Render(content);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a section with no paragraphs should still render correctly");
    }
}
