using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AllergenCheckServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;
    private readonly IAuditLogService _auditLogService;
    private readonly AllergenCheckService _sut;

    private const string UserId = "acting-user-allergen-check-001";
    private int _seededClientId;
    private int _seededMealPlanId;

    public AllergenCheckServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);
        _auditLogService = Substitute.For<IAuditLogService>();
        _sut = new AllergenCheckService(_dbContextFactory, _auditLogService);
        SeedData();
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        var nutritionist = new ApplicationUser
        {
            Id = UserId,
            UserName = "nutritionist@allergentest.com",
            NormalizedUserName = "NUTRITIONIST@ALLERGENTEST.COM",
            Email = "nutritionist@allergentest.com",
            NormalizedEmail = "NUTRITIONIST@ALLERGENTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Test",
            LastName = "AllergenClient",
            PrimaryNutritionistId = UserId,
            ConsentGiven = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;

        // Seed a meal plan with foods that will trigger allergen matching:
        //   - "Peanut butter sandwich"  → Peanut (category) + Gluten (category)
        //   - "Grilled chicken breast"  → no allergens
        //   - "Kiwi smoothie"           → Kiwi (direct match, no category)
        var plan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = UserId,
            Title = "Allergen Test Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Days =
            [
                new MealPlanDay
                {
                    DayNumber = 1,
                    Label = "Day 1",
                    MealSlots =
                    [
                        new MealSlot
                        {
                            MealType = MealType.Breakfast,
                            SortOrder = 0,
                            Items =
                            [
                                new MealItem
                                {
                                    FoodName = "Peanut butter sandwich",
                                    Quantity = 1,
                                    Unit = "serving",
                                    CaloriesKcal = 350,
                                    ProteinG = 12,
                                    CarbsG = 40,
                                    FatG = 16,
                                    SortOrder = 0
                                },
                                new MealItem
                                {
                                    FoodName = "Grilled chicken breast",
                                    Quantity = 150,
                                    Unit = "g",
                                    CaloriesKcal = 250,
                                    ProteinG = 45,
                                    CarbsG = 0,
                                    FatG = 5,
                                    SortOrder = 1
                                },
                                new MealItem
                                {
                                    FoodName = "Kiwi smoothie",
                                    Quantity = 1,
                                    Unit = "glass",
                                    CaloriesKcal = 120,
                                    ProteinG = 2,
                                    CarbsG = 28,
                                    FatG = 1,
                                    SortOrder = 2
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        _dbContext.MealPlans.Add(plan);
        _dbContext.SaveChanges();

        _seededMealPlanId = plan.Id;
    }

    private void SeedAllergyPeanutSevere()
    {
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Peanut allergy",
            Severity = AllergySeverity.Severe,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private void SeedAllergyGlutenModerate()
    {
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Gluten intolerance",
            Severity = AllergySeverity.Moderate,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    private void SeedAllergyKiwiMild()
    {
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Kiwi",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();
    }

    // ---------------------------------------------------------------------------
    // CheckAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CheckAsync_WithNonExistentMealPlan_ReturnsEmptyList()
    {
        // Arrange
        const int nonExistentId = 99999;

        // Act
        var result = await _sut.CheckAsync(nonExistentId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_WhenClientHasNoFoodAllergies_ReturnsEmptyList()
    {
        // Arrange – no allergies seeded, so the client has none

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_WhenClientHasOnlyNonFoodAllergies_ReturnsEmptyList()
    {
        // Arrange – drug allergy should be ignored by the service
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Penicillin",
            Severity = AllergySeverity.Severe,
            AllergyType = AllergyType.Drug,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAsync_WithPeanutAllergy_DetectsWarningForPeanutButterSandwich()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().ContainSingle(w =>
            w.FoodName == "Peanut butter sandwich" &&
            w.AllergenCategory == AllergenCategory.Peanut &&
            w.MatchedAllergyName == "Peanut allergy" &&
            w.Severity == AllergySeverity.Severe);
    }

    [Fact]
    public async Task CheckAsync_WithPeanutAllergy_DoesNotFlagNonAllergenFood()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().NotContain(w => w.FoodName == "Grilled chicken breast");
    }

    [Fact]
    public async Task CheckAsync_WithGlutenAllergy_DoesNotFlagFoodWithoutGlutenKeyword()
    {
        // Arrange – "Peanut butter sandwich" does not contain any gluten keywords
        // (gluten, wheat, barley, etc.), so no gluten warning should be raised.
        SeedAllergyGlutenModerate();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().NotContain(w =>
            w.AllergenCategory == AllergenCategory.Gluten &&
            w.FoodName == "Peanut butter sandwich");
    }

    [Fact]
    public async Task CheckAsync_WithKiwiAllergyAndDirectMatch_DetectsWarningForKiwiSmoothie()
    {
        // Arrange – "Kiwi" has no mapped category so falls back to DirectMatch
        SeedAllergyKiwiMild();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        result.Should().ContainSingle(w =>
            w.FoodName == "Kiwi smoothie" &&
            w.AllergenCategory == null &&
            w.MatchedAllergyName == "Kiwi" &&
            w.Severity == AllergySeverity.Mild);
    }

    [Fact]
    public async Task CheckAsync_WithMultipleAllergies_DetectsAllMatchingWarnings()
    {
        // Arrange
        SeedAllergyPeanutSevere();
        SeedAllergyKiwiMild();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert – one warning for peanut butter, one for kiwi smoothie
        result.Should().HaveCount(2);
        result.Should().Contain(w => w.FoodName == "Peanut butter sandwich" && w.AllergenCategory == AllergenCategory.Peanut);
        result.Should().Contain(w => w.FoodName == "Kiwi smoothie" && w.AllergenCategory == null);
    }

    [Fact]
    public async Task CheckAsync_ReturnsCorrectDayAndMealTypeContext()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert – metadata from the meal plan structure is surfaced correctly
        var warning = result.Should().ContainSingle().Subject;
        warning.DayNumber.Should().Be(1);
        warning.DayLabel.Should().Be("Day 1");
        warning.MealType.Should().Be(MealType.Breakfast);
    }

    [Fact]
    public async Task CheckAsync_WithExistingOverride_SetsIsOverriddenTrue()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = "Peanut butter sandwich",
            AllergenCategory = AllergenCategory.Peanut,
            OverrideNote = "Client aware of risk",
            AcknowledgedByUserId = UserId,
            AcknowledgedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        var warning = result.Should().ContainSingle().Subject;
        warning.IsOverridden.Should().BeTrue();
        warning.OverrideNote.Should().Be("Client aware of risk");
        warning.AcknowledgedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CheckAsync_WithNoOverride_SetsIsOverriddenFalse()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert
        var warning = result.Should().ContainSingle().Subject;
        warning.IsOverridden.Should().BeFalse();
        warning.OverrideNote.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_IgnoresSoftDeletedAllergies()
    {
        // Arrange – soft-deleted allergy should not produce warnings
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Peanut allergy",
            Severity = AllergySeverity.Severe,
            AllergyType = AllergyType.Food,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.CheckAsync(_seededMealPlanId);

        // Assert – the global query filter excludes soft-deleted rows
        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // AcknowledgeAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task AcknowledgeAsync_WhenNoExistingOverride_CreatesNewOverrideRecord()
    {
        // Arrange
        const string foodName = "Peanut butter sandwich";
        const AllergenCategory category = AllergenCategory.Peanut;
        const string note = "Client accepts the risk";

        // Act
        await _sut.AcknowledgeAsync(_seededMealPlanId, foodName, category, note, UserId);

        // Assert
        var saved = _dbContext.AllergenWarningOverrides
            .SingleOrDefault(o => o.MealPlanId == _seededMealPlanId && o.FoodName == foodName);

        saved.Should().NotBeNull();
        saved!.AllergenCategory.Should().Be(category);
        saved.OverrideNote.Should().Be(note);
        saved.AcknowledgedByUserId.Should().Be(UserId);
        saved.AcknowledgedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task AcknowledgeAsync_WhenOverrideAlreadyExists_UpdatesExistingRecord()
    {
        // Arrange – seed an existing override
        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = "Peanut butter sandwich",
            AllergenCategory = AllergenCategory.Peanut,
            OverrideNote = "Original note",
            AcknowledgedByUserId = "other-user",
            AcknowledgedAt = DateTime.UtcNow.AddDays(-1)
        });
        _dbContext.SaveChanges();

        const string updatedNote = "Updated note after review";

        // Act
        await _sut.AcknowledgeAsync(_seededMealPlanId, "Peanut butter sandwich", AllergenCategory.Peanut, updatedNote, UserId);

        // Assert – only one record exists and it reflects the update.
        // Clear the change tracker so the subsequent query re-reads from SQLite
        // rather than returning the stale entity tracked from the Arrange step.
        _dbContext.ChangeTracker.Clear();
        var overrides = _dbContext.AllergenWarningOverrides
            .Where(o => o.MealPlanId == _seededMealPlanId && o.FoodName == "Peanut butter sandwich")
            .ToList();

        overrides.Should().HaveCount(1);
        overrides[0].OverrideNote.Should().Be(updatedNote);
        overrides[0].AcknowledgedByUserId.Should().Be(UserId);

        // Audit log should still fire on upsert
        await _auditLogService.Received(1).LogAsync(
            Arg.Is(UserId),
            Arg.Is("AllergenWarningAcknowledged"),
            Arg.Is("MealPlan"),
            Arg.Is(_seededMealPlanId.ToString()),
            Arg.Is<string>(s => s.Contains("Peanut butter sandwich")));
    }

    [Fact]
    public async Task AcknowledgeAsync_WithNullCategory_CreatesOverrideForDirectMatchAllergy()
    {
        // Arrange – Kiwi has no category
        const string foodName = "Kiwi smoothie";
        const string note = "Patient notified";

        // Act
        await _sut.AcknowledgeAsync(_seededMealPlanId, foodName, null, note, UserId);

        // Assert
        var saved = _dbContext.AllergenWarningOverrides
            .SingleOrDefault(o => o.MealPlanId == _seededMealPlanId && o.FoodName == foodName && o.AllergenCategory == null);

        saved.Should().NotBeNull();
        saved!.OverrideNote.Should().Be(note);
    }

    [Fact]
    public async Task AcknowledgeAsync_LogsAuditEntry()
    {
        // Arrange
        const string foodName = "Peanut butter sandwich";

        // Act
        await _sut.AcknowledgeAsync(_seededMealPlanId, foodName, AllergenCategory.Peanut, "Safe to proceed", UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            Arg.Is(UserId),
            Arg.Is("AllergenWarningAcknowledged"),
            Arg.Is("MealPlan"),
            Arg.Is(_seededMealPlanId.ToString()),
            Arg.Is<string>(s => s.Contains(foodName)));
    }

    // ---------------------------------------------------------------------------
    // RemoveAcknowledgementAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task RemoveAcknowledgementAsync_WhenOverrideExists_RemovesRecord()
    {
        // Arrange
        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = "Peanut butter sandwich",
            AllergenCategory = AllergenCategory.Peanut,
            OverrideNote = "Will be removed",
            AcknowledgedByUserId = UserId,
            AcknowledgedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        await _sut.RemoveAcknowledgementAsync(_seededMealPlanId, "Peanut butter sandwich", AllergenCategory.Peanut, UserId);

        // Assert
        var remaining = _dbContext.AllergenWarningOverrides
            .Where(o => o.MealPlanId == _seededMealPlanId)
            .ToList();

        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveAcknowledgementAsync_WhenOverrideDoesNotExist_DoesNotThrow()
    {
        // Arrange – no overrides in database

        // Act
        var act = async () => await _sut.RemoveAcknowledgementAsync(_seededMealPlanId, "Peanut butter sandwich", AllergenCategory.Peanut, UserId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RemoveAcknowledgementAsync_WhenOverrideDoesNotExist_DoesNotCallAuditLog()
    {
        // Arrange – no overrides in database

        // Act
        await _sut.RemoveAcknowledgementAsync(_seededMealPlanId, "Peanut butter sandwich", AllergenCategory.Peanut, UserId);

        // Assert – audit log should NOT be called if there was nothing to remove
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task RemoveAcknowledgementAsync_WhenOverrideExists_LogsAuditEntry()
    {
        // Arrange
        const string foodName = "Kiwi smoothie";

        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = foodName,
            AllergenCategory = null,
            OverrideNote = "Direct match override",
            AcknowledgedByUserId = UserId,
            AcknowledgedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        await _sut.RemoveAcknowledgementAsync(_seededMealPlanId, foodName, null, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            Arg.Is(UserId),
            Arg.Is("AllergenWarningAcknowledgementRemoved"),
            Arg.Is("MealPlan"),
            Arg.Is(_seededMealPlanId.ToString()),
            Arg.Is<string>(s => s.Contains(foodName)));
    }

    // ---------------------------------------------------------------------------
    // CanActivateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CanActivateAsync_WhenNoWarnings_ReturnsTrue()
    {
        // Arrange – no food allergies seeded; CheckAsync returns []

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanActivateAsync_WhenOnlyMildWarningsExist_ReturnsTrue()
    {
        // Arrange – Kiwi is Mild; mild unoverridden warnings do not block activation
        SeedAllergyKiwiMild();

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanActivateAsync_WhenOnlyModerateWarningsExist_ReturnsTrue()
    {
        // Arrange – Gluten is Moderate; moderate unoverridden warnings do not block activation.
        // Seed a food that triggers gluten directly to ensure a warning is generated.
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Gluten intolerance",
            Severity = AllergySeverity.Moderate,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });

        // Add a gluten-containing food item to the plan so a warning is actually raised
        var plan = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstAsync(mp => mp.Id == _seededMealPlanId);

        plan.Days[0].MealSlots[0].Items.Add(new MealItem
        {
            FoodName = "Whole wheat bread",
            Quantity = 2,
            Unit = "slice",
            CaloriesKcal = 140,
            ProteinG = 5,
            CarbsG = 28,
            FatG = 2,
            SortOrder = 3
        });
        await _dbContext.SaveChangesAsync();

        // Verify a moderate warning is actually generated (guards against false pass)
        var warnings = await _sut.CheckAsync(_seededMealPlanId);
        warnings.Should().Contain(w =>
            w.AllergenCategory == AllergenCategory.Gluten &&
            w.Severity == AllergySeverity.Moderate);

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanActivateAsync_WhenSevereUnacknowledgedWarningExists_ReturnsFalse()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanActivateAsync_WhenAllSevereWarningsAreAcknowledged_ReturnsTrue()
    {
        // Arrange
        SeedAllergyPeanutSevere();

        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = "Peanut butter sandwich",
            AllergenCategory = AllergenCategory.Peanut,
            OverrideNote = "Patient accepts risk in writing",
            AcknowledgedByUserId = UserId,
            AcknowledgedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanActivateAsync_WhenSeverePlusAcknowledgedAndMildUnacknowledgedExist_ReturnsTrue()
    {
        // Arrange – severe is acknowledged (should not block), mild is not (should not block either)
        SeedAllergyPeanutSevere();
        SeedAllergyKiwiMild();

        // Acknowledge only the severe warning
        _dbContext.AllergenWarningOverrides.Add(new AllergenWarningOverride
        {
            MealPlanId = _seededMealPlanId,
            FoodName = "Peanut butter sandwich",
            AllergenCategory = AllergenCategory.Peanut,
            OverrideNote = "Acknowledged",
            AcknowledgedByUserId = UserId,
            AcknowledgedAt = DateTime.UtcNow
        });
        _dbContext.SaveChanges();

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanActivateAsync_WhenSevereUnacknowledgedAndModerateUnacknowledged_ReturnsFalse()
    {
        // Arrange – severe peanut (unacknowledged) + moderate gluten (unacknowledged)
        SeedAllergyPeanutSevere();
        SeedAllergyGlutenModerate();

        // Add a gluten-triggering food so both allergies produce warnings
        var plan = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstAsync(mp => mp.Id == _seededMealPlanId);

        plan.Days[0].MealSlots[0].Items.Add(new MealItem
        {
            FoodName = "Whole wheat bread",
            Quantity = 2,
            Unit = "slice",
            CaloriesKcal = 140,
            ProteinG = 5,
            CarbsG = 28,
            FatG = 2,
            SortOrder = 3
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.CanActivateAsync(_seededMealPlanId);

        // Assert – blocked by the severe unacknowledged warning
        result.Should().BeFalse();
    }
}
