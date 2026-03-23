using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AiConversationPurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiConversationPurgeService> _logger;
    private readonly AiRetentionOptions _options;

    public AiConversationPurgeService(
        IServiceScopeFactory scopeFactory,
        ILogger<AiConversationPurgeService> logger,
        IOptions<AiRetentionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.PurgeIntervalMinutes);
        _logger.LogInformation("AI conversation purge service starting with interval of {IntervalMinutes} minutes", _options.PurgeIntervalMinutes);

        // Initial delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Run immediately on startup, then periodically
        await PurgeExpiredConversationsAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await PurgeExpiredConversationsAsync(stoppingToken);
        }
    }

    private async Task PurgeExpiredConversationsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var threshold = DateTime.UtcNow.AddDays(-_options.PurgeThresholdDays);
            var conversations = await db.AiConversations
                .Where(c => c.LastMessageAt < threshold)
                .ToListAsync(ct);

            if (conversations.Count == 0) return;

            db.AiConversations.RemoveRange(conversations);
            await db.SaveChangesAsync(ct);

            await auditLogService.LogAsync(
                "system",
                "AiConversationRetentionPurge",
                "AiConversation",
                "",
                $"Purged {conversations.Count} AI conversations older than {_options.PurgeThresholdDays} days");

            _logger.LogInformation("Purged {Count} AI conversations older than {ThresholdDays} days", conversations.Count, _options.PurgeThresholdDays);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during AI conversation purge");
        }
    }
}
