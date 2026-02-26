namespace Nutrir.Core.Interfaces;

public enum ConfirmationTier { Standard, Elevated }

public record ToolConfirmationRequest(
    string ToolCallId,
    string ToolName,
    string Description,
    ConfirmationTier Tier
);

public record AgentStreamEvent
{
    public string? TextDelta { get; init; }
    public string? ToolName { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
    public ToolConfirmationRequest? ConfirmationRequest { get; init; }
}

public interface IAiAgentService
{
    IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default);
    void ClearHistory();
    void SetUserContext(string userName, string userRole);
    void SetUserId(string userId);
    void RespondToConfirmation(string toolCallId, bool allowed);
    void SetPageContext(string? entityType, string? entityId);
}
