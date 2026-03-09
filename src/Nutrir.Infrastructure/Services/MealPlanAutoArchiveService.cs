using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class MealPlanAutoArchiveService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MealPlanAutoArchiveService> _logger;

    public MealPlanAutoArchiveService(IServiceScopeFactory scopeFactory, ILogger<MealPlanAutoArchiveService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let the app start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        // Run immediately on startup, then periodically
        await ArchiveExpiredPlansAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ArchiveExpiredPlansAsync(stoppingToken);
        }
    }

    private async Task ArchiveExpiredPlansAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbContextFactory.CreateDbContextAsync(ct);
            var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var expiredPlans = await db.MealPlans
                .Where(p => p.Status == MealPlanStatus.Active && p.EndDate.HasValue && p.EndDate.Value < today)
                .ToListAsync(ct);

            if (expiredPlans.Count == 0) return;

            foreach (var plan in expiredPlans)
            {
                plan.Status = MealPlanStatus.Archived;
                plan.UpdatedAt = DateTime.UtcNow;

                await auditLogService.LogAsync(
                    "system",
                    "MealPlanAutoArchived",
                    "MealPlan",
                    plan.Id.ToString(),
                    $"Auto-archived expired meal plan '{plan.Title}' (ended {plan.EndDate})");
            }

            await db.SaveChangesAsync(ct);

            _logger.LogInformation("Auto-archived {Count} expired meal plans", expiredPlans.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error during meal plan auto-archive");
        }
    }
}
