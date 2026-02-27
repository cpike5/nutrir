namespace Nutrir.Core.Interfaces;

public interface IAiSuggestionService
{
    /// <summary>
    /// Returns conversation starter prompts for an empty chat.
    /// </summary>
    string[] GetStarters();

    /// <summary>
    /// Filters suggestions by query text, combining context-aware entries with general suggestions.
    /// Returns up to 5 matches.
    /// </summary>
    List<string> GetSuggestions(string query, string? pageEntityType);
}
