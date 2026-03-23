using FluentAssertions;
using Nutrir.Core.Models;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Renderers;

public class ConsentFormDocxRendererTests
{
    // ---------------------------------------------------------------------------
    // Render — fully populated content, no template (programmatic path)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithFullyPopulatedContentAndNoTemplate_ReturnsNonEmptyByteArray()
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
        var result = ConsentFormDocxRenderer.Render(content, templatePath: null);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a fully-populated consent form should produce a valid DOCX document");
    }

    // ---------------------------------------------------------------------------
    // Render — minimal / sparse content, no template
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithMinimalContentAndNoTemplate_ReturnsNonEmptyByteArray()
    {
        // Arrange — empty sections list and blank string fields
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
        var result = ConsentFormDocxRenderer.Render(content, templatePath: null);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "rendering with empty content should still produce a valid DOCX structure");
    }

    // ---------------------------------------------------------------------------
    // Render — non-existent template path falls back to programmatic generation
    // ---------------------------------------------------------------------------

    [Fact]
    public void Render_WithNonExistentTemplatePath_FallsBackToProgrammaticAndReturnsNonEmptyByteArray()
    {
        // Arrange — provide a path that does not exist so the renderer falls back
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
                    Paragraphs = ["I understand and agree to the terms described herein."]
                }
            ]
        };

        // Act
        var result = ConsentFormDocxRenderer.Render(content, templatePath: "/tmp/nonexistent-template.docx");

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "when the template file does not exist the renderer falls back to programmatic generation");
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
            PracticeName = "Nutrition Co.",
            FormVersion = "1.0",
            ClientName = "Mary Watson",
            PractitionerName = "Dr. Tom Hill",
            Date = new DateTime(2024, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            SignatureBlockText = "Signature required below.",
            Sections =
            [
                new ConsentSection
                {
                    Heading = "Heading With No Body",
                    Paragraphs = []
                }
            ]
        };

        // Act
        var result = ConsentFormDocxRenderer.Render(content, templatePath: null);

        // Assert
        result.Should().NotBeNull();
        result.Should().NotBeEmpty(because: "a section without paragraphs should still render successfully");
    }
}
