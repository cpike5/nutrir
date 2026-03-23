using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class InviteCodeServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly SqliteConnection _connection;
    private readonly IAuditLogService _auditLogService;
    private readonly InviteCodeService _sut;

    private const string CreatorUserId = "creator-user-001";
    private const string RedeemerUserId = "redeemer-user-001";

    public InviteCodeServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _auditLogService = Substitute.For<IAuditLogService>();
        _sut = new InviteCodeService(_dbContext, _auditLogService, NullLogger<InviteCodeService>.Instance);
        SeedData();
    }

    private void SeedData()
    {
        _dbContext.Users.AddRange(
            new ApplicationUser
            {
                Id = CreatorUserId,
                DisplayName = "Test Creator",
                UserName = "creator@test.com",
                NormalizedUserName = "CREATOR@TEST.COM",
                Email = "creator@test.com",
                NormalizedEmail = "CREATOR@TEST.COM",
                FirstName = "Test",
                LastName = "Creator",
                CreatedDate = DateTime.UtcNow
            },
            new ApplicationUser
            {
                Id = RedeemerUserId,
                DisplayName = "Test Redeemer",
                UserName = "redeemer@test.com",
                NormalizedUserName = "REDEEMER@TEST.COM",
                Email = "redeemer@test.com",
                NormalizedEmail = "REDEEMER@TEST.COM",
                FirstName = "Test",
                LastName = "Redeemer",
                CreatedDate = DateTime.UtcNow
            });
        _dbContext.SaveChanges();
    }

    /// <summary>
    /// Seeds an InviteCode with sensible defaults, allowing selective overrides.
    /// The seeded entity is saved to the database and its assigned Id is returned.
    /// </summary>
    private int MakeInviteCode(
        string? code = null,
        string targetRole = "Client",
        DateTime? expiresAt = null,
        bool isUsed = false,
        bool isCancelled = false,
        string? redeemedById = null,
        DateTime? redeemedAt = null,
        DateTime? cancelledAt = null,
        DateTime? createdAt = null)
    {
        var inviteCode = new InviteCode
        {
            Code = code ?? "ABC-1234",
            TargetRole = targetRole,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            IsUsed = isUsed,
            IsCancelled = isCancelled,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            CreatedById = CreatorUserId,
            CreatedBy = _dbContext.Users.Find(CreatorUserId)!,
            RedeemedById = redeemedById,
            RedeemedAt = redeemedAt,
            CancelledAt = cancelledAt
        };

        _dbContext.InviteCodes.Add(inviteCode);
        _dbContext.SaveChanges();
        return inviteCode.Id;
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // GenerateAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_WithValidInputs_ReturnsCorrectTargetRole()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.TargetRole.Should().Be("Client");
    }

    [Fact]
    public async Task GenerateAsync_WithDefaultExpiration_SetsExpiresAtApproximatelySevenDaysFromNow()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(7), TimeSpan.FromSeconds(10),
            because: "default expiration is 7 days from creation time");
    }

    [Fact]
    public async Task GenerateAsync_WithCustomExpirationDays_SetsExpiresAtCorrectly()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client", expirationDays: 30);

        // Assert
        result.ExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), TimeSpan.FromSeconds(10),
            because: "custom expirationDays should control the expiry window");
    }

    [Fact]
    public async Task GenerateAsync_WithValidInputs_ReturnsIsUsedFalse()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.IsUsed.Should().BeFalse(because: "a freshly generated code has not been used");
    }

    [Fact]
    public async Task GenerateAsync_WithValidInputs_ReturnsCodeMatchingExpectedFormat()
    {
        // The generator picks from "ABCDEFGHJKLMNPQRSTUVWXYZ" (excludes I and O),
        // so the expected pattern uses [A-HJ-NP-Z].
        var expectedPattern = new Regex(@"^[A-HJ-NP-Z]{3}-\d{4}$");

        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.Code.Should().MatchRegex(expectedPattern.ToString(),
            because: "invite codes must follow the 3-letter hyphen 4-digit format");
    }

    [Fact]
    public async Task GenerateAsync_WithValidInputs_PersistsInviteCodeToDatabase()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        var persisted = await _dbContext.InviteCodes.FirstOrDefaultAsync(ic => ic.Id == result.Id);
        persisted.Should().NotBeNull(because: "the generated invite code must be saved to the database");
        persisted!.Code.Should().Be(result.Code);
        persisted.TargetRole.Should().Be("Client");
        persisted.IsUsed.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateAsync_WithValidInputs_CallsAuditLogWithGeneratedAction()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        await _auditLogService.Received(1).LogAsync(
            CreatorUserId,
            "InviteCode.Generated",
            "InviteCode",
            result.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task GenerateAsync_WithSeededUser_ReturnsCreatedByNameFromDatabase()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.CreatedByName.Should().Be("Test Creator",
            because: "the DTO should reflect the DisplayName of the user who created the code");
    }

    [Fact]
    public async Task GenerateAsync_WithValidInputs_ReturnsAssignedId()
    {
        // Act
        var result = await _sut.GenerateAsync(CreatorUserId, "Client");

        // Assert
        result.Id.Should().BeGreaterThan(0, because: "the database should assign a positive identity value");
    }

    // ---------------------------------------------------------------------------
    // ValidateAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ValidateAsync_WithValidActiveCode_ReturnsIsValidTrueAndStatusValid()
    {
        // Arrange
        MakeInviteCode(code: "VLD-0001");

        // Act
        var result = await _sut.ValidateAsync("VLD-0001");

        // Assert
        result.IsValid.Should().BeTrue();
        result.Status.Should().Be(InviteCodeValidationStatus.Valid);
    }

    [Fact]
    public async Task ValidateAsync_WithValidActiveCode_ReturnsTargetRole()
    {
        // Arrange
        MakeInviteCode(code: "VLD-0002", targetRole: "Nutritionist");

        // Act
        var result = await _sut.ValidateAsync("VLD-0002");

        // Assert
        result.TargetRole.Should().Be("Nutritionist",
            because: "the target role must be returned for valid codes so callers can enforce role assignment");
    }

    [Fact]
    public async Task ValidateAsync_WithNonExistentCode_ReturnsNotFound()
    {
        // Act
        var result = await _sut.ValidateAsync("ZZZ-9999");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(InviteCodeValidationStatus.NotFound);
    }

    [Fact]
    public async Task ValidateAsync_WithAlreadyUsedCode_ReturnsAlreadyUsed()
    {
        // Arrange
        MakeInviteCode(code: "USD-0001", isUsed: true);

        // Act
        var result = await _sut.ValidateAsync("USD-0001");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(InviteCodeValidationStatus.AlreadyUsed);
    }

    [Fact]
    public async Task ValidateAsync_WithCancelledCode_ReturnsCancelled()
    {
        // Arrange
        MakeInviteCode(code: "CAN-0001", isCancelled: true, cancelledAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await _sut.ValidateAsync("CAN-0001");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(InviteCodeValidationStatus.Cancelled);
    }

    [Fact]
    public async Task ValidateAsync_WithExpiredCode_ReturnsExpired()
    {
        // Arrange
        MakeInviteCode(code: "EXP-0001", expiresAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var result = await _sut.ValidateAsync("EXP-0001");

        // Assert
        result.IsValid.Should().BeFalse();
        result.Status.Should().Be(InviteCodeValidationStatus.Expired);
    }

    // ---------------------------------------------------------------------------
    // RedeemAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RedeemAsync_WithValidCode_MarksCodeAsUsed()
    {
        // Arrange
        var id = MakeInviteCode(code: "RDM-0001");

        // Act
        await _sut.RedeemAsync("RDM-0001", RedeemerUserId);

        // Assert
        var persisted = await _dbContext.InviteCodes.FirstOrDefaultAsync(ic => ic.Id == id);
        persisted!.IsUsed.Should().BeTrue(because: "the code should be marked used after redemption");
    }

    [Fact]
    public async Task RedeemAsync_WithValidCode_SetsRedeemedByIdAndRedeemedAt()
    {
        // Arrange
        var id = MakeInviteCode(code: "RDM-0002");

        // Act
        await _sut.RedeemAsync("RDM-0002", RedeemerUserId);

        // Assert
        var persisted = await _dbContext.InviteCodes.FirstOrDefaultAsync(ic => ic.Id == id);
        persisted!.RedeemedById.Should().Be(RedeemerUserId);
        persisted.RedeemedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10),
            because: "the redemption timestamp should be set at the time of the call");
    }

    [Fact]
    public async Task RedeemAsync_WithValidCode_CallsAuditLogWithRedeemedAction()
    {
        // Arrange
        var id = MakeInviteCode(code: "RDM-0003");

        // Act
        await _sut.RedeemAsync("RDM-0003", RedeemerUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            RedeemerUserId,
            "InviteCode.Redeemed",
            "InviteCode",
            id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task RedeemAsync_WithNonExistentCode_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sut.RedeemAsync("ZZZ-9999", RedeemerUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "redeeming a code that does not exist must fail with a clear exception");
    }

    [Fact]
    public async Task RedeemAsync_WithAlreadyUsedCode_ThrowsInvalidOperationException()
    {
        // Arrange
        MakeInviteCode(code: "USD-0002", isUsed: true);

        // Act
        var act = async () => await _sut.RedeemAsync("USD-0002", RedeemerUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a code that has already been used cannot be redeemed again");
    }

    [Fact]
    public async Task RedeemAsync_WithCancelledCode_ThrowsInvalidOperationException()
    {
        // Arrange
        MakeInviteCode(code: "CAN-0002", isCancelled: true, cancelledAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var act = async () => await _sut.RedeemAsync("CAN-0002", RedeemerUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a cancelled code cannot be redeemed");
    }

    [Fact]
    public async Task RedeemAsync_WithExpiredCode_ThrowsInvalidOperationException()
    {
        // Arrange
        MakeInviteCode(code: "EXP-0002", expiresAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var act = async () => await _sut.RedeemAsync("EXP-0002", RedeemerUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "an expired code cannot be redeemed");
    }

    // ---------------------------------------------------------------------------
    // CancelAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CancelAsync_WithValidCode_MarksCodeAsCancelled()
    {
        // Arrange
        var id = MakeInviteCode(code: "CNL-0001");

        // Act
        await _sut.CancelAsync(id, CreatorUserId);

        // Assert
        var persisted = await _dbContext.InviteCodes.FirstOrDefaultAsync(ic => ic.Id == id);
        persisted!.IsCancelled.Should().BeTrue(because: "the code should be flagged as cancelled after the call");
    }

    [Fact]
    public async Task CancelAsync_WithValidCode_SetsCancelledAt()
    {
        // Arrange
        var id = MakeInviteCode(code: "CNL-0002");

        // Act
        await _sut.CancelAsync(id, CreatorUserId);

        // Assert
        var persisted = await _dbContext.InviteCodes.FirstOrDefaultAsync(ic => ic.Id == id);
        persisted!.CancelledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(10),
            because: "the cancellation timestamp should be set at the time of the call");
    }

    [Fact]
    public async Task CancelAsync_WithValidCode_CallsAuditLogWithCancelledAction()
    {
        // Arrange
        var id = MakeInviteCode(code: "CNL-0003");

        // Act
        await _sut.CancelAsync(id, CreatorUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            CreatorUserId,
            "InviteCode.Cancelled",
            "InviteCode",
            id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task CancelAsync_WithNonExistentId_ThrowsInvalidOperationException()
    {
        // Act
        var act = async () => await _sut.CancelAsync(int.MaxValue, CreatorUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "cancelling a code that does not exist must fail with a clear exception");
    }

    [Fact]
    public async Task CancelAsync_WithAlreadyRedeemedCode_ThrowsInvalidOperationException()
    {
        // Arrange
        var id = MakeInviteCode(
            code: "USD-0003",
            isUsed: true,
            redeemedById: RedeemerUserId,
            redeemedAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var act = async () => await _sut.CancelAsync(id, CreatorUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "a code that has already been redeemed cannot be cancelled");
    }

    [Fact]
    public async Task CancelAsync_WithAlreadyCancelledCode_ThrowsInvalidOperationException()
    {
        // Arrange
        var id = MakeInviteCode(code: "CAN-0003", isCancelled: true, cancelledAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var act = async () => await _sut.CancelAsync(id, CreatorUserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>(
            because: "cancelling an already-cancelled code must fail with a clear exception");
    }

    // ---------------------------------------------------------------------------
    // GetAllAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllAsync_WithMultipleCodes_ReturnsAllInviteCodes()
    {
        // Arrange
        MakeInviteCode(code: "ALL-0001", createdAt: DateTime.UtcNow.AddHours(-2));
        MakeInviteCode(code: "ALL-0002", createdAt: DateTime.UtcNow.AddHours(-1));
        MakeInviteCode(code: "ALL-0003", createdAt: DateTime.UtcNow);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().HaveCount(3, because: "all three seeded invite codes should be returned");
    }

    [Fact]
    public async Task GetAllAsync_WithMultipleCodes_ReturnsCodesOrderedByCreatedAtDescending()
    {
        // Arrange — insert in ascending time order so the ordering under test is meaningful
        MakeInviteCode(code: "ORD-0001", createdAt: DateTime.UtcNow.AddHours(-2));
        MakeInviteCode(code: "ORD-0002", createdAt: DateTime.UtcNow.AddHours(-1));
        MakeInviteCode(code: "ORD-0003", createdAt: DateTime.UtcNow);

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        result.Select(r => r.Code).Should().ContainInOrder(
            new[] { "ORD-0003", "ORD-0002", "ORD-0001" },
            because: "GetAllAsync must order by CreatedAt descending — newest first");
    }

    [Fact]
    public async Task GetAllAsync_WithCode_MapsCreatedByNameCorrectly()
    {
        // Arrange
        MakeInviteCode(code: "MAP-0001");

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        var dto = result.Single(r => r.Code == "MAP-0001");
        dto.CreatedByName.Should().Be("Test Creator",
            because: "the DTO should reflect the DisplayName of the user who created the code");
    }

    [Fact]
    public async Task GetAllAsync_WithRedeemedCode_MapsRedeemedByNameCorrectly()
    {
        // Arrange
        MakeInviteCode(
            code: "MAP-0002",
            isUsed: true,
            redeemedById: RedeemerUserId,
            redeemedAt: DateTime.UtcNow.AddHours(-1));

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        var dto = result.Single(r => r.Code == "MAP-0002");
        dto.RedeemedByName.Should().Be("Test Redeemer",
            because: "the DTO should reflect the DisplayName of the user who redeemed the code");
    }

    [Fact]
    public async Task GetAllAsync_WithUnredeemedCode_ReturnsNullRedeemedByName()
    {
        // Arrange
        MakeInviteCode(code: "NRD-0001");

        // Act
        var result = await _sut.GetAllAsync();

        // Assert
        var dto = result.Single(r => r.Code == "NRD-0001");
        dto.RedeemedByName.Should().BeNull(
            because: "a code that has not been redeemed should have a null RedeemedByName");
    }

    [Fact]
    public async Task GetAllAsync_WithNoCodes_ReturnsEmptyList()
    {
        // Act — no invite codes seeded (only ApplicationUser rows exist from SeedData)
        var result = await _sut.GetAllAsync();

        // Assert
        result.Should().BeEmpty(because: "no invite codes exist in the database");
    }
}
