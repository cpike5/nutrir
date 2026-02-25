namespace Nutrir.Core.Interfaces;

public record AgentStreamEvent
{
    public string? TextDelta { get; init; }
    public string? ToolName { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
}

public interface IAiAgentService
{
    IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(string userMessage, CancellationToken cancellationToken = default);
    void ClearHistory();
}
