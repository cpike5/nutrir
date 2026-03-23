using FluentAssertions;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services.Ai;

public class AiSuggestionServiceTests
{
    private readonly AiSuggestionService _sut = new();

    // -------------------------------------------------------------------------
    // GetStarters
    // -------------------------------------------------------------------------

    [Fact]
    public void GetStarters_ReturnsEightItems()
    {
        // Arrange / Act
        var starters = _sut.GetStarters();

        // Assert
        starters.Should().HaveCount(8);
    }

    [Theory]
    [InlineData("What appointments are today?")]
    [InlineData("Give me a practice overview")]
    [InlineData("Create a new client")]
    [InlineData("Log a progress entry")]
    public void GetStarters_ContainsExpectedSuggestions(string expectedStarter)
    {
        // Arrange / Act
        var starters = _sut.GetStarters();

        // Assert
        starters.Should().Contain(expectedStarter);
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — empty / whitespace input guards
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_EmptyQuery_ReturnsEmptyList()
    {
        // Arrange / Act
        var result = _sut.GetSuggestions(string.Empty, pageEntityType: null);

        // Assert
        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void GetSuggestions_WhitespaceQuery_ReturnsEmptyList(string whitespaceQuery)
    {
        // Arrange / Act
        var result = _sut.GetSuggestions(whitespaceQuery, pageEntityType: null);

        // Assert
        result.Should().BeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — substring / case-insensitive matching
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_MatchesSubstring_CaseInsensitive()
    {
        // Arrange — "APPOINTMENT" should match any item containing "appointment"
        const string query = "APPOINTMENT";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: null);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(s =>
            s.Contains("appointment", StringComparison.OrdinalIgnoreCase).Should().BeTrue(
                because: $"every returned suggestion must match the query '{query}'"));
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — context-aware suggestions
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_WithClientContext_IncludesContextSuggestions()
    {
        // Arrange — "this client" is present in several client-context suggestions
        const string query = "this client";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: "client");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(s =>
            s.Contains("this client", StringComparison.OrdinalIgnoreCase),
            because: "client context suggestions include 'this client' phrases");
    }

    [Fact]
    public void GetSuggestions_WithAppointmentContext_IncludesContextSuggestions()
    {
        // Arrange — "this appointment" appears in appointment-context suggestions
        const string query = "this appointment";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: "appointment");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(s =>
            s.Contains("this appointment", StringComparison.OrdinalIgnoreCase),
            because: "appointment context suggestions include 'this appointment' phrases");
    }

    [Fact]
    public void GetSuggestions_WithMealPlanContext_IncludesContextSuggestions()
    {
        // Arrange — "this meal plan" appears in meal_plan-context suggestions
        const string query = "this meal plan";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: "meal_plan");

        // Assert
        result.Should().NotBeEmpty();
        result.Should().Contain(s =>
            s.Contains("this meal plan", StringComparison.OrdinalIgnoreCase),
            because: "meal_plan context suggestions include 'this meal plan' phrases");
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — unknown context falls back to general corpus
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_UnknownContext_FallsBackToGeneral()
    {
        // Arrange — "nonexistent" is not a registered context key; "appointment"
        // still has matches in the general suggestions corpus.
        const string query = "appointment";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: "nonexistent_context_type");

        // Assert — results come from the general corpus rather than being empty
        result.Should().NotBeEmpty(
            because: "an unknown context type should fall back to the general suggestions list");
        result.Should().AllSatisfy(s =>
            s.Contains("appointment", StringComparison.OrdinalIgnoreCase).Should().BeTrue());
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — result cap
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_ReturnsMaxFiveItems()
    {
        // Arrange — "a" is a very common substring that matches many suggestions
        // across both the client context corpus and the general corpus.
        const string query = "a";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: "client");

        // Assert
        result.Should().HaveCountLessOrEqualTo(5,
            because: "GetSuggestions caps results at 5 items regardless of corpus size");
    }

    // -------------------------------------------------------------------------
    // GetSuggestions — no match
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSuggestions_NoMatch_ReturnsEmptyList()
    {
        // Arrange — "zzzzz" will not appear in any suggestion string
        const string query = "zzzzz";

        // Act
        var result = _sut.GetSuggestions(query, pageEntityType: null);

        // Assert
        result.Should().BeEmpty(
            because: "no suggestion in any corpus contains the query string 'zzzzz'");
    }
}
