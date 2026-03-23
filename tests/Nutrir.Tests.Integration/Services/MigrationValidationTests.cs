using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Nutrir.Tests.Integration.Fixtures;
using Xunit;

namespace Nutrir.Tests.Integration.Services;

/// <summary>
/// Validates that EF Core migrations apply cleanly to a real PostgreSQL database
/// and that the resulting schema matches what the model expects.
/// These tests are read-only schema checks — no ResetDatabaseAsync needed.
/// </summary>
[Collection("Database")]
public class MigrationValidationTests
{
    private readonly DatabaseFixture _fixture;

    public MigrationValidationTests(DatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task All_Migrations_Applied_Successfully()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var pendingMigrations = await context.Database.GetPendingMigrationsAsync();

        pendingMigrations.Should().BeEmpty(
            "all migrations should have been applied by the fixture during initialization");
    }

    [Fact]
    public async Task At_Least_One_Migration_Has_Been_Applied()
    {
        await using var context = await _fixture.CreateDbContextAsync();

        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();

        appliedMigrations.Should().NotBeEmpty(
            "at least one migration must be applied to confirm the schema was created from the EF model");
    }

    [Fact]
    public async Task All_Expected_Tables_Exist()
    {
        var expectedTables = new[]
        {
            // Domain entities
            "Clients", "Appointments", "MealPlans", "MealPlanDays",
            "MealSlots", "MealItems", "ProgressGoals", "ProgressEntries",
            "ProgressMeasurements", "ConsentEvents", "ConsentForms",
            "SessionNotes", "AppointmentReminders", "AuditLogEntries",
            "DataPurgeAuditLogs", "InviteCodes",
            // Health profile
            "ClientAllergies", "ClientMedications", "ClientConditions",
            "ClientDietaryRestrictions", "Allergens", "AllergenWarningOverrides",
            "Conditions", "Medications",
            // Scheduling
            "PractitionerSchedules", "PractitionerTimeBlocks",
            // Intake forms
            "IntakeForms", "IntakeFormResponses",
            // AI
            "AiConversations", "AiConversationMessages", "AiUsageLogs",
            // ASP.NET Core Identity
            "AspNetUsers", "AspNetRoles", "AspNetUserRoles",
            "AspNetUserClaims", "AspNetRoleClaims", "AspNetUserLogins",
            "AspNetUserTokens",
        };

        await using var context = await _fixture.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var existingTables = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            existingTables.Add(reader.GetString(0));

        existingTables.Should().Contain(
            expectedTables,
            "every expected entity table must exist in the migrated schema");
    }

    [Fact]
    public async Task Core_Indexes_Exist_On_Foreign_Key_Columns()
    {
        var expectedIndexPrefixes = new[]
        {
            "IX_AiConversations_UserId",
            "IX_AiConversationMessages_ConversationId",
            "IX_AiUsageLogs_UserId",
            "IX_AiUsageLogs_RequestedAt",
            "IX_ClientAllergies_ClientId",
            "IX_ClientMedications_ClientId",
            "IX_ClientConditions_ClientId",
            "IX_ClientDietaryRestrictions_ClientId",
            "IX_IntakeFormResponses_IntakeFormId",
            "IX_SessionNotes_AppointmentId",
            "IX_SessionNotes_ClientId",
            "IX_InviteCodes_Code",
            "IX_MealPlanDays_MealPlanId_DayNumber",
            "IX_Conditions_Name",
            "IX_Allergens_Name",
            "IX_AllergenWarningOverrides_MealPlanId",
            "IX_IntakeForms_Token",
            "IX_AppointmentReminders_AppointmentId",
            "IX_PractitionerSchedules_UserId_DayOfWeek",
            "IX_PractitionerTimeBlocks_UserId_Date",
        };

        await using var context = await _fixture.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var existingIndexes = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT indexname FROM pg_indexes WHERE schemaname = 'public'";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            existingIndexes.Add(reader.GetString(0));

        // Npgsql folds identifiers to lowercase; use case-insensitive prefix matching
        var existingLower = existingIndexes.Select(i => i.ToLowerInvariant()).ToList();

        var missingIndexes = expectedIndexPrefixes
            .Where(prefix => !existingLower.Any(i => i.StartsWith(prefix.ToLowerInvariant())))
            .ToList();

        missingIndexes.Should().BeEmpty(
            "all explicitly declared indexes must exist in the migrated schema; " +
            "missing: {0}", string.Join(", ", missingIndexes));
    }

    [Fact]
    public async Task EF_Migrations_History_Table_Exists_And_Is_Populated()
    {
        await using var context = await _fixture.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """SELECT COUNT(*)::int FROM "__EFMigrationsHistory" """;
        var count = (int)(await cmd.ExecuteScalarAsync())!;

        count.Should().BeGreaterThan(0,
            "at least one migration must be recorded in __EFMigrationsHistory");
    }
}
