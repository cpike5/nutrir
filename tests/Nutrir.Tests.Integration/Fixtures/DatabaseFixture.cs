using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Security;
using Testcontainers.PostgreSql;

namespace Nutrir.Tests.Integration.Fixtures;

public class DatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public IDbContextFactory<AppDbContext> DbContextFactory { get; private set; } = null!;

    public Task<AppDbContext> CreateDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        var context = new AppDbContext(options);
        return Task.FromResult(context);
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Initialize field-level encryption with a test key before migration
        // so that EF Core value converters can resolve AesGcmFieldEncryptor.Instance
        var testKey = Convert.ToBase64String(new byte[32]);
        var encryptionOptions = new EncryptionOptions
        {
            Key = testKey,
            KeyVersion = 1,
            Enabled = true
        };
        AesGcmFieldEncryptor.Instance = new AesGcmFieldEncryptor(
            Options.Create(encryptionOptions),
            NullLogger<AesGcmFieldEncryptor>.Instance);

        DbContextFactory = new IntegrationDbContextFactory(ConnectionString);

        await using var context = await CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }

    public async Task ResetDatabaseAsync()
    {
        await using var context = await CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // Use raw ADO.NET to query table names (EF SqlQueryRaw<string> has column mapping issues)
        var tableNames = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                  AND table_type = 'BASE TABLE'
                  AND table_name != '__EFMigrationsHistory'
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                tableNames.Add(reader.GetString(0));
        }

        if (tableNames.Count > 0)
        {
            var quoted = string.Join(", ", tableNames.Select(t => $"\"{t}\""));
            await using var truncateCmd = connection.CreateCommand();
            truncateCmd.CommandText = $"TRUNCATE {quoted} CASCADE";
            await truncateCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public class IntegrationDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly string _connectionString;

        public IntegrationDbContextFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public AppDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_connectionString)
                .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
                .Options;

            return new AppDbContext(options);
        }
    }
}

[CollectionDefinition("Database")]
public class DatabaseCollection : ICollectionFixture<DatabaseFixture> { }
