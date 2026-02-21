using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Entities;

namespace Nutrir.Infrastructure.Data;

public class DatabaseSeeder
{
    private static readonly string[] Roles = ["Admin", "Nutritionist", "Assistant", "Client"];

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SeedOptions _seedOptions;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        IOptions<SeedOptions> seedOptions,
        ILogger<DatabaseSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync()
    {
        await SeedRolesAsync();
        await SeedAdminUserAsync();
    }

    private async Task SeedRolesAsync()
    {
        foreach (var roleName in Roles)
        {
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var result = await _roleManager.CreateAsync(new IdentityRole(roleName));

                if (result.Succeeded)
                {
                    _logger.LogInformation("Created role {RoleName}", roleName);
                }
                else
                {
                    _logger.LogError("Failed to create role {RoleName}: {Errors}", roleName,
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }

    private async Task SeedAdminUserAsync()
    {
        var existingAdmin = await _userManager.FindByEmailAsync(_seedOptions.AdminEmail);

        if (existingAdmin is not null)
        {
            _logger.LogInformation("Admin user {AdminEmail} already exists, skipping creation", _seedOptions.AdminEmail);
            return;
        }

        var adminUser = new ApplicationUser
        {
            UserName = _seedOptions.AdminEmail,
            Email = _seedOptions.AdminEmail,
            EmailConfirmed = true,
            IsActive = true,
            FirstName = "Admin",
            LastName = string.Empty,
            CreatedDate = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(adminUser, _seedOptions.AdminPassword);

        if (!createResult.Succeeded)
        {
            _logger.LogError("Failed to create admin user {AdminEmail}: {Errors}", _seedOptions.AdminEmail,
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        _logger.LogInformation("Created admin user {AdminEmail}", _seedOptions.AdminEmail);

        var roleResult = await _userManager.AddToRoleAsync(adminUser, "Admin");

        if (roleResult.Succeeded)
        {
            _logger.LogInformation("Assigned Admin role to user {AdminEmail}", _seedOptions.AdminEmail);
        }
        else
        {
            _logger.LogError("Failed to assign Admin role to user {AdminEmail}: {Errors}", _seedOptions.AdminEmail,
                string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }
    }
}
