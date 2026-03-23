using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class MealPlanServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAuditLogService _auditLogService;
    private readonly IAllergenCheckService _allergenCheckService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IRetentionTracker _retentionTracker;

    private readonly MealPlanService _sut;

    private const string NutritionistId = "nutritionist-mealplan-test-001";
    private const string UserId = "acting-user-mealplan-001";

    private int _seededClientId;

    public MealPlanServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _auditLogService = Substitute.For<IAuditLogService>();
        _allergenCheckService = Substitute.For<IAllergenCheckService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        // Default: allergen check passes
        _allergenCheckService.CanActivateAsync(Arg.Any<int>()).Returns(true);

        _sut = new MealPlanService(
            _dbContext,
            _dbContextFactory,
            _auditLogService,
            _allergenCheckService,
            _notificationDispatcher,
            _retentionTracker,
            NullLogger<MealPlanService>.Instance);

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
            UserName = "nutritionist@mealplantest.com",
            NormalizedUserName = "NUTRITIONIST@MEALPLANTEST.COM",
            Email = "nutritionist@mealplantest.com",
            NormalizedEmail = "NUTRITIONIST@MEALPLANTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Test",
            LastName = "MealPlanClient",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    private async Task<int> SeedMealPlanWithContentAsync()
    {
        var plan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Test Plan",
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
                                    FoodName = "Oatmeal",
                                    Quantity = 1,
                                    Unit = "cup",
                                    CaloriesKcal = 150,
                                    ProteinG = 5,
                                    CarbsG = 27,
                                    FatG = 3,
                                    SortOrder = 0
                                }
                            ]
                        }
                    ]
                }
            ]
        };
        _dbContext.MealPlans.Add(plan);
        await _dbContext.SaveChangesAsync();
        return plan.Id;
    }

    private CreateMealPlanDto BuildCreateDto(string title = "My Plan", int numberOfDays = 3) =>
        new(
            ClientId: _seededClientId,
            Title: title,
            Description: "A test plan",
            StartDate: new DateOnly(2026, 4, 1),
            EndDate: new DateOnly(2026, 4, 7),
            CalorieTarget: 2000m,
            ProteinTargetG: 150m,
            CarbsTargetG: 200m,
            FatTargetG: 70m,
            Notes: "Some notes",
            Instructions: "Some instructions",
            NumberOfDays: numberOfDays);

    // ---------------------------------------------------------------------------
    // CreateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithValidDto_SetsCorrectFields()
    {
        // Arrange
        var dto = BuildCreateDto("My New Plan");

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.Title.Should().Be("My New Plan");
        result.ClientId.Should().Be(_seededClientId);
        result.Status.Should().Be(MealPlanStatus.Draft);
        result.CreatedByUserId.Should().Be(NutritionistId);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_CreatesCorrectNumberOfDays()
    {
        // Arrange
        var dto = BuildCreateDto(numberOfDays: 5);

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.Days.Should().HaveCount(5);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_DaysAreLabeledCorrectly()
    {
        // Arrange
        var dto = BuildCreateDto(numberOfDays: 3);

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.Days[0].Label.Should().Be("Day 1");
        result.Days[1].Label.Should().Be("Day 2");
        result.Days[2].Label.Should().Be("Day 3");
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsMealPlanDetailDtoWithClientName()
    {
        // Arrange
        var dto = BuildCreateDto();

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.ClientFirstName.Should().Be("Test");
        result.ClientLastName.Should().Be("MealPlanClient");
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_ReturnsCreatedByName()
    {
        // Arrange
        var dto = BuildCreateDto();

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.CreatedByName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_CallsAuditLogWithMealPlanCreated()
    {
        // Arrange
        var dto = BuildCreateDto();

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanCreated",
            "MealPlan",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains(_seededClientId.ToString())));
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = BuildCreateDto();

        // Act
        await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateAsync_WithValidDto_DispatchesCreatedNotification()
    {
        // Arrange
        var dto = BuildCreateDto();

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "MealPlan" &&
            n.EntityId == result.Id &&
            n.ChangeType == EntityChangeType.Created &&
            n.ClientId == _seededClientId));
    }

    [Theory]
    [InlineData(0, 1)]  // 0 clamped to 1
    [InlineData(10, 7)] // 10 clamped to 7
    public async Task CreateAsync_NumberOfDaysClamped_ClampsToValidRange(int input, int expected)
    {
        // Arrange
        var dto = BuildCreateDto(numberOfDays: input);

        // Act
        var result = await _sut.CreateAsync(dto, NutritionistId);

        // Assert
        result.Days.Should().HaveCount(expected,
            because: $"NumberOfDays={input} should be clamped to {expected}");
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WithExistingPlan_ReturnsMealPlanDetailDto()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetByIdAsync(planId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(planId);
        result.Title.Should().Be("Test Plan");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(999_901);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingPlan_IncludesClientName()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetByIdAsync(planId);

        // Assert
        result!.ClientFirstName.Should().Be("Test");
        result.ClientLastName.Should().Be("MealPlanClient");
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingPlan_IncludesCreatorName()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetByIdAsync(planId);

        // Assert
        result!.CreatedByName.Should().Be("Jane Smith");
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingPlan_DaysOrderedByDayNumber()
    {
        // Arrange — create a plan with days in reverse order
        var plan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Order Test",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow,
            Days =
            [
                new MealPlanDay { DayNumber = 3, Label = "Day 3" },
                new MealPlanDay { DayNumber = 1, Label = "Day 1" },
                new MealPlanDay { DayNumber = 2, Label = "Day 2" }
            ]
        };
        _dbContext.MealPlans.Add(plan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(plan.Id);

        // Assert
        result!.Days.Select(d => d.DayNumber).Should().BeInAscendingOrder();
    }

    // ---------------------------------------------------------------------------
    // GetListAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetListAsync_WithNoFilters_ReturnsAllPlans()
    {
        // Arrange — seed two plans
        await SeedMealPlanWithContentAsync();
        await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetListAsync();

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetListAsync_FilterByClientId_ReturnsOnlyMatchingPlans()
    {
        // Arrange — add a second client with its own plan
        var otherClient = new Client
        {
            FirstName = "Other",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(otherClient);
        await _dbContext.SaveChangesAsync();

        await SeedMealPlanWithContentAsync(); // belongs to _seededClientId

        var otherPlan = new MealPlan
        {
            ClientId = otherClient.Id,
            CreatedByUserId = NutritionistId,
            Title = "Other Client Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MealPlans.Add(otherPlan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetListAsync(clientId: _seededClientId);

        // Assert
        result.Should().AllSatisfy(p => p.ClientId.Should().Be(_seededClientId));
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetListAsync_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        // Arrange — seed one Draft and one Active plan
        await SeedMealPlanWithContentAsync(); // Draft

        var activePlan = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Active Plan",
            Status = MealPlanStatus.Active,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.MealPlans.Add(activePlan);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetListAsync(status: MealPlanStatus.Active);

        // Assert
        result.Should().AllSatisfy(p => p.Status.Should().Be(MealPlanStatus.Active));
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetListAsync_WithNoMatchingFilters_ReturnsEmptyList()
    {
        // Act — no Archived plans have been seeded
        var result = await _sut.GetListAsync(status: MealPlanStatus.Archived);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetListAsync_ReturnsPlansOrderedByCreatedAtDescending()
    {
        // Arrange — seed plans with explicit timestamps
        var older = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Older Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var newer = new MealPlan
        {
            ClientId = _seededClientId,
            CreatedByUserId = NutritionistId,
            Title = "Newer Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        _dbContext.MealPlans.AddRange(older, newer);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetListAsync();

        // Assert — newest first
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer Plan");
        result[1].Title.Should().Be("Older Plan");
    }

    // ---------------------------------------------------------------------------
    // GetPagedAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetPagedAsync_DefaultQuery_ReturnsAllPlans()
    {
        // Arrange
        await SeedMealPlanWithContentAsync();
        await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery());

        // Assert
        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPagedAsync_WithPageSize_RespectsPageSize()
    {
        // Arrange — seed 5 plans
        for (var i = 0; i < 5; i++)
            await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(Page: 1, PageSize: 2));

        // Assert
        result.Items.Should().HaveCount(2);
        result.TotalCount.Should().Be(5);
    }

    [Fact]
    public async Task GetPagedAsync_PageTwo_ReturnsCorrectPage()
    {
        // Arrange — seed 5 plans
        for (var i = 0; i < 5; i++)
            await SeedMealPlanWithContentAsync();

        // Act — page 2 with size 3 should yield 2 items
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(Page: 2, PageSize: 3));

        // Assert
        result.Items.Should().HaveCount(2);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetPagedAsync_SortByTitleAscending_ReturnsSortedResults()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Zebra Plan", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Apple Plan", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(
            SortColumn: "title",
            SortDirection: SortDirection.Ascending));

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Title.Should().Be("Apple Plan");
        result.Items[1].Title.Should().Be("Zebra Plan");
    }

    [Fact]
    public async Task GetPagedAsync_SortByTitleDescending_ReturnsSortedResults()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Apple Plan", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Zebra Plan", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(
            SortColumn: "title",
            SortDirection: SortDirection.Descending));

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Title.Should().Be("Zebra Plan");
        result.Items[1].Title.Should().Be("Apple Plan");
    }

    [Fact]
    public async Task GetPagedAsync_FilterByClientId_ReturnsOnlyMatchingPlans()
    {
        // Arrange
        var otherClient = new Client
        {
            FirstName = "Other",
            LastName = "Person",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            EmailRemindersEnabled = false,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(otherClient);
        await _dbContext.SaveChangesAsync();

        await SeedMealPlanWithContentAsync();
        _dbContext.MealPlans.Add(new MealPlan
        {
            ClientId = otherClient.Id,
            CreatedByUserId = NutritionistId,
            Title = "Other Plan",
            Status = MealPlanStatus.Draft,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(ClientId: _seededClientId));

        // Assert
        result.Items.Should().AllSatisfy(p => p.ClientId.Should().Be(_seededClientId));
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_FilterByStatus_ReturnsOnlyMatchingStatus()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Draft Plan", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Active Plan", Status = MealPlanStatus.Active, CreatedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery(StatusFilter: MealPlanStatus.Draft));

        // Assert
        result.Items.Should().AllSatisfy(p => p.Status.Should().Be(MealPlanStatus.Draft));
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetPagedAsync_DefaultSort_OrdersByCreatedAtDescending()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Older", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow.AddDays(-3) },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Newer", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetPagedAsync(new MealPlanListQuery());

        // Assert
        result.Items.Should().HaveCount(2);
        result.Items[0].Title.Should().Be("Newer");
        result.Items[1].Title.Should().Be("Older");
    }

    // ---------------------------------------------------------------------------
    // UpdateMetadataAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateMetadataAsync_WithExistingPlan_UpdatesAllMetadataFields()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        var updateDto = new CreateMealPlanDto(
            ClientId: _seededClientId,
            Title: "Updated Title",
            Description: "Updated description",
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 5, 7),
            CalorieTarget: 1800m,
            ProteinTargetG: 120m,
            CarbsTargetG: 180m,
            FatTargetG: 60m,
            Notes: "Updated notes",
            Instructions: "Updated instructions",
            NumberOfDays: 3);

        // Act
        var result = await _sut.UpdateMetadataAsync(planId, updateDto, NutritionistId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.Title.Should().Be("Updated Title");
        persisted.Description.Should().Be("Updated description");
        persisted.StartDate.Should().Be(new DateOnly(2026, 5, 1));
        persisted.EndDate.Should().Be(new DateOnly(2026, 5, 7));
        persisted.CalorieTarget.Should().Be(1800m);
        persisted.ProteinTargetG.Should().Be(120m);
        persisted.CarbsTargetG.Should().Be(180m);
        persisted.FatTargetG.Should().Be(60m);
        persisted.Notes.Should().Be("Updated notes");
        persisted.Instructions.Should().Be("Updated instructions");
    }

    [Fact]
    public async Task UpdateMetadataAsync_WithExistingPlan_ReturnsTrue()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.UpdateMetadataAsync(planId, BuildCreateDto(), NutritionistId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateMetadataAsync_WithNonExistentPlan_ReturnsFalse()
    {
        // Act
        var result = await _sut.UpdateMetadataAsync(999_901, BuildCreateDto(), NutritionistId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMetadataAsync_WithExistingPlan_SetsUpdatedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateMetadataAsync(planId, BuildCreateDto(), NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task UpdateMetadataAsync_WithExistingPlan_CallsAuditLogWithMealPlanUpdated()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateMetadataAsync(planId, BuildCreateDto(), NutritionistId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanUpdated",
            "MealPlan",
            planId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateMetadataAsync_WithExistingPlan_CallsRetentionTracker()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateMetadataAsync(planId, BuildCreateDto(), NutritionistId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    // ---------------------------------------------------------------------------
    // SaveContentAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SaveContentAsync_WithExistingPlan_ReturnsTrue()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        var contentDto = BuildSaveContentDto(planId);

        // Act
        var result = await _sut.SaveContentAsync(contentDto, NutritionistId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SaveContentAsync_WithNonExistentPlan_ReturnsFalse()
    {
        // Arrange
        var contentDto = BuildSaveContentDto(999_901);

        // Act
        var result = await _sut.SaveContentAsync(contentDto, NutritionistId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SaveContentAsync_WithExistingPlan_ReplacesExistingDaysWithNewStructure()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        var newContent = new SaveMealPlanContentDto(
            MealPlanId: planId,
            Days:
            [
                new SaveMealPlanDayDto(
                    Id: null, DayNumber: 1, Label: "New Day 1", Notes: null,
                    MealSlots:
                    [
                        new SaveMealSlotDto(
                            Id: null, MealType: MealType.Lunch, CustomName: null, SortOrder: 0, Notes: null,
                            Items:
                            [
                                new SaveMealItemDto(null, "Salad", 1, "bowl", 200, 10, 20, 5, null, 0),
                                new SaveMealItemDto(null, "Chicken", 150, "g", 250, 30, 0, 5, null, 1)
                            ])
                    ])
            ]);

        // Act
        await _sut.SaveContentAsync(newContent, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstAsync(mp => mp.Id == planId);

        var day = persisted.Days.Single();
        day.Label.Should().Be("New Day 1");
        var slot = day.MealSlots.Single();
        slot.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveContentAsync_WithExistingPlan_SetsUpdatedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SaveContentAsync(BuildSaveContentDto(planId), NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task SaveContentAsync_WithExistingPlan_CallsAuditLogWithMealPlanContentSaved()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SaveContentAsync(BuildSaveContentDto(planId), NutritionistId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanContentSaved",
            "MealPlan",
            planId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SaveContentAsync_NewItems_HaveCorrectFieldValues()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        var newContent = new SaveMealPlanContentDto(
            MealPlanId: planId,
            Days:
            [
                new SaveMealPlanDayDto(null, 1, "Day 1", null,
                [
                    new SaveMealSlotDto(null, MealType.Dinner, "Custom Dinner", 0, "Slot note",
                    [
                        new SaveMealItemDto(null, "Pasta", 2, "cups", 400, 15, 70, 8, "Item note", 0)
                    ])
                ])
            ]);

        // Act
        await _sut.SaveContentAsync(newContent, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans
            .Include(mp => mp.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .FirstAsync(mp => mp.Id == planId);

        var item = persisted.Days.Single().MealSlots.Single().Items.Single();
        item.FoodName.Should().Be("Pasta");
        item.Quantity.Should().Be(2);
        item.Unit.Should().Be("cups");
        item.CaloriesKcal.Should().Be(400);
        item.ProteinG.Should().Be(15);
        item.CarbsG.Should().Be(70);
        item.FatG.Should().Be(8);
        item.Notes.Should().Be("Item note");
        item.SortOrder.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // UpdateStatusAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateStatusAsync_DraftToActive_SucceedsWhenAllergenCheckPasses()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        _allergenCheckService.CanActivateAsync(planId).Returns(true);

        // Act
        var result = await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        result.Success.Should().BeTrue();
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.Status.Should().Be(MealPlanStatus.Active);
    }

    [Fact]
    public async Task UpdateStatusAsync_ActiveToArchived_Succeeds()
    {
        // Arrange — force status to Active directly in DB
        var planId = await SeedMealPlanWithContentAsync();
        var entity = await _dbContext.MealPlans.FindAsync(planId);
        entity!.Status = MealPlanStatus.Active;
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.UpdateStatusAsync(planId, MealPlanStatus.Archived, NutritionistId);

        // Assert
        result.Success.Should().BeTrue();
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.Status.Should().Be(MealPlanStatus.Archived);
    }

    [Fact]
    public async Task UpdateStatusAsync_DraftToActive_BlockedWhenAllergenCheckFails()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        _allergenCheckService.CanActivateAsync(planId).Returns(false);

        // Act
        var result = await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateStatusAsync_DraftToActive_BlockedWhenAllergenCheckFails_StatusRemainsUnchanged()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        _allergenCheckService.CanActivateAsync(planId).Returns(false);

        // Act
        await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert — status should not have changed
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.Status.Should().Be(MealPlanStatus.Draft);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithNonExistentPlan_ReturnsNotFoundError()
    {
        // Act
        var result = await _sut.UpdateStatusAsync(999_901, MealPlanStatus.Active, NutritionistId);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task UpdateStatusAsync_WithExistingPlan_CallsAuditLogWithStatusChangedAndTransitionDetail()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanStatusChanged",
            "MealPlan",
            planId.ToString(),
            Arg.Is<string>(s => s.Contains("Draft") && s.Contains("Active")));
    }

    [Fact]
    public async Task UpdateStatusAsync_BlockedByAllergenGate_DoesNotCallAuditLog()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();
        _allergenCheckService.CanActivateAsync(planId).Returns(false);

        // Act
        await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task UpdateStatusAsync_WithSuccessfulTransition_CallsRetentionTracker()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateStatusAsync_WithExistingPlan_SetsUpdatedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.UpdateStatusAsync(planId, MealPlanStatus.Active, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.FindAsync(planId);
        persisted!.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeAfter(before);
    }

    // ---------------------------------------------------------------------------
    // DuplicateAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DuplicateAsync_WithExistingPlan_ReturnsTrue()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateAsync_WithNonExistentPlan_ReturnsFalse()
    {
        // Act
        var result = await _sut.DuplicateAsync(999_901, NutritionistId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DuplicateAsync_Copy_HasDraftStatus()
    {
        // Arrange — source is Draft
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert — find the copy by title suffix
        var copy = await _dbContext.MealPlans.SingleAsync(p => p.Title.EndsWith(" (Copy)"));
        copy.Status.Should().Be(MealPlanStatus.Draft);
    }

    [Fact]
    public async Task DuplicateAsync_Copy_TitleHasCopySuffix()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert
        var copy = await _dbContext.MealPlans.SingleAsync(p => p.Title.EndsWith(" (Copy)"));
        copy.Title.Should().Be("Test Plan (Copy)");
    }

    [Fact]
    public async Task DuplicateAsync_Copy_HasDifferentIdFromSource()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert
        var copy = await _dbContext.MealPlans.SingleAsync(p => p.Title.EndsWith(" (Copy)"));
        copy.Id.Should().NotBe(planId);
    }

    [Fact]
    public async Task DuplicateAsync_Copy_DeepClonesAllDaysAndSlotsAndItems()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert — the copy should contain the same structure
        var copy = await _dbContext.MealPlans
            .Include(p => p.Days)
                .ThenInclude(d => d.MealSlots)
                    .ThenInclude(s => s.Items)
            .SingleAsync(p => p.Title.EndsWith(" (Copy)"));

        var day = copy.Days.Single();
        var slot = day.MealSlots.Single();
        var item = slot.Items.Single();
        item.FoodName.Should().Be("Oatmeal");
    }

    [Fact]
    public async Task DuplicateAsync_WithExistingPlan_CallsAuditLogWithMealPlanDuplicated()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert — audit is called with copy's ID, not source
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanDuplicated",
            "MealPlan",
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains(planId.ToString())));
    }

    [Fact]
    public async Task DuplicateAsync_WithExistingPlan_CallsRetentionTracker()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.DuplicateAsync(planId, NutritionistId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    // ---------------------------------------------------------------------------
    // SoftDeleteAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_ReturnsTrue()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithNonExistentPlan_ReturnsFalse()
    {
        // Act
        var result = await _sut.SoftDeleteAsync(999_901, NutritionistId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_SetsIsDeletedTrue()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.IgnoreQueryFilters().FirstAsync(p => p.Id == planId);
        persisted.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_SetsDeletedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.IgnoreQueryFilters().FirstAsync(p => p.Id == planId);
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_SetsDeletedBy()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        var persisted = await _dbContext.MealPlans.IgnoreQueryFilters().FirstAsync(p => p.Id == planId);
        persisted.DeletedBy.Should().Be(NutritionistId);
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_CallsAuditLogWithMealPlanSoftDeleted()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            NutritionistId,
            "MealPlanSoftDeleted",
            "MealPlan",
            planId.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_DispatchesDeletedNotification()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "MealPlan" &&
            n.EntityId == planId &&
            n.ChangeType == EntityChangeType.Deleted));
    }

    [Fact]
    public async Task SoftDeleteAsync_WithExistingPlan_DoesNotCallRetentionTracker()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert — SoftDeleteAsync does NOT call retention tracker per spec
        await _retentionTracker.DidNotReceive().UpdateLastInteractionAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task SoftDeleteAsync_DeletedPlan_ExcludedFromNormalQueries()
    {
        // Arrange
        var planId = await SeedMealPlanWithContentAsync();

        // Act
        await _sut.SoftDeleteAsync(planId, NutritionistId);

        // Assert — normal query should not return soft-deleted plan
        var result = await _sut.GetListAsync();
        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // GetByClientAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByClientAsync_ReturnsPlansForSpecificClient()
    {
        // Arrange
        await SeedMealPlanWithContentAsync();
        await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetByClientAsync(_seededClientId);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(p => p.ClientId.Should().Be(_seededClientId));
    }

    [Fact]
    public async Task GetByClientAsync_RespectsCountLimit()
    {
        // Arrange — seed more plans than the limit
        for (var i = 0; i < 5; i++)
            await SeedMealPlanWithContentAsync();

        // Act
        var result = await _sut.GetByClientAsync(_seededClientId, count: 3);

        // Assert
        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetByClientAsync_OrdersByCreatedAtDescending()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Older", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow.AddDays(-2) },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Newer", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow.AddDays(-1) }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByClientAsync(_seededClientId, count: 10);

        // Assert
        result.Should().HaveCount(2);
        result[0].Title.Should().Be("Newer");
        result[1].Title.Should().Be("Older");
    }

    [Fact]
    public async Task GetByClientAsync_ClientWithNoPlans_ReturnsEmptyList()
    {
        // Act — client exists but has no plans
        var result = await _sut.GetByClientAsync(_seededClientId);

        // Assert
        result.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------
    // GetActiveCountAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetActiveCountAsync_WithActivePlans_ReturnsOnlyActiveCount()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Active 1", Status = MealPlanStatus.Active, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Active 2", Status = MealPlanStatus.Active, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Draft 1", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Archived 1", Status = MealPlanStatus.Archived, CreatedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(2);
    }

    [Fact]
    public async Task GetActiveCountAsync_ExcludesDraftAndArchivedPlans()
    {
        // Arrange
        _dbContext.MealPlans.AddRange(
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Draft", Status = MealPlanStatus.Draft, CreatedAt = DateTime.UtcNow },
            new MealPlan { ClientId = _seededClientId, CreatedByUserId = NutritionistId, Title = "Archived", Status = MealPlanStatus.Archived, CreatedAt = DateTime.UtcNow }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetActiveCountAsync_WithNoActivePlans_ReturnsZero()
    {
        // Act — no plans seeded
        var count = await _sut.GetActiveCountAsync();

        // Assert
        count.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // Private helpers
    // ---------------------------------------------------------------------------

    private static SaveMealPlanContentDto BuildSaveContentDto(int mealPlanId) =>
        new(
            MealPlanId: mealPlanId,
            Days:
            [
                new SaveMealPlanDayDto(
                    Id: null,
                    DayNumber: 1,
                    Label: "Day 1",
                    Notes: null,
                    MealSlots:
                    [
                        new SaveMealSlotDto(
                            Id: null,
                            MealType: MealType.Breakfast,
                            CustomName: null,
                            SortOrder: 0,
                            Notes: null,
                            Items:
                            [
                                new SaveMealItemDto(null, "Eggs", 2, "units", 140, 12, 1, 10, null, 0)
                            ])
                    ])
            ]);

    // ---------------------------------------------------------------------------
    // Cleanup
    // ---------------------------------------------------------------------------

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }
}
