using FluentAssertions;
using Nutrir.Core.Allergens;
using Nutrir.Core.Enums;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AllergenKeywordMapTests
{
    // ---------------------------------------------------------------------------
    // MatchFood
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("peanut butter sandwich", AllergenCategory.Peanut)]
    [InlineData("shrimp tempura", AllergenCategory.Crustacean)]
    [InlineData("tofu stir fry", AllergenCategory.Soy)]
    [InlineData("cheese omelette", AllergenCategory.Milk)]
    [InlineData("sesame chicken", AllergenCategory.Sesame)]
    [InlineData("walnut brownie", AllergenCategory.TreeNut)]
    [InlineData("egg salad", AllergenCategory.Egg)]
    [InlineData("salmon fillet", AllergenCategory.Fish)]
    [InlineData("wheat bread", AllergenCategory.Gluten)]
    [InlineData("celery sticks", AllergenCategory.Celery)]
    [InlineData("mustard dressing", AllergenCategory.Mustard)]
    [InlineData("lupin flour", AllergenCategory.Lupin)]
    [InlineData("calamari rings", AllergenCategory.Mollusc)]
    public void MatchFood_WithSingleAllergen_ReturnsExpectedCategory(string foodName, AllergenCategory expected)
    {
        var result = AllergenKeywordMap.MatchFood(foodName);

        result.Should().Contain(expected);
    }

    [Fact]
    public void MatchFood_WithMultipleAllergens_ReturnsAllMatchingCategories()
    {
        // "peanut butter on wheat bread" contains peanut + gluten
        var result = AllergenKeywordMap.MatchFood("peanut butter on wheat bread");

        result.Should().Contain(AllergenCategory.Peanut);
        result.Should().Contain(AllergenCategory.Gluten);
        result.Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public void MatchFood_WithNoAllergens_ReturnsEmptyList()
    {
        var result = AllergenKeywordMap.MatchFood("grilled chicken breast");

        result.Should().BeEmpty();
    }

    [Fact]
    public void MatchFood_IsCaseInsensitive()
    {
        var result = AllergenKeywordMap.MatchFood("PEANUT BUTTER");

        result.Should().Contain(AllergenCategory.Peanut);
    }

    [Fact]
    public void MatchFood_WithMixedCase_ReturnsCorrectCategories()
    {
        var result = AllergenKeywordMap.MatchFood("Shrimp Cocktail");

        result.Should().Contain(AllergenCategory.Crustacean);
    }

    // ---------------------------------------------------------------------------
    // MapAllergyNameToCategory
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("Peanut allergy", AllergenCategory.Peanut)]
    [InlineData("milk intolerance", AllergenCategory.Milk)]
    [InlineData("gluten sensitivity", AllergenCategory.Gluten)]
    [InlineData("shellfish (shrimp)", AllergenCategory.Crustacean)]
    [InlineData("egg allergy", AllergenCategory.Egg)]
    public void MapAllergyNameToCategory_WithKnownAllergy_ReturnsCategory(string allergyName, AllergenCategory expected)
    {
        var result = AllergenKeywordMap.MapAllergyNameToCategory(allergyName);

        result.Should().Be(expected);
    }

    [Fact]
    public void MapAllergyNameToCategory_WithUnknownAllergy_ReturnsNull()
    {
        var result = AllergenKeywordMap.MapAllergyNameToCategory("unknown allergy xyz");

        result.Should().BeNull();
    }

    [Fact]
    public void MapAllergyNameToCategory_IsCaseInsensitive()
    {
        var result = AllergenKeywordMap.MapAllergyNameToCategory("PEANUT ALLERGY");

        result.Should().Be(AllergenCategory.Peanut);
    }

    // ---------------------------------------------------------------------------
    // DirectMatch
    // ---------------------------------------------------------------------------

    [Fact]
    public void DirectMatch_WhenFoodContainsAllergyName_ReturnsTrue()
    {
        var result = AllergenKeywordMap.DirectMatch("salmon fillet", "salmon");

        result.Should().BeTrue();
    }

    [Fact]
    public void DirectMatch_WhenAllergyNameContainsFood_ReturnsTrue()
    {
        // e.g., allergy name is "salmon and tuna", food is "salmon"
        var result = AllergenKeywordMap.DirectMatch("salmon", "salmon and tuna");

        result.Should().BeTrue();
    }

    [Fact]
    public void DirectMatch_WhenNoOverlap_ReturnsFalse()
    {
        var result = AllergenKeywordMap.DirectMatch("chicken breast", "salmon");

        result.Should().BeFalse();
    }

    [Fact]
    public void DirectMatch_IsCaseInsensitive()
    {
        var result = AllergenKeywordMap.DirectMatch("SALMON Fillet", "salmon");

        result.Should().BeTrue();
    }
}
