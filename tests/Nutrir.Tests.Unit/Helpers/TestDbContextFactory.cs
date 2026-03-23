using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Tests.Unit.Helpers;

/// <summary>
/// Creates an in-memory SQLite AppDbContext suitable for unit tests.
///
/// The production AppDbContext contains PostgreSQL-specific model configuration
/// (e.g. HasDefaultValueSql("now() at time zone 'utc'") and xmin row-version
/// properties) that SQLite cannot parse.  TestAppDbContext strips those after
/// the base model is built so that EnsureCreated() succeeds.
/// </summary>
public static class TestDbContextFactory
{
    public static (AppDbContext Context, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new TestAppDbContext(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}

/// <summary>
/// Subclass of AppDbContext that replaces PostgreSQL-only model metadata with
/// SQLite-compatible equivalents so the schema can be created in tests.
/// </summary>
/// <summary>
/// Factory that creates NEW TestAppDbContext instances sharing the same SQLite connection.
/// Required for testing services that create/dispose their own context per method call
/// via IDbContextFactory. The shared connection keeps schema and data intact across
/// multiple disposals.
/// </summary>
internal sealed class SharedConnectionContextFactory(SqliteConnection connection) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        return new TestAppDbContext(options);
    }

    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(CreateDbContext());
}

internal sealed class TestAppDbContext(DbContextOptions<AppDbContext> options) : AppDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Let the production configuration run first
        base.OnModelCreating(builder);

        // Iterate all entity types and strip incompatible metadata
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                // Replace PostgreSQL-specific SQL default with a CLR default value
                // so SQLite DDL generation succeeds.
                if (property.GetDefaultValueSql() is { } sql
                    && sql.Contains("at time zone", StringComparison.OrdinalIgnoreCase))
                {
                    property.SetDefaultValueSql(null);
                    // Use DateTime.UtcNow as the CLR-side default instead
                    if (property.ClrType == typeof(DateTime))
                        property.SetDefaultValue(DateTime.UtcNow);
                }

                // xmin is a PostgreSQL system column configured as a row version.
                // SQLite cannot represent uint row versions, so reset it to a plain
                // non-generated, non-concurrency-token property.
                if (property.Name == "xmin")
                {
                    property.ValueGenerated = ValueGenerated.Never;
                    property.IsConcurrencyToken = false;
                }
            }
        }
    }
}
