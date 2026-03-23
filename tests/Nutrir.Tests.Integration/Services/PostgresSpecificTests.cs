using System.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Data;
using Nutrir.Tests.Integration.Fixtures;
using Xunit;

namespace Nutrir.Tests.Integration.Services;

/// <summary>
/// Tests for Postgres-specific EF Core behaviors that cannot be replicated with SQLite
/// or in-memory providers. Each test class method creates its own short-lived DbContext
/// instances to avoid shared-state concurrency issues.
/// </summary>
[Collection("Database")]
public class PostgresSpecificTests : IAsyncLifetime
{
    private readonly DatabaseFixture _fixture;

    public PostgresSpecificTests(DatabaseFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates an ApplicationUser and persists it so that FK constraints on
    /// Client.PrimaryNutritionistId and Appointment.NutritionistId are satisfied.
    /// </summary>
    private static async Task<ApplicationUser> SeedNutritionistAsync(AppDbContext db)
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var email = $"nutritionist_{uniqueId}@test.com";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName = "Test",
            LastName = "Nutritionist",
            DisplayName = "Test Nutritionist",
            SecurityStamp = Guid.NewGuid().ToString()
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>
    /// Creates a Client with the minimum required fields and persists it.
    /// CreatedAt is intentionally left at default so the Postgres default expression can fire.
    /// </summary>
    private static async Task<Client> SeedClientAsync(AppDbContext db, string nutritionistId)
    {
        var client = new Client
        {
            FirstName = "Jane",
            LastName = "Doe",
            PrimaryNutritionistId = nutritionistId,
            // CreatedAt deliberately left as default to test DB-side default expression
            CreatedAt = default
        };
        db.Clients.Add(client);
        await db.SaveChangesAsync();
        return client;
    }

    // ===========================================================================
    // 1. xmin Row Versioning (Optimistic Concurrency)
    // ===========================================================================

    [Fact]
    public async Task XminRowVersion_WhenTwoContextsUpdateSameClient_SecondSaveThrowsDbUpdateConcurrencyException()
    {
        // Arrange – seed the client in a dedicated context that is then disposed
        await using var seedDb = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(seedDb);
        var seeded = await SeedClientAsync(seedDb, nutritionist.Id);
        var clientId = seeded.Id;

        // Act – load the same row in two independent contexts
        await using var context1 = await _fixture.CreateDbContextAsync();
        await using var context2 = await _fixture.CreateDbContextAsync();

        // Both contexts read the row; they capture the same xmin value
        var clientInCtx1 = await context1.Clients.SingleAsync(c => c.Id == clientId);
        var clientInCtx2 = await context2.Clients.SingleAsync(c => c.Id == clientId);

        // context1 updates and commits first – this bumps xmin on the Postgres side
        clientInCtx1.FirstName = "UpdatedByContext1";
        await context1.SaveChangesAsync();

        // context2 still has the stale xmin token; its update should be rejected
        clientInCtx2.FirstName = "UpdatedByContext2";
        var act = async () => await context2.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateConcurrencyException>(
            because: "context2 holds a stale xmin row version and Postgres should reject the update");
    }

    // ===========================================================================
    // 2. Timestamp Default Expression (now() at time zone 'utc')
    // ===========================================================================

    [Fact]
    public async Task TimestampDefault_WhenClientInsertedWithDefaultCreatedAt_PostgresPopulatesCreatedAtNearNow()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-2);

        await using var writeDb = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(writeDb);

        // Insert with CreatedAt = default (DateTime.MinValue) so the C# value is
        // deliberately wrong; the DB default expression should override it.
        var client = new Client
        {
            FirstName = "Default",
            LastName = "Timestamp",
            PrimaryNutritionistId = nutritionist.Id,
            CreatedAt = default
        };
        writeDb.Clients.Add(client);
        await writeDb.SaveChangesAsync();

        var after = DateTime.UtcNow.AddSeconds(2);

        // Act – re-read from a fresh context so there is no cached value
        await using var readDb = await _fixture.CreateDbContextAsync();
        var persisted = await readDb.Clients
            .IgnoreQueryFilters()
            .SingleAsync(c => c.Id == client.Id);

        // Assert
        persisted.CreatedAt.Kind.Should().Be(DateTimeKind.Utc,
            because: "Npgsql maps timestamptz to UTC DateTime");
        persisted.CreatedAt.Should().BeOnOrAfter(before,
            because: "the Postgres default expression sets CreatedAt to the current UTC time");
        persisted.CreatedAt.Should().BeOnOrBefore(after,
            because: "the Postgres default expression should fire at INSERT time");
    }

    // ===========================================================================
    // 3. Soft-Delete Query Filters
    // ===========================================================================

    [Fact]
    public async Task SoftDeleteFilter_NormalQuery_ExcludesSoftDeletedClient()
    {
        // Arrange – insert 3 clients
        await using var db = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(db);

        for (var i = 0; i < 3; i++)
        {
            db.Clients.Add(new Client
            {
                FirstName = $"Client{i}",
                LastName = "Filter",
                PrimaryNutritionistId = nutritionist.Id
            });
        }
        await db.SaveChangesAsync();

        // Soft-delete the first client
        var toDelete = await db.Clients.FirstAsync();
        toDelete.IsDeleted = true;
        toDelete.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Act – normal query (filter active) vs bypass filter
        await using var readDb = await _fixture.CreateDbContextAsync();
        var visibleCount = await readDb.Clients.CountAsync();
        var totalCount = await readDb.Clients.IgnoreQueryFilters().CountAsync();

        // Assert
        visibleCount.Should().Be(2,
            because: "the global query filter should hide the one soft-deleted client");
        totalCount.Should().Be(3,
            because: "IgnoreQueryFilters() should return all rows including soft-deleted ones");
    }

    [Fact]
    public async Task SoftDeleteFilter_IgnoreQueryFilters_ReturnsAllIncludingDeleted()
    {
        // Arrange
        await using var db = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(db);

        for (var i = 0; i < 3; i++)
        {
            db.Clients.Add(new Client
            {
                FirstName = $"Client{i}",
                LastName = "All",
                PrimaryNutritionistId = nutritionist.Id
            });
        }
        await db.SaveChangesAsync();

        // Soft-delete all 3
        var clients = await db.Clients.ToListAsync();
        foreach (var c in clients)
        {
            c.IsDeleted = true;
            c.DeletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();

        // Act
        await using var readDb = await _fixture.CreateDbContextAsync();
        var withFilter = await readDb.Clients.CountAsync();
        var withoutFilter = await readDb.Clients.IgnoreQueryFilters().CountAsync();

        // Assert
        withFilter.Should().Be(0, because: "all clients are soft-deleted and the filter is active");
        withoutFilter.Should().Be(3, because: "IgnoreQueryFilters bypasses the soft-delete filter");
    }

    // ===========================================================================
    // 4. Transaction Isolation (Serializable)
    // ===========================================================================

    [Fact]
    public async Task TransactionIsolation_UncommittedInsertNotVisibleToOtherContext()
    {
        // Arrange
        await using var txDb = await _fixture.CreateDbContextAsync();
        await using var observerDb = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(txDb);

        // Act – begin a serializable transaction but do not commit yet
        await using var tx = await txDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var client = new Client
        {
            FirstName = "Uncommitted",
            LastName = "Transaction",
            PrimaryNutritionistId = nutritionist.Id
        };
        txDb.Clients.Add(client);
        await txDb.SaveChangesAsync();

        // A separate context with no knowledge of the transaction should see 0 clients
        var countBeforeCommit = await observerDb.Clients.CountAsync();

        // Commit the transaction
        await tx.CommitAsync();

        // Now the observer must see the new client (use a fresh context to bypass
        // any first-level cache the observerDb might hold)
        await using var afterDb = await _fixture.CreateDbContextAsync();
        var countAfterCommit = await afterDb.Clients.CountAsync();

        // Assert
        countBeforeCommit.Should().Be(0,
            because: "the client was inserted inside an uncommitted serializable transaction");
        countAfterCommit.Should().Be(1,
            because: "after the transaction is committed the client is visible to new readers");
    }

    // ===========================================================================
    // 5. Concurrent DbContext Access (IDbContextFactory pattern)
    // ===========================================================================

    [Fact]
    public async Task ConcurrentDbContextAccess_TwoFactoryContextsReadSimultaneously_BothSucceedWithoutConcurrencyError()
    {
        // Arrange – seed some data so the reads are non-trivial
        await using var seedDb = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(seedDb);
        await SeedClientAsync(seedDb, nutritionist.Id);
        await SeedClientAsync(seedDb, nutritionist.Id);

        // Act – use the factory (not shared contexts) and Task.WhenAll
        // This is safe because each context is an independent database connection
        var task1 = Task.Run(async () =>
        {
            await using var ctx = _fixture.DbContextFactory.CreateDbContext();
            return await ctx.Clients.CountAsync();
        });

        var task2 = Task.Run(async () =>
        {
            await using var ctx = _fixture.DbContextFactory.CreateDbContext();
            return await ctx.Clients.CountAsync();
        });

        int[] results = null!;
        var act = async () => { results = await Task.WhenAll(task1, task2); };

        // Assert – no exception, both tasks return the same count
        await act.Should().NotThrowAsync(
            because: "IDbContextFactory creates independent contexts so parallel reads are safe");
        results.Should().HaveCount(2);
        results[0].Should().Be(results[1],
            because: "both contexts read the same committed data");
    }

    // ===========================================================================
    // 6. Enum-to-String Conversion
    // ===========================================================================

    [Fact]
    public async Task EnumToStringConversion_AppointmentStatus_StoredAsStringInDatabase()
    {
        // Arrange – seed a nutritionist and a client to satisfy FK constraints
        await using var db = await _fixture.CreateDbContextAsync();
        var nutritionist = await SeedNutritionistAsync(db);
        var client = await SeedClientAsync(db, nutritionist.Id);

        var appointment = new Appointment
        {
            ClientId = client.Id,
            NutritionistId = nutritionist.Id,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Confirmed,
            Location = AppointmentLocation.Virtual,
            StartTime = DateTime.UtcNow.Date.AddHours(9),
            DurationMinutes = 60
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();

        // Act – read the raw column value directly via SQL to bypass EF value conversion.
        // SqlQuery<T> with an interpolated FormattableString uses parameterized SQL, which
        // suppresses EF1002 and prevents SQL injection.
        await using var rawDb = await _fixture.CreateDbContextAsync();
        var appointmentId = appointment.Id;
        var rawStatus = await rawDb.Database
            .SqlQuery<string>(
                $"SELECT \"Status\" AS \"Value\" FROM \"Appointments\" WHERE \"Id\" = {appointmentId}")
            .SingleAsync();

        // Assert – stored as the enum member name, not its integer ordinal
        rawStatus.Should().Be(nameof(AppointmentStatus.Confirmed),
            because: "HasConversion<string>() maps enum values to their string names in the database");
        rawStatus.Should().NotBe(((int)AppointmentStatus.Confirmed).ToString(),
            because: "integer storage would break migrations and human readability");
    }
}
