using FluentAssertions;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services.Ai;

public class AiMarkdownRendererTests
{
    private readonly AiMarkdownRenderer _sut = new();

    // -------------------------------------------------------------------------
    // RenderToHtml — null / empty guards
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_NullInput_ReturnsEmptyString()
    {
        // Arrange / Act
        var result = _sut.RenderToHtml(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void RenderToHtml_EmptyInput_ReturnsEmptyString()
    {
        // Arrange / Act
        var result = _sut.RenderToHtml(string.Empty);

        // Assert
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // RenderToHtml — entity link chips
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_EntityLink_Client_RendersAnchorWithCorrectHrefAndClass()
    {
        // Arrange
        const string markdown = "[[client:5:John Doe]]";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("href=\"/clients/5\"",
            because: "client entity links route to /clients/{id}");
        result.Should().Contain("cc-ai-entity-chip",
            because: "entity link chips carry the cc-ai-entity-chip CSS class");
        result.Should().Contain("John Doe",
            because: "the display text must be preserved in the rendered output");
    }

    [Fact]
    public void RenderToHtml_EntityLink_Appointment_RendersAnchorWithCorrectHref()
    {
        // Arrange
        const string markdown = "[[appointment:42:Meeting]]";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("href=\"/appointments/42\"",
            because: "appointment entity links route to /appointments/{id}");
        result.Should().Contain("Meeting");
    }

    [Fact]
    public void RenderToHtml_EntityLink_MealPlan_RendersAnchorWithCorrectHref()
    {
        // Arrange
        const string markdown = "[[meal_plan:7:Diet Plan]]";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("href=\"/meal-plans/7\"",
            because: "meal_plan entity links route to /meal-plans/{id}");
        result.Should().Contain("Diet Plan");
    }

    [Fact]
    public void RenderToHtml_EntityLink_User_WithSlugId_RendersAnchorWithCorrectHref()
    {
        // Arrange — user ids may be slug-style (letters, digits, hyphens)
        const string markdown = "[[user:abc-123:Admin]]";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("href=\"/admin/users/abc-123\"",
            because: "user entity links route to /admin/users/{id}");
        result.Should().Contain("Admin");
    }

    [Fact]
    public void RenderToHtml_EntityLink_UnknownType_RendersPlainDisplayText()
    {
        // Arrange
        const string markdown = "[[unknown:1:Text]]";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("Text",
            because: "the display label must still appear");
        result.Should().NotContain("<a ",
            because: "unknown entity types must not produce an anchor element");
        result.Should().NotContain("href",
            because: "unknown entity types have no registered route");
    }

    // -------------------------------------------------------------------------
    // RenderToHtml — inline formatting
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_Bold_RendersStrongTag()
    {
        // Arrange
        const string markdown = "**bold**";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<strong>bold</strong>");
    }

    [Fact]
    public void RenderToHtml_Italic_RendersEmTag()
    {
        // Arrange
        const string markdown = "*italic*";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<em>italic</em>");
    }

    [Fact]
    public void RenderToHtml_InlineCode_RendersCodeTag()
    {
        // Arrange
        const string markdown = "`code`";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<code>code</code>");
    }

    // -------------------------------------------------------------------------
    // RenderToHtml — headers
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_Header1_RendersH1ClassDiv()
    {
        // Arrange
        const string markdown = "# Title";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<div class=\"cc-ai-h1\">Title</div>");
    }

    [Fact]
    public void RenderToHtml_Header2_RendersH2ClassDiv()
    {
        // Arrange
        const string markdown = "## Section";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<div class=\"cc-ai-h2\">Section</div>");
    }

    [Fact]
    public void RenderToHtml_Header3_RendersH3ClassDiv()
    {
        // Arrange
        const string markdown = "### Subsection";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<div class=\"cc-ai-h3\">Subsection</div>");
    }

    // -------------------------------------------------------------------------
    // RenderToHtml — horizontal rule
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_HorizontalRule_RendersHrTag()
    {
        // Arrange
        const string markdown = "---";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().Contain("<hr/>");
    }

    // -------------------------------------------------------------------------
    // RenderToHtml — XSS prevention
    // -------------------------------------------------------------------------

    [Fact]
    public void RenderToHtml_HtmlInput_EncodesTagsAndPreventsXss()
    {
        // Arrange
        const string markdown = "<script>alert('xss')</script>";

        // Act
        var result = _sut.RenderToHtml(markdown);

        // Assert
        result.Should().NotContain("<script>",
            because: "raw HTML in markdown input must be entity-encoded to prevent XSS");
        result.Should().Contain("&lt;script&gt;",
            because: "angle brackets must be replaced with their HTML entities");
    }

    // -------------------------------------------------------------------------
    // FormatToolName — known tools
    // -------------------------------------------------------------------------

    [Fact]
    public void FormatToolName_ListClients_ReturnsFriendlyName()
    {
        // Arrange / Act
        var result = _sut.FormatToolName("list_clients");

        // Assert
        result.Should().Be("clients");
    }

    [Fact]
    public void FormatToolName_CreateAppointment_ReturnsFriendlyName()
    {
        // Arrange / Act
        var result = _sut.FormatToolName("create_appointment");

        // Assert
        result.Should().Be("creating appointment");
    }

    [Fact]
    public void FormatToolName_GetDashboard_ReturnsFriendlyName()
    {
        // Arrange / Act
        var result = _sut.FormatToolName("get_dashboard");

        // Assert
        result.Should().Be("dashboard data");
    }

    // -------------------------------------------------------------------------
    // FormatToolName — known tools (Theory coverage)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("get_client", "client details")]
    [InlineData("update_client", "updating client")]
    [InlineData("delete_client", "deleting client")]
    [InlineData("list_appointments", "appointments")]
    [InlineData("get_appointment", "appointment details")]
    [InlineData("update_appointment", "updating appointment")]
    [InlineData("cancel_appointment", "cancelling appointment")]
    [InlineData("delete_appointment", "deleting appointment")]
    [InlineData("list_meal_plans", "meal plans")]
    [InlineData("get_meal_plan", "meal plan details")]
    [InlineData("create_meal_plan", "creating meal plan")]
    [InlineData("update_meal_plan", "updating meal plan")]
    [InlineData("activate_meal_plan", "activating meal plan")]
    [InlineData("archive_meal_plan", "archiving meal plan")]
    [InlineData("duplicate_meal_plan", "duplicating meal plan")]
    [InlineData("delete_meal_plan", "deleting meal plan")]
    [InlineData("list_goals", "goals")]
    [InlineData("get_goal", "goal details")]
    [InlineData("create_goal", "creating goal")]
    [InlineData("update_goal", "updating goal")]
    [InlineData("achieve_goal", "achieving goal")]
    [InlineData("abandon_goal", "abandoning goal")]
    [InlineData("delete_goal", "deleting goal")]
    [InlineData("list_progress", "progress entries")]
    [InlineData("get_progress_entry", "progress details")]
    [InlineData("create_progress_entry", "creating progress entry")]
    [InlineData("delete_progress_entry", "deleting progress entry")]
    [InlineData("list_users", "users")]
    [InlineData("get_user", "user details")]
    [InlineData("create_user", "creating user")]
    [InlineData("change_user_role", "changing user role")]
    [InlineData("deactivate_user", "deactivating user")]
    [InlineData("reactivate_user", "reactivating user")]
    [InlineData("reset_user_password", "resetting password")]
    [InlineData("search", "search results")]
    [InlineData("create_client", "creating client")]
    public void FormatToolName_KnownTool_ReturnsMappedFriendlyName(string toolName, string expected)
    {
        // Arrange / Act
        var result = _sut.FormatToolName(toolName);

        // Assert
        result.Should().Be(expected,
            because: $"'{toolName}' has an explicit friendly-name mapping");
    }

    // -------------------------------------------------------------------------
    // FormatToolName — unknown tools fall back to underscore replacement
    // -------------------------------------------------------------------------

    [Fact]
    public void FormatToolName_UnknownTool_ReplacesUnderscoresWithSpaces()
    {
        // Arrange
        const string toolName = "some_unknown_tool";

        // Act
        var result = _sut.FormatToolName(toolName);

        // Assert
        result.Should().Be("some unknown tool",
            because: "unmapped tool names fall back to replacing underscores with spaces");
    }

    [Theory]
    [InlineData("single", "single")]
    [InlineData("two_words", "two words")]
    [InlineData("three_part_name", "three part name")]
    public void FormatToolName_UnknownTool_HandlesVariousUnderscoredNames(string toolName, string expected)
    {
        // Arrange / Act
        var result = _sut.FormatToolName(toolName);

        // Assert
        result.Should().Be(expected);
    }
}
