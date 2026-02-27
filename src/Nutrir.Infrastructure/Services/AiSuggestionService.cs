using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class AiSuggestionService : IAiSuggestionService
{
    private static readonly string[] Starters =
    [
        "What appointments are today?",
        "Give me a practice overview",
        "Create a new client",
        "Schedule an appointment",
        "Search for a client by name",
        "List draft meal plans needing review",
        "Create a goal for a client",
        "Log a progress entry",
    ];

    private static readonly string[] GeneralSuggestions =
    [
        "What appointments are today?",
        "Give me a practice overview",
        "Create a new client",
        "Schedule an appointment",
        "Search for a client by name",
        "List draft meal plans needing review",
        "Create a goal for a client",
        "Log a progress entry",
        "Update a client",
        "List all appointments",
        "Cancel an appointment",
        "Create a meal plan",
        "Duplicate a meal plan",
        "Show dashboard overview",
        "List all goals",
        "Log a progress entry for a client",
        "Show all clients",
        "Reschedule an appointment",
        "Archive a meal plan",
        "Activate a meal plan",
        "Show upcoming appointments",
        "List overdue goals",
        "Search appointments by date",
        "Create an appointment for a client",
        "Show recent progress entries",
    ];

    private static readonly Dictionary<string, string[]> ContextSuggestions = new()
    {
        ["client"] =
        [
            "Show this client's appointments",
            "Create a meal plan for this client",
            "Show this client's goals",
            "Create a goal for this client",
            "Log a progress entry for this client",
            "Schedule an appointment for this client",
        ],
        ["appointment"] =
        [
            "Reschedule this appointment",
            "Cancel this appointment",
        ],
        ["meal_plan"] =
        [
            "Duplicate this meal plan",
            "Activate this meal plan",
            "Archive this meal plan",
        ],
    };

    public string[] GetStarters() => Starters;

    public List<string> GetSuggestions(string query, string? pageEntityType)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var corpus = new List<string>();

        // Add context-aware suggestions first (higher priority)
        if (pageEntityType is not null && ContextSuggestions.TryGetValue(pageEntityType, out var contextItems))
        {
            corpus.AddRange(contextItems);
        }

        corpus.AddRange(GeneralSuggestions);

        return corpus
            .Where(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }
}
