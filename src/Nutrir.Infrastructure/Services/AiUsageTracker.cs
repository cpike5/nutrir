using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AiUsageTracker : IAiUsageTracker
{
    private readonly AppDbContext _db;

    public AiUsageTracker(AppDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(string userId, int inputTokens, int outputTokens, int toolCallCount, int durationMs, string? model)
    {
        _db.AiUsageLogs.Add(new AiUsageLog
        {
            UserId = userId,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            ToolCallCount = toolCallCount,
            DurationMs = durationMs,
            Model = model,
        });
        await _db.SaveChangesAsync();
    }

    public async Task<List<AiUsageSummary>> GetSummaryAsync(DateTime from, DateTime to)
    {
        var summaries = await _db.AiUsageLogs
            .Where(l => l.RequestedAt >= from && l.RequestedAt <= to)
            .GroupBy(l => l.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                TotalRequests = g.Count(),
                TotalInputTokens = g.Sum(l => l.InputTokens),
                TotalOutputTokens = g.Sum(l => l.OutputTokens),
                LastActive = g.Max(l => l.RequestedAt),
            })
            .ToListAsync();

        // Batch-load user info
        var userIds = summaries.Select(s => s.UserId).ToList();
        var users = await _db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })
            .ToDictionaryAsync(u => u.Id);

        return summaries
            .Select(s =>
            {
                users.TryGetValue(s.UserId, out var user);
                return new AiUsageSummary(
                    s.UserId,
                    user is not null ? $"{user.FirstName} {user.LastName}" : null,
                    user?.Email,
                    s.TotalRequests,
                    s.TotalInputTokens,
                    s.TotalOutputTokens,
                    s.LastActive);
            })
            .OrderByDescending(s => s.TotalRequests)
            .ToList();
    }

    public async Task<List<AiDailyUsage>> GetDailyUsageAsync(string userId, DateTime from, DateTime to)
    {
        return await _db.AiUsageLogs
            .Where(l => l.UserId == userId && l.RequestedAt >= from && l.RequestedAt <= to)
            .GroupBy(l => l.RequestedAt.Date)
            .Select(g => new AiDailyUsage(
                DateOnly.FromDateTime(g.Key),
                g.Count(),
                g.Sum(l => l.InputTokens),
                g.Sum(l => l.OutputTokens),
                g.Sum(l => l.ToolCallCount),
                (int)g.Average(l => l.DurationMs)))
            .OrderByDescending(d => d.Date)
            .ToListAsync();
    }
}
