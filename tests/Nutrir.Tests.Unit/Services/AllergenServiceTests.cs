using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// AllergenService relies heavily on EF.Functions.ILike (PostgreSQL-specific) for both
/// SearchAsync and GetOrCreateAsync. Those methods cannot be tested with SQLite in-memory
/// and are covered by integration tests instead.
///
/// These unit tests verify the early-return guard clauses that don't hit the database.
/// </summary>
public class AllergenServiceTests
{
    private readonly IAuditLogService _auditLogService = Substitute.For<IAuditLogService>();

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyList()
    {
        // SearchAsync has an early return for empty/whitespace queries before any DB call
        var (_, connection) = Helpers.TestDbContextFactory.Create();
        var factory = new Helpers.SharedConnectionContextFactory(connection);
        var logger = Substitute.For<ILogger<AllergenService>>();
        var sut = new AllergenService(factory, _auditLogService, logger);

        var result = await sut.SearchAsync("");

        result.Should().BeEmpty();
        connection.Dispose();
    }

    [Fact]
    public async Task SearchAsync_WithWhitespace_ReturnsEmptyList()
    {
        var (_, connection) = Helpers.TestDbContextFactory.Create();
        var factory = new Helpers.SharedConnectionContextFactory(connection);
        var logger = Substitute.For<ILogger<AllergenService>>();
        var sut = new AllergenService(factory, _auditLogService, logger);

        var result = await sut.SearchAsync("   ");

        result.Should().BeEmpty();
        connection.Dispose();
    }
}
