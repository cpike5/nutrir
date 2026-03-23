using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class UserManagementServiceTests : IDisposable
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IAuditLogService _auditLogService;

    private readonly UserManagementService _sut;

    private const string UserId       = "user-mgmt-test-001";
    private const string AdminId      = "admin-mgmt-test-001";
    private const string RolePractitioner = "Practitioner";
    private const string RoleAdmin    = "Admin";

    public UserManagementServiceTests()
    {
        var userStore = Substitute.For<IUserStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            userStore, null, null, null, null, null, null, null, null);

        var roleStore = Substitute.For<IRoleStore<IdentityRole>>();
        _roleManager = Substitute.For<RoleManager<IdentityRole>>(
            roleStore, null, null, null, null);

        _auditLogService = Substitute.For<IAuditLogService>();

        _sut = new UserManagementService(
            _userManager,
            _roleManager,
            _auditLogService,
            NullLogger<UserManagementService>.Instance);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ApplicationUser MakeUser(
        string? id = null,
        bool isActive = true,
        bool twoFactorEnabled = false) => new()
    {
        Id          = id ?? UserId,
        UserName    = "test@example.com",
        Email       = "test@example.com",
        FirstName   = "Test",
        LastName    = "User",
        DisplayName = "Test User",
        IsActive    = isActive,
        TwoFactorEnabled = twoFactorEnabled,
        CreatedDate = DateTime.UtcNow
    };

    private static IdentityError TestError =>
        new() { Code = "Error", Description = "Test error" };

    // ---------------------------------------------------------------------------
    // GetUserByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetUserByIdAsync_WhenUserExists_ReturnsPopulatedDto()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new List<string> { RolePractitioner });

        // Act
        var result = await _sut.GetUserByIdAsync(UserId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(UserId);
        result.FirstName.Should().Be(user.FirstName);
        result.LastName.Should().Be(user.LastName);
        result.DisplayName.Should().Be(user.DisplayName);
        result.Email.Should().Be(user.Email);
        result.Role.Should().Be(RolePractitioner);
        result.IsActive.Should().BeTrue();
        result.TwoFactorEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenUserHasNoRole_ReturnsEmptyRoleString()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.GetRolesAsync(user).Returns(new List<string>());

        // Act
        var result = await _sut.GetUserByIdAsync(UserId);

        // Assert
        result.Should().NotBeNull();
        result!.Role.Should().BeEmpty();
    }

    [Fact]
    public async Task GetUserByIdAsync_WhenUserNotFound_ReturnsNull()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.GetUserByIdAsync(UserId);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // CreateUserAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateUserAsync_WithValidDto_ReturnsResultWithUserDetail()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, "Pass@word1");
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync(RolePractitioner).Returns(true);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RolePractitioner)
            .Returns(IdentityResult.Success);

        // Act
        var result = await _sut.CreateUserAsync(dto, AdminId);

        // Assert
        result.Should().NotBeNull();
        result.User.FirstName.Should().Be("Jane");
        result.User.LastName.Should().Be("Doe");
        result.User.Email.Should().Be("jane@example.com");
        result.User.Role.Should().Be(RolePractitioner);
        result.User.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateUserAsync_WithProvidedPassword_UsesProvidedPassword()
    {
        // Arrange
        const string explicitPassword = "Explicit@Pass1";
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, explicitPassword);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), explicitPassword)
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync(RolePractitioner).Returns(true);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RolePractitioner)
            .Returns(IdentityResult.Success);

        // Act
        var result = await _sut.CreateUserAsync(dto, AdminId);

        // Assert
        result.GeneratedPassword.Should().Be(explicitPassword);
        await _userManager.Received(1).CreateAsync(Arg.Any<ApplicationUser>(), explicitPassword);
    }

    [Fact]
    public async Task CreateUserAsync_WithNullPassword_GeneratesNonEmptyPassword()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync(RolePractitioner).Returns(true);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RolePractitioner)
            .Returns(IdentityResult.Success);

        // Act
        var result = await _sut.CreateUserAsync(dto, AdminId);

        // Assert
        result.GeneratedPassword.Should().NotBeNullOrEmpty(
            because: "a random password must be generated when none is supplied");
    }

    [Fact]
    public async Task CreateUserAsync_WhenCreateAsyncFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, "Pass@word1");
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var act = () => _sut.CreateUserAsync(dto, AdminId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to create user*");
    }

    [Fact]
    public async Task CreateUserAsync_WhenRoleDoesNotExist_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", "NonExistentRole", "Pass@word1");
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync("NonExistentRole").Returns(false);

        // Act
        var act = () => _sut.CreateUserAsync(dto, AdminId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Role*does not exist*");
    }

    [Fact]
    public async Task CreateUserAsync_WhenAddToRoleAsyncFails_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, "Pass@word1");
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync(RolePractitioner).Returns(true);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RolePractitioner)
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var act = () => _sut.CreateUserAsync(dto, AdminId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Failed to assign role*");
    }

    [Fact]
    public async Task CreateUserAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var dto = new CreateUserDto("Jane", "Doe", "jane@example.com", RolePractitioner, "Pass@word1");
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
        _roleManager.RoleExistsAsync(RolePractitioner).Returns(true);
        _userManager.AddToRoleAsync(Arg.Any<ApplicationUser>(), RolePractitioner)
            .Returns(IdentityResult.Success);

        // Act
        await _sut.CreateUserAsync(dto, AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "UserCreated", "User", Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // UpdateProfileAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateProfileAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "jane@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_WithSameEmail_UpdatesNameFieldsAndReturnsTrue()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "same@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Updated", "Name", "Updated Name", "same@example.com");

        // Assert
        result.Should().BeTrue();
        user.FirstName.Should().Be("Updated");
        user.LastName.Should().Be("Name");
        user.DisplayName.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateProfileAsync_WithChangedEmail_CallsSetEmailAsync()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "old@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetEmailAsync(user, "new@example.com").Returns(IdentityResult.Success);
        _userManager.SetUserNameAsync(user, "new@example.com").Returns(IdentityResult.Success);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "new@example.com");

        // Assert
        result.Should().BeTrue();
        await _userManager.Received(1).SetEmailAsync(user, "new@example.com");
        await _userManager.Received(1).SetUserNameAsync(user, "new@example.com");
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenSetEmailAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "old@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetEmailAsync(user, "new@example.com")
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "new@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenSetUserNameAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "old@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetEmailAsync(user, "new@example.com").Returns(IdentityResult.Success);
        _userManager.SetUserNameAsync(user, "new@example.com")
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "new@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenUpdateAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "same@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "same@example.com");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateProfileAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser();
        user.Email = "same@example.com";
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "same@example.com");

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId, "ProfileUpdated", "User", UserId, Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateProfileAsync_WhenUserNotFound_DoesNotLogAudit()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        await _sut.UpdateProfileAsync(UserId, "Jane", "Doe", "Jane Doe", "jane@example.com");

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // ChangeRoleAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ChangeRoleAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenRoleDoesNotExist_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(false);

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeRoleAsync_WithExistingRoles_RemovesOldRolesAndAddsNewRole()
    {
        // Arrange
        var user = MakeUser();
        var existingRoles = new List<string> { RolePractitioner };
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(true);
        _userManager.GetRolesAsync(user).Returns(existingRoles);
        _userManager.RemoveFromRolesAsync(user, existingRoles).Returns(IdentityResult.Success);
        _userManager.AddToRoleAsync(user, RoleAdmin).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeTrue();
        await _userManager.Received(1).RemoveFromRolesAsync(user, existingRoles);
        await _userManager.Received(1).AddToRoleAsync(user, RoleAdmin);
    }

    [Fact]
    public async Task ChangeRoleAsync_WithNoExistingRoles_SkipsRemoveAndAddsNewRole()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());
        _userManager.AddToRoleAsync(user, RoleAdmin).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeTrue();
        await _userManager.DidNotReceive().RemoveFromRolesAsync(Arg.Any<ApplicationUser>(), Arg.Any<IEnumerable<string>>());
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenRemoveFromRolesAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        var existingRoles = new List<string> { RolePractitioner };
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(true);
        _userManager.GetRolesAsync(user).Returns(existingRoles);
        _userManager.RemoveFromRolesAsync(user, existingRoles)
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeRoleAsync_WhenAddToRoleAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());
        _userManager.AddToRoleAsync(user, RoleAdmin)
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ChangeRoleAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _roleManager.RoleExistsAsync(RoleAdmin).Returns(true);
        _userManager.GetRolesAsync(user).Returns(new List<string>());
        _userManager.AddToRoleAsync(user, RoleAdmin).Returns(IdentityResult.Success);

        // Act
        await _sut.ChangeRoleAsync(UserId, RoleAdmin, AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "RoleChanged", "User", UserId, Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // DeactivateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeactivateAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_WhenUserAlreadyInactive_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(isActive: false);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        var result = await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_WhenUserAlreadyInactive_DoesNotLogAudit()
    {
        // Arrange
        var user = MakeUser(isActive: false);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeactivateAsync_WhenUserIsActive_SetsIsActiveFalseAndReturnsTrue()
    {
        // Arrange
        var user = MakeUser(isActive: true);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeTrue();
        user.IsActive.Should().BeFalse(because: "DeactivateAsync must set IsActive to false");
    }

    [Fact]
    public async Task DeactivateAsync_WhenUpdateAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(isActive: true);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser(isActive: true);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "UserDeactivated", "User", UserId, Arg.Any<string>());
    }

    [Fact]
    public async Task DeactivateAsync_WhenUserNotFound_DoesNotLogAudit()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        await _sut.DeactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // ReactivateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReactivateAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivateAsync_WhenUserAlreadyActive_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(isActive: true);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        var result = await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivateAsync_WhenUserAlreadyActive_DoesNotLogAudit()
    {
        // Arrange
        var user = MakeUser(isActive: true);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReactivateAsync_WhenUserIsInactive_SetsIsActiveTrueAndReturnsTrue()
    {
        // Arrange
        var user = MakeUser(isActive: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeTrue();
        user.IsActive.Should().BeTrue(because: "ReactivateAsync must set IsActive to true");
    }

    [Fact]
    public async Task ReactivateAsync_WhenUpdateAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(isActive: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ReactivateAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser(isActive: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "UserReactivated", "User", UserId, Arg.Any<string>());
    }

    [Fact]
    public async Task ReactivateAsync_WhenUserNotFound_DoesNotLogAudit()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        await _sut.ReactivateAsync(UserId, AdminId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // ResetPasswordAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ResetPasswordAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenSuccessful_RemovesOldPasswordAndAddsNew()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.RemovePasswordAsync(user).Returns(IdentityResult.Success);
        _userManager.AddPasswordAsync(user, "NewPass@1").Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        result.Should().BeTrue();
        await _userManager.Received(1).RemovePasswordAsync(user);
        await _userManager.Received(1).AddPasswordAsync(user, "NewPass@1");
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenRemovePasswordAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.RemovePasswordAsync(user).Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenAddPasswordAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.RemovePasswordAsync(user).Returns(IdentityResult.Success);
        _userManager.AddPasswordAsync(user, "NewPass@1").Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ResetPasswordAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.RemovePasswordAsync(user).Returns(IdentityResult.Success);
        _userManager.AddPasswordAsync(user, "NewPass@1").Returns(IdentityResult.Success);

        // Act
        await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "PasswordReset", "User", UserId, Arg.Any<string>());
    }

    [Fact]
    public async Task ResetPasswordAsync_WhenRemovePasswordAsyncFails_DoesNotCallAddPasswordAsync()
    {
        // Arrange
        var user = MakeUser();
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.RemovePasswordAsync(user).Returns(IdentityResult.Failed(TestError));

        // Act
        await _sut.ResetPasswordAsync(UserId, "NewPass@1", AdminId);

        // Assert
        await _userManager.DidNotReceive().AddPasswordAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // ForceMfaAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ForceMfaAsync_WhenUserNotFound_ReturnsFalse()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        var result = await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ForceMfaAsync_WhenMfaAlreadyEnabled_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(twoFactorEnabled: true);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        var result = await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ForceMfaAsync_WhenMfaDisabled_EnablesTwoFactorAndReturnsTrue()
    {
        // Arrange
        var user = MakeUser(twoFactorEnabled: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        result.Should().BeTrue();
        await _userManager.Received(1).SetTwoFactorEnabledAsync(user, true);
    }

    [Fact]
    public async Task ForceMfaAsync_WhenSetTwoFactorEnabledAsyncFails_ReturnsFalse()
    {
        // Arrange
        var user = MakeUser(twoFactorEnabled: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetTwoFactorEnabledAsync(user, true)
            .Returns(IdentityResult.Failed(TestError));

        // Act
        var result = await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ForceMfaAsync_OnSuccess_LogsAudit()
    {
        // Arrange
        var user = MakeUser(twoFactorEnabled: false);
        _userManager.FindByIdAsync(UserId).Returns(user);
        _userManager.SetTwoFactorEnabledAsync(user, true).Returns(IdentityResult.Success);

        // Act
        await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            AdminId, "MfaForced", "User", UserId, Arg.Any<string>());
    }

    [Fact]
    public async Task ForceMfaAsync_WhenMfaAlreadyEnabled_DoesNotCallSetTwoFactorEnabledAsync()
    {
        // Arrange
        var user = MakeUser(twoFactorEnabled: true);
        _userManager.FindByIdAsync(UserId).Returns(user);

        // Act
        await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        await _userManager.DidNotReceive()
            .SetTwoFactorEnabledAsync(Arg.Any<ApplicationUser>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task ForceMfaAsync_WhenUserNotFound_DoesNotLogAudit()
    {
        // Arrange
        _userManager.FindByIdAsync(UserId).Returns((ApplicationUser?)null);

        // Act
        await _sut.ForceMfaAsync(UserId, AdminId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _userManager.Dispose();
        _roleManager.Dispose();
    }
}
