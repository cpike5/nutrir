using Nutrir.Core.Enums;

namespace Nutrir.Core.Allergens;

public static class AllergenKeywordMap
{
    private static readonly Dictionary<AllergenCategory, string[]> Keywords = new()
    {
        [AllergenCategory.Gluten] = ["gluten", "wheat", "barley", "rye", "oat", "spelt", "kamut", "triticale", "semolina", "durum", "farro", "couscous", "bulgur", "seitan"],
        [AllergenCategory.Crustacean] = ["crustacean", "shrimp", "prawn", "crab", "lobster", "crayfish", "crawfish", "langoustine", "scampi"],
        [AllergenCategory.Egg] = ["egg", "eggs", "albumin", "meringue", "mayonnaise", "mayo", "quiche", "custard", "eggnog"],
        [AllergenCategory.Fish] = ["fish", "salmon", "tuna", "cod", "haddock", "trout", "mackerel", "sardine", "anchovy", "tilapia", "halibut", "bass", "swordfish", "catfish", "pollock", "snapper"],
        [AllergenCategory.Peanut] = ["peanut", "peanuts", "groundnut", "arachis"],
        [AllergenCategory.Soy] = ["soy", "soya", "soybean", "edamame", "tofu", "tempeh", "miso", "tamari", "soy sauce"],
        [AllergenCategory.Milk] = ["milk", "dairy", "cheese", "butter", "cream", "whey", "casein", "yogurt", "yoghurt", "ghee", "lactose", "curd", "paneer", "ricotta", "mozzarella", "parmesan", "cheddar", "brie", "gouda"],
        [AllergenCategory.TreeNut] = ["tree nut", "almond", "cashew", "walnut", "pecan", "pistachio", "hazelnut", "macadamia", "brazil nut", "chestnut", "pine nut", "praline", "marzipan", "nougat"],
        [AllergenCategory.Celery] = ["celery", "celeriac"],
        [AllergenCategory.Mustard] = ["mustard"],
        [AllergenCategory.Sesame] = ["sesame", "tahini", "halvah", "halva"],
        [AllergenCategory.Sulphite] = ["sulphite", "sulfite", "sulphur dioxide", "sulfur dioxide", "so2"],
        [AllergenCategory.Lupin] = ["lupin", "lupine", "lupini"],
        [AllergenCategory.Mollusc] = ["mollusc", "mollusk", "mussel", "clam", "oyster", "scallop", "squid", "calamari", "octopus", "snail", "escargot", "abalone"]
    };

    /// <summary>
    /// Returns all allergen categories whose keywords match the given food name.
    /// </summary>
    public static List<AllergenCategory> MatchFood(string foodName)
    {
        var lower = foodName.ToLowerInvariant();
        var matches = new List<AllergenCategory>();

        foreach (var (category, keywords) in Keywords)
        {
            foreach (var keyword in keywords)
            {
                if (lower.Contains(keyword))
                {
                    matches.Add(category);
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Maps a free-text allergy name (e.g. "Peanut allergy") to an AllergenCategory.
    /// Returns null if no category matches.
    /// </summary>
    public static AllergenCategory? MapAllergyNameToCategory(string allergyName)
    {
        var lower = allergyName.ToLowerInvariant();

        foreach (var (category, keywords) in Keywords)
        {
            foreach (var keyword in keywords)
            {
                if (lower.Contains(keyword))
                {
                    return category;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a food name directly matches an allergy name via substring (fallback for unmapped allergies).
    /// </summary>
    public static bool DirectMatch(string foodName, string allergyName)
    {
        var lowerFood = foodName.ToLowerInvariant();
        var lowerAllergy = allergyName.ToLowerInvariant();
        return lowerFood.Contains(lowerAllergy) || lowerAllergy.Contains(lowerFood);
    }
}
