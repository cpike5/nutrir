using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class AiContentStrippingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiContentStrippingService> _logger;
    private readonly AiRetentionOptions _options;

    public AiContentStrippingService(
        IServiceScopeFactory scopeFactory,
        ILogger<AiContentStrippingService> logger,
        IOptions<AiRetentionOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.ContentStripIntervalMinutes);
        _logger.LogInformation("AI content stripping service starting with interval of {IntervalMinutes} minutes", _options.ContentStripIntervalMinutes);

        // Initial delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Run immediately on startup, then periodically
        await StripExpiredContentAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await StripExpiredContentAsync(stoppingToken);
        }
    }

    private async Task StripExpiredContentAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var threshold = DateTime.UtcNow.AddHours(-_options.ContentStripThresholdHours);
            var staleConversationIds = await db.AiConversations
                .Where(c => c.LastMessageAt < threshold)
                .Select(c => c.Id)
                .ToListAsync(ct);

            if (staleConversationIds.Count == 0) return;

            var messages = await db.AiConversationMessages
                .Where(m => m.ContentJson != ""
                    && staleConversationIds.Contains(m.ConversationId))
                .ToListAsync(ct);

            if (messages.Count == 0) return;

            foreach (var message in messages)
            {
                message.ContentJson = "";
            }

            await db.SaveChangesAsync(ct);

            await auditLogService.LogAsync(
                "system",
                "AiConversationContentStripped",
                "AiConversationMessage",
                "",
                $"Stripped content from {messages.Count} AI conversation messages older than {_options.ContentStripThresholdHours} hours");

            _logger.LogInformation("Stripped content from {Count} AI conversation messages", messages.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during AI content stripping");
        }
    }
}
