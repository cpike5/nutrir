using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class RetentionTracker : IRetentionTracker
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<RetentionTracker> _logger;

    public RetentionTracker(
        IDbContextFactory<AppDbContext> dbContextFactory,
        ILogger<RetentionTracker> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task UpdateLastInteractionAsync(int clientId)
    {
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync();
            var client = await db.Clients.FindAsync(clientId);
            if (client is null) return;

            client.LastInteractionDate = DateTime.UtcNow;
            client.RetentionExpiresAt = DateTime.UtcNow.AddYears(client.RetentionYears);
            client.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update retention tracking for client {ClientId}", clientId);
        }
    }
}
