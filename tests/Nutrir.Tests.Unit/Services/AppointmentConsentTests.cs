using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Exceptions;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

/// <summary>
/// Minimal IDbContextFactory implementation that always returns the same pre-created context.
/// This avoids NSubstitute ValueTask return-type inference issues.
/// </summary>
file sealed class FixedContextFactory(AppDbContext context) : IDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext() => context;
    public Task<AppDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(context);
}

public class AppointmentConsentTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IAvailabilityService _availabilityService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly ISessionNoteService _sessionNoteService;
    private readonly IRetentionTracker _retentionTracker;

    private readonly AppointmentService _sut;

    private const string NutritionistId = "nutritionist-user-id-001";

    public AppointmentConsentTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _availabilityService = Substitute.For<IAvailabilityService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _sessionNoteService = Substitute.For<ISessionNoteService>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        // FixedContextFactory always returns the same shared test context without
        // triggering NSubstitute/ValueTask return-type inference issues.
        var dbContextFactory = new FixedContextFactory(_dbContext);

        // Availability always approves so that consent is the only gate under test
        _availabilityService
            .IsSlotWithinScheduleAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<int>())
            .Returns((true, (string?)null));

        _sut = new AppointmentService(
            _dbContext,
            dbContextFactory,
            _auditLogService,
            _availabilityService,
            _notificationDispatcher,
            _sessionNoteService,
            _retentionTracker,
            NullLogger<AppointmentService>.Instance);

        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        var nutritionist = new ApplicationUser
        {
            Id = NutritionistId,
            UserName = "nutritionist@test.com",
            NormalizedUserName = "NUTRITIONIST@TEST.COM",
            Email = "nutritionist@test.com",
            NormalizedEmail = "NUTRITIONIST@TEST.COM",
            FirstName = "Jane",
            LastName = "Doe",
            DisplayName = "Jane Doe",
            CreatedDate = DateTime.UtcNow
        };

        var clientWithoutConsent = new Client
        {
            Id = 1,
            FirstName = "Alice",
            LastName = "NoConsent",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = false,
            CreatedAt = DateTime.UtcNow
        };

        var clientWithConsent = new Client
        {
            Id = 2,
            FirstName = "Bob",
            LastName = "HasConsent",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(clientWithoutConsent);
        _dbContext.Clients.Add(clientWithConsent);
        _dbContext.SaveChanges();
    }

    private static CreateAppointmentDto BuildDto(int clientId, DateTime? startTime = null) =>
        new(
            ClientId: clientId,
            Type: AppointmentType.FollowUp,
            StartTime: startTime ?? DateTime.UtcNow.AddDays(1),
            DurationMinutes: 60,
            Location: AppointmentLocation.Virtual,
            VirtualMeetingUrl: null,
            LocationNotes: null,
            Notes: null,
            PrepNotes: null);

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithoutConsent_ThrowsConsentRequiredException()
    {
        // Arrange
        var dto = BuildDto(clientId: 1);

        // Act
        var act = () => _sut.CreateAsync(dto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<ConsentRequiredException>()
            .WithMessage("*1*");
    }

    [Fact]
    public async Task CreateAsync_WithConsent_Succeeds()
    {
        // Arrange
        var dto = BuildDto(clientId: 2);

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be(2);
        result.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task CreateRecurringAsync_WithoutConsent_ThrowsConsentRequiredException()
    {
        // Arrange
        var baseDto = BuildDto(clientId: 1);
        var recurringDto = new CreateRecurringAppointmentDto(
            Base: baseDto,
            IntervalDays: 7,
            Count: 3);

        // Act
        var act = () => _sut.CreateRecurringAsync(recurringDto, NutritionistId);

        // Assert
        await act.Should().ThrowAsync<ConsentRequiredException>()
            .WithMessage("*1*");
    }

    // ---------------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
