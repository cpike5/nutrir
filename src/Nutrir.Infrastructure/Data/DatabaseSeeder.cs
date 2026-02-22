using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;

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

    private async Task SeedDashboardDataAsync()
    {
        // Skip if clients already exist (idempotent)
        if (await _dbContext.Clients.AnyAsync())
        {
            _logger.LogInformation("Dashboard seed data already exists, skipping");

            // Still try to seed appointments (may be added after initial seeding)
            var existingNutritionist = await _userManager.FindByEmailAsync(_seedOptions.AdminEmail);
            if (existingNutritionist is not null)
            {
                var existingClientLookup = await _dbContext.Clients.ToDictionaryAsync(c => $"{c.FirstName} {c.LastName}", c => c.Id);
                await SeedAppointmentDataAsync(existingNutritionist.Id, existingClientLookup);
            }
            return;
        }

        // Use the admin user as the nutritionist for seed data
        var nutritionist = await _userManager.FindByEmailAsync(_seedOptions.AdminEmail);
        if (nutritionist is null)
        {
            _logger.LogWarning("No admin user found for dashboard seeding, skipping");
            return;
        }

        var now = DateTime.UtcNow;
        var nutritionistId = nutritionist.Id;

        // Seed 8 clients
        var clients = new List<Client>
        {
            new()
            {
                FirstName = "Maria", LastName = "Santos",
                Email = "maria.santos@email.com", Phone = "(416) 555-0142",
                DateOfBirth = new DateOnly(1988, 3, 15),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = false,
                Notes = "Initial consultation scheduled. Referred by Dr. Patel.",
                CreatedAt = now.AddHours(-2)
            },
            new()
            {
                FirstName = "James", LastName = "Whitfield",
                Email = "james.w@email.com", Phone = "(905) 555-0198",
                DateOfBirth = new DateOnly(1975, 11, 22),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-25),
                ConsentPolicyVersion = "1.0",
                Notes = "Weight loss program, Week 4. Target: 75kg. Current: 82kg.",
                CreatedAt = now.AddDays(-28)
            },
            new()
            {
                FirstName = "Anika", LastName = "Patel",
                Email = "anika.p@email.com", Phone = "(647) 555-0234",
                DateOfBirth = new DateOnly(1992, 7, 8),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-20),
                ConsentPolicyVersion = "1.0",
                Notes = "Type 2 diabetes management. HbA1c: 7.2, target 6.5.",
                CreatedAt = now.AddDays(-14)
            },
            new()
            {
                FirstName = "David", LastName = "Kim",
                Email = "david.kim@email.com", Phone = "(416) 555-0367",
                DateOfBirth = new DateOnly(1998, 1, 30),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-15),
                ConsentPolicyVersion = "1.0",
                Notes = "Sports nutrition plan. Marathon training. Protein target: 160g.",
                CreatedAt = now.AddDays(-10)
            },
            new()
            {
                FirstName = "Lisa", LastName = "Tremblay",
                Email = "lisa.t@email.com", Phone = "(514) 555-0445",
                DateOfBirth = new DateOnly(1990, 9, 12),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-10),
                ConsentPolicyVersion = "1.0",
                Notes = "Prenatal nutrition, 2nd trimester. Iron monitoring.",
                CreatedAt = now.AddDays(-7)
            },
            new()
            {
                FirstName = "Robert", LastName = "Nguyen",
                Email = "robert.n@email.com", Phone = "(604) 555-0523",
                DateOfBirth = new DateOnly(1983, 5, 19),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = false,
                Notes = "New client, initial consult pending. Referred by physiotherapy clinic.",
                CreatedAt = now.AddDays(-2)
            },
            new()
            {
                FirstName = "Elena", LastName = "Morales",
                Email = "elena.m@email.com", Phone = "(416) 555-0601",
                DateOfBirth = new DateOnly(1995, 12, 3),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-18),
                ConsentPolicyVersion = "1.0",
                Notes = "IBS management. Tracking symptom-free days. Low FODMAP.",
                CreatedAt = now.AddDays(-20)
            },
            new()
            {
                FirstName = "Thomas", LastName = "Beaulieu",
                Email = "t.beaulieu@email.com", Phone = "(819) 555-0178",
                DateOfBirth = new DateOnly(1970, 4, 25),
                PrimaryNutritionistId = nutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = now.AddDays(-22),
                ConsentPolicyVersion = "1.0",
                Notes = "Cardiac rehab nutrition. Post-stent placement. Low sodium.",
                CreatedAt = now.AddDays(-25)
            }
        };

        _dbContext.Clients.AddRange(clients);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Seeded {Count} dashboard clients", clients.Count);

        // Seed audit log entries
        var clientLookup = await _dbContext.Clients.ToDictionaryAsync(c => $"{c.FirstName} {c.LastName}", c => c.Id);

        var auditEntries = new List<AuditLogEntry>
        {
            new() { Timestamp = now.AddHours(-2), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["Maria Santos"].ToString(), Details = "Created client Maria Santos" },
            new() { Timestamp = now.AddHours(-3), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["James Whitfield"].ToString(), Details = "Updated client James Whitfield" },
            new() { Timestamp = now.AddHours(-5), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["Anika Patel"].ToString(), Details = "Updated client Anika Patel" },
            new() { Timestamp = now.AddHours(-6), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["David Kim"].ToString(), Details = "Updated client David Kim" },
            new() { Timestamp = now.AddDays(-1), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["Lisa Tremblay"].ToString(), Details = "Updated client Lisa Tremblay" },
            new() { Timestamp = now.AddDays(-2), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["Robert Nguyen"].ToString(), Details = "Created client Robert Nguyen" },
            new() { Timestamp = now.AddDays(-2).AddHours(-3), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["Elena Morales"].ToString(), Details = "Updated client Elena Morales" },
            new() { Timestamp = now.AddDays(-3), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["Thomas Beaulieu"].ToString(), Details = "Created client Thomas Beaulieu" },
            new() { Timestamp = now.AddDays(-5), UserId = nutritionistId, Action = "ClientUpdated", EntityType = "Client", EntityId = clientLookup["James Whitfield"].ToString(), Details = "Updated client James Whitfield" },
            new() { Timestamp = now.AddDays(-7), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["Lisa Tremblay"].ToString(), Details = "Created client Lisa Tremblay" },
            new() { Timestamp = now.AddDays(-10), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["David Kim"].ToString(), Details = "Created client David Kim" },
            new() { Timestamp = now.AddDays(-14), UserId = nutritionistId, Action = "ClientCreated", EntityType = "Client", EntityId = clientLookup["Anika Patel"].ToString(), Details = "Created client Anika Patel" },
        };

        _dbContext.AuditLogEntries.AddRange(auditEntries);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Seeded {Count} dashboard audit log entries", auditEntries.Count);

        await SeedAppointmentDataAsync(nutritionistId, clientLookup);
    }

    private async Task SeedAppointmentDataAsync(string nutritionistId, Dictionary<string, int> clientLookup)
    {
        if (await _dbContext.Appointments.AnyAsync())
        {
            _logger.LogInformation("Appointment seed data already exists, skipping");
            return;
        }

        var now = DateTime.UtcNow;
        var today = now.Date;

        var appointments = new List<Appointment>
        {
            new()
            {
                ClientId = clientLookup["Maria Santos"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.InitialConsultation,
                Status = AppointmentStatus.Scheduled,
                StartTime = today.AddHours(now.Hour + 2),
                DurationMinutes = 60,
                Location = AppointmentLocation.Virtual,
                VirtualMeetingUrl = "https://meet.example.com/maria-santos",
                Notes = "Initial consultation. Referred by Dr. Patel.",
                CreatedAt = now.AddDays(-2)
            },
            new()
            {
                ClientId = clientLookup["James Whitfield"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Confirmed,
                StartTime = today.AddHours(now.Hour + 4),
                DurationMinutes = 30,
                Location = AppointmentLocation.InPerson,
                LocationNotes = "Office A, Suite 204",
                Notes = "Week 4 check-in. Review weight progress.",
                CreatedAt = now.AddDays(-3)
            },
            new()
            {
                ClientId = clientLookup["Anika Patel"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Scheduled,
                StartTime = today.AddDays(1).AddHours(10),
                DurationMinutes = 45,
                Location = AppointmentLocation.Virtual,
                VirtualMeetingUrl = "https://meet.example.com/anika-patel",
                Notes = "Review HbA1c progress.",
                CreatedAt = now.AddDays(-1)
            },
            new()
            {
                ClientId = clientLookup["David Kim"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.CheckIn,
                Status = AppointmentStatus.Confirmed,
                StartTime = today.AddDays(1).AddHours(14),
                DurationMinutes = 15,
                Location = AppointmentLocation.Phone,
                Notes = "Quick check on protein targets.",
                CreatedAt = now.AddDays(-1)
            },
            new()
            {
                ClientId = clientLookup["Lisa Tremblay"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Scheduled,
                StartTime = today.AddDays(3).AddHours(11),
                DurationMinutes = 45,
                Location = AppointmentLocation.InPerson,
                LocationNotes = "Office A, Suite 204",
                Notes = "Prenatal nutrition follow-up. Iron levels.",
                CreatedAt = now.AddDays(-2)
            },
            new()
            {
                ClientId = clientLookup["Elena Morales"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Completed,
                StartTime = today.AddDays(-1).AddHours(10),
                DurationMinutes = 30,
                Location = AppointmentLocation.Virtual,
                VirtualMeetingUrl = "https://meet.example.com/elena-morales",
                Notes = "IBS management review. Low FODMAP compliance.",
                CreatedAt = now.AddDays(-5)
            },
            new()
            {
                ClientId = clientLookup["Thomas Beaulieu"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Completed,
                StartTime = today.AddDays(-3).AddHours(14),
                DurationMinutes = 45,
                Location = AppointmentLocation.InPerson,
                LocationNotes = "Office A, Suite 204",
                Notes = "Post-stent nutrition review. Sodium tracking.",
                CreatedAt = now.AddDays(-7)
            },
            new()
            {
                ClientId = clientLookup["James Whitfield"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.CheckIn,
                Status = AppointmentStatus.NoShow,
                StartTime = today.AddDays(-5).AddHours(9),
                DurationMinutes = 15,
                Location = AppointmentLocation.Phone,
                CreatedAt = now.AddDays(-8)
            },
            new()
            {
                ClientId = clientLookup["Anika Patel"],
                NutritionistId = nutritionistId,
                Type = AppointmentType.InitialConsultation,
                Status = AppointmentStatus.Completed,
                StartTime = today.AddDays(-14).AddHours(10),
                DurationMinutes = 60,
                Location = AppointmentLocation.InPerson,
                LocationNotes = "Office A, Suite 204",
                Notes = "Initial assessment. Type 2 diabetes management plan created.",
                CreatedAt = now.AddDays(-16)
            }
        };

        _dbContext.Appointments.AddRange(appointments);
        await _dbContext.SaveChangesAsync();

        _logger.LogInformation("Seeded {Count} appointments", appointments.Count);
    }
}
