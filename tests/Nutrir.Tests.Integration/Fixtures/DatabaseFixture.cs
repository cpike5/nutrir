using Xunit;
using Microsoft.EntityFrameworkCore;
using Nutrir.Infrastructure.Data;
using Testcontainers.PostgreSql;

namespace Nutrir.Tests.Integration.Fixtures;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task<AppDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        var context = new AppDbContext(options);
        return Task.FromResult(context);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var context = await CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
