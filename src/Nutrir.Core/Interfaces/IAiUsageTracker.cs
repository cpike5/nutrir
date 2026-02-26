namespace Nutrir.Core.Interfaces;

public record AiUsageSummary(
    string UserId,
    string? UserName,
    string? Email,
    int TotalRequests,
    int TotalInputTokens,
    int TotalOutputTokens,
    DateTime? LastActive);

public record AiDailyUsage(
    DateOnly Date,
    int Requests,
    int InputTokens,
    int OutputTokens,
    int ToolCalls,
    int AvgDurationMs);

public interface IAiUsageTracker
{
    Task LogAsync(string userId, int inputTokens, int outputTokens, int toolCallCount, int durationMs, string? model);
    Task<List<AiUsageSummary>> GetSummaryAsync(DateTime from, DateTime to);
    Task<List<AiDailyUsage>> GetDailyUsageAsync(string userId, DateTime from, DateTime to);
}
