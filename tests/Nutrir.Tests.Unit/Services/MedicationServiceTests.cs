using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// MedicationService relies on EF.Functions.ILike (PostgreSQL-specific) for both
/// SearchAsync and GetOrCreateAsync. Those methods cannot be unit tested with SQLite
/// and are covered by integration tests.
///
/// These tests verify guard clause behavior and input validation.
/// </summary>
public class MedicationServiceTests
{
    private readonly IAuditLogService _auditLogService = Substitute.For<IAuditLogService>();

    [Fact]
    public void GetOrCreateAsync_WithNullName_ThrowsArgumentException()
    {
        var (_, connection) = Helpers.TestDbContextFactory.Create();
        var factory = new Helpers.SharedConnectionContextFactory(connection);
        var logger = Substitute.For<ILogger<MedicationService>>();
        var sut = new MedicationService(factory, _auditLogService, logger);

        var act = async () => await sut.GetOrCreateAsync(null!, "user-1");

        act.Should().ThrowAsync<ArgumentException>();
        connection.Dispose();
    }

    [Fact]
    public void GetOrCreateAsync_WithEmptyName_ThrowsArgumentException()
    {
        var (_, connection) = Helpers.TestDbContextFactory.Create();
        var factory = new Helpers.SharedConnectionContextFactory(connection);
        var logger = Substitute.For<ILogger<MedicationService>>();
        var sut = new MedicationService(factory, _auditLogService, logger);

        var act = async () => await sut.GetOrCreateAsync("", "user-1");

        act.Should().ThrowAsync<ArgumentException>();
        connection.Dispose();
    }

    [Fact]
    public void GetOrCreateAsync_WithWhitespaceName_ThrowsArgumentException()
    {
        var (_, connection) = Helpers.TestDbContextFactory.Create();
        var factory = new Helpers.SharedConnectionContextFactory(connection);
        var logger = Substitute.For<ILogger<MedicationService>>();
        var sut = new MedicationService(factory, _auditLogService, logger);

        var act = async () => await sut.GetOrCreateAsync("   ", "user-1");

        act.Should().ThrowAsync<ArgumentException>();
        connection.Dispose();
    }
}
