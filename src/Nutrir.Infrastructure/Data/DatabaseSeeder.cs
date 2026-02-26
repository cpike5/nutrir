using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Entities;
using Nutrir.Infrastructure.Data.Seeding;

namespace Nutrir.Infrastructure.Data;

public class DatabaseSeeder
{
    private static readonly string[] Roles = ["Admin", "Nutritionist", "Assistant", "Client"];

    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _dbContext;
    private readonly SeedOptions _seedOptions;
    private readonly ILogger<DatabaseSeeder> _logger;

    public DatabaseSeeder(
        RoleManager<IdentityRole> roleManager,
        UserManager<ApplicationUser> userManager,
        AppDbContext dbContext,
        IOptions<SeedOptions> seedOptions,
        ILogger<DatabaseSeeder> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _dbContext = dbContext;
        _seedOptions = seedOptions.Value;
        _logger = logger;
    }

    public async Task SeedAsync(bool isDevelopment = false)
    {
        await SeedRolesAsync();
        await SeedAdminUserAsync();
        await SeedNutritionistUserAsync();

        if (isDevelopment)
        {
            await SeedDashboardDataAsync();
        }
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

            // Ensure existing admin also has the Nutritionist role
            if (!await _userManager.IsInRoleAsync(existingAdmin, "Nutritionist"))
            {
                var addNutResult = await _userManager.AddToRoleAsync(existingAdmin, "Nutritionist");
                if (addNutResult.Succeeded)
                    _logger.LogInformation("Assigned Nutritionist role to existing admin {AdminEmail}", _seedOptions.AdminEmail);
            }

            return;
        }

        var adminUser = new ApplicationUser
        {
            UserName = _seedOptions.AdminEmail,
            Email = _seedOptions.AdminEmail,
            EmailConfirmed = true,
            IsActive = true,
            FirstName = "Admin",
            LastName = "User",
            DisplayName = "Admin",
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

        // Admin user also acts as the default nutritionist for small practices
        var nutritionistResult = await _userManager.AddToRoleAsync(adminUser, "Nutritionist");

        if (nutritionistResult.Succeeded)
        {
            _logger.LogInformation("Assigned Nutritionist role to user {AdminEmail}", _seedOptions.AdminEmail);
        }
        else
        {
            _logger.LogError("Failed to assign Nutritionist role to user {AdminEmail}: {Errors}", _seedOptions.AdminEmail,
                string.Join(", ", nutritionistResult.Errors.Select(e => e.Description)));
        }
    }

    private async Task SeedNutritionistUserAsync()
    {
        const string email = "alyssa@domain.com";
        var existing = await _userManager.FindByEmailAsync(email);

        if (existing is not null)
        {
            _logger.LogInformation("Nutritionist user {Email} already exists, skipping creation", email);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            IsActive = true,
            FirstName = "Alyssa",
            LastName = "Martin",
            DisplayName = "Alyssa Martin",
            CreatedDate = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user, _seedOptions.AdminPassword);

        if (!createResult.Succeeded)
        {
            _logger.LogError("Failed to create nutritionist user {Email}: {Errors}", email,
                string.Join(", ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        _logger.LogInformation("Created nutritionist user {Email}", email);

        var roleResult = await _userManager.AddToRoleAsync(user, "Nutritionist");

        if (roleResult.Succeeded)
        {
            _logger.LogInformation("Assigned Nutritionist role to user {Email}", email);
        }
        else
        {
            _logger.LogError("Failed to assign Nutritionist role to user {Email}: {Errors}", email,
                string.Join(", ", roleResult.Errors.Select(e => e.Description)));
        }
    }

    private async Task SeedDashboardDataAsync()
    {
        // Idempotency check — skip if clients already exist
        if (await _dbContext.Clients.AnyAsync())
        {
            _logger.LogInformation("Dashboard seed data already exists, skipping");
            return;
        }

        // Resolve nutritionist user IDs for seed data
        var adminUser = await _userManager.FindByEmailAsync(_seedOptions.AdminEmail);
        if (adminUser is null)
        {
            _logger.LogWarning("No admin user found for dashboard seeding, skipping");
            return;
        }

        var nutritionistIds = new List<string> { adminUser.Id };

        var secondNutritionist = await _userManager.FindByEmailAsync("alyssa@domain.com");
        if (secondNutritionist is not null)
        {
            nutritionistIds.Add(secondNutritionist.Id);
        }

        var ids = nutritionistIds.ToArray();
        var generator = new SeedDataGenerator(_seedOptions);

        // Stage 1: Generate and persist clients (gives them real DB IDs)
        var generatedClients = generator.GenerateClients(ids);
        var clients = generatedClients.Select(gc => gc.Client).ToList();
        _dbContext.Clients.AddRange(clients);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} clients", clients.Count);

        // Stage 2: Generate child entities (now clients have real IDs) and persist
        var (appointments, mealPlans, goals, entries) = generator.GenerateChildEntities(ids);
        _dbContext.Appointments.AddRange(appointments);
        _dbContext.MealPlans.AddRange(mealPlans);
        _dbContext.ProgressGoals.AddRange(goals);
        _dbContext.ProgressEntries.AddRange(entries);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Seeded {AppointmentCount} appointments, {MealPlanCount} meal plans, {GoalCount} goals, {EntryCount} progress entries",
            appointments.Count, mealPlans.Count, goals.Count, entries.Count);

        // Stage 3: Generate audit logs (now all entities have real IDs) and persist
        var auditLogs = generator.GenerateAuditLogs(ids);
        _dbContext.AuditLogEntries.AddRange(auditLogs);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Seeded {Count} audit log entries", auditLogs.Count);
    }
}
