using FluentAssertions;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class SearchServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly SearchService _sut;

    private const string NutritionistId = "nutritionist-search-test-001";
    private const string OtherUserId = "other-user-search-test-002";

    // Captured after SaveChanges so tests never hard-code magic numbers.
    private int _clientId;
    private int _otherClientId;

    public SearchServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _sut = new SearchService(_dbContext);
        SeedData();
    }

    // ---------------------------------------------------------------------------
    // Seed helpers
    // ---------------------------------------------------------------------------

    private void SeedData()
    {
        // Seed two ApplicationUser rows so FK constraints on Appointment.NutritionistId
        // and MealPlan.CreatedByUserId are satisfied.
        _dbContext.Users.AddRange(
            new ApplicationUser
            {
                Id = NutritionistId,
                UserName = "nutritionist@searchtest.com",
                NormalizedUserName = "NUTRITIONIST@SEARCHTEST.COM",
                Email = "nutritionist@searchtest.com",
                NormalizedEmail = "NUTRITIONIST@SEARCHTEST.COM",
                FirstName = "Jane",
                LastName = "Smith",
                DisplayName = "Jane Smith",
                CreatedDate = DateTime.UtcNow
            },
            new ApplicationUser
            {
                Id = OtherUserId,
                UserName = "other@searchtest.com",
                NormalizedUserName = "OTHER@SEARCHTEST.COM",
                Email = "other@searchtest.com",
                NormalizedEmail = "OTHER@SEARCHTEST.COM",
                FirstName = "Other",
                LastName = "User",
                DisplayName = "Other User",
                CreatedDate = DateTime.UtcNow
            });

        // Seed one client per nutritionist.  Clients must exist before appointments
        // and meal plans because of the FK constraint.
        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Thompson",
            Email = "alice.thompson@example.com",
            Phone = "555-0100",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var otherClient = new Client
        {
            FirstName = "Bob",
            LastName = "Martinez",
            Email = "bob.martinez@example.com",
            PrimaryNutritionistId = OtherUserId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Clients.AddRange(client, otherClient);
        _dbContext.SaveChanges();

        _clientId = client.Id;
        _otherClientId = otherClient.Id;

        // Seed one appointment per client.
        _dbContext.Appointments.AddRange(
            new Appointment
            {
                ClientId = _clientId,
                NutritionistId = NutritionistId,
                Type = AppointmentType.InitialConsultation,
                Status = AppointmentStatus.Scheduled,
                StartTime = DateTime.UtcNow.AddDays(7),
                DurationMinutes = 60,
                CreatedAt = DateTime.UtcNow
            },
            new Appointment
            {
                ClientId = _otherClientId,
                NutritionistId = OtherUserId,
                Type = AppointmentType.FollowUp,
                Status = AppointmentStatus.Confirmed,
                StartTime = DateTime.UtcNow.AddDays(14),
                DurationMinutes = 30,
                CreatedAt = DateTime.UtcNow
            });

        // Seed one meal plan per client.
        _dbContext.MealPlans.AddRange(
            new MealPlan
            {
                ClientId = _clientId,
                CreatedByUserId = NutritionistId,
                Title = "Alice Weight Loss Plan",
                Status = MealPlanStatus.Active,
                CreatedAt = DateTime.UtcNow
            },
            new MealPlan
            {
                ClientId = _otherClientId,
                CreatedByUserId = OtherUserId,
                Title = "Bob Maintenance Plan",
                Status = MealPlanStatus.Draft,
                CreatedAt = DateTime.UtcNow
            });

        _dbContext.SaveChanges();
    }

    // ---------------------------------------------------------------------------
    // Query validation tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsEmptyResults()
    {
        // Act
        var result = await _sut.SearchAsync("", NutritionistId);

        // Assert
        result.Groups.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithShortQuery_ReturnsEmptyResults()
    {
        // Act — single character is below the 2-char minimum
        var result = await _sut.SearchAsync("a", NutritionistId);

        // Assert
        result.Groups.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithNullQuery_ReturnsEmptyResults()
    {
        // Act
        var result = await _sut.SearchAsync(null!, NutritionistId);

        // Assert
        result.Groups.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithWhitespaceOnlyQuery_ReturnsEmptyResults()
    {
        // Act — whitespace-only is treated the same as empty
        var result = await _sut.SearchAsync("   ", NutritionistId);

        // Assert
        result.Groups.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // Core matching / grouping tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithMatchingQuery_ReturnsGroupedResultsAcrossEntities()
    {
        // Arrange — "alice" matches: client (FirstName), appointment (client name), meal plan (client name + title)
        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert — all three entity types should be represented
        result.Groups.Should().HaveCount(3);
        result.Groups.Select(g => g.EntityType).Should().BeEquivalentTo(
            ["Clients", "Appointments", "Meal Plans"],
            because: "a match on the client name propagates to related appointments and meal plans");
    }

    [Fact]
    public async Task SearchAsync_WithNoMatches_ReturnsEmptyGroups()
    {
        // Act — term that matches nothing in the seeded data
        var result = await _sut.SearchAsync("zzznomatch", NutritionistId);

        // Assert
        result.Groups.Should().BeEmpty(because: "empty groups must be excluded from the result");
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_WithClientEmail_MatchesClient()
    {
        // Arrange — search by a substring of the seeded email address
        // Act
        var result = await _sut.SearchAsync("alice.thompson", NutritionistId);

        // Assert
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().NotBeNull(because: "email substring should match the Clients group");
        clientGroup!.Items.Should().ContainSingle(i => i.PrimaryText == "Alice Thompson");
    }

    [Fact]
    public async Task SearchAsync_WithMultiTermQuery_MatchesAllTerms()
    {
        // Arrange — "alice thompson" requires BOTH terms to match (AND logic)
        // Act
        var result = await _sut.SearchAsync("alice thompson", NutritionistId);

        // Assert
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().NotBeNull();
        clientGroup!.Items.Should().ContainSingle(i => i.PrimaryText == "Alice Thompson");
    }

    [Fact]
    public async Task SearchAsync_WithMultiTermQuery_ExcludesPartialMatches()
    {
        // Arrange — seed an extra client whose name only matches one of the two terms
        var partialMatch = new Client
        {
            FirstName = "Alice",
            LastName = "Johnson",   // matches "alice" but NOT "thompson"
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(partialMatch);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SearchAsync("alice thompson", NutritionistId);

        // Assert — only "Alice Thompson" should appear; "Alice Johnson" does not satisfy both terms
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().NotBeNull();
        clientGroup!.Items.Should().NotContain(i => i.PrimaryText == "Alice Johnson");
    }

    // ---------------------------------------------------------------------------
    // maxPerGroup / TotalInGroup tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_WithMaxPerGroup_RespectsLimit()
    {
        // Arrange — seed 3 more clients so we have 4 total matching "thompson" under NutritionistId
        for (var i = 0; i < 3; i++)
        {
            _dbContext.Clients.Add(new Client
            {
                FirstName = $"Extra{i}",
                LastName = "Thompson",
                PrimaryNutritionistId = NutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act — maxPerGroup=2 must cap Items but TotalInGroup must reflect the full count within scope
        var result = await _sut.SearchAsync("thompson", NutritionistId, maxPerGroup: 2);

        // Assert
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().NotBeNull();
        clientGroup!.Items.Should().HaveCount(2,
            because: "maxPerGroup caps the number of returned items");
        clientGroup.TotalInGroup.Should().Be(4,
            because: "TotalInGroup reflects the full count within the user's scope, not the page limit");
    }

    [Fact]
    public async Task SearchAsync_WithDefaultMaxPerGroup_LimitsToThreeItems()
    {
        // Arrange — seed enough clients so more than 3 match
        for (var i = 0; i < 4; i++)
        {
            _dbContext.Clients.Add(new Client
            {
                FirstName = $"Default{i}",
                LastName = "Limitcheck",
                PrimaryNutritionistId = NutritionistId,
                ConsentGiven = true,
                ConsentTimestamp = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync();

        // Act — use default maxPerGroup (3)
        var result = await _sut.SearchAsync("limitcheck", NutritionistId);

        // Assert
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().NotBeNull();
        clientGroup!.Items.Should().HaveCount(3,
            because: "the default maxPerGroup is 3");
        clientGroup.TotalInGroup.Should().Be(4);
    }

    // ---------------------------------------------------------------------------
    // Access control tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_AsNonAdmin_ReturnsOnlyOwnData()
    {
        // Arrange — NutritionistId owns "Alice Thompson"; OtherUserId owns "Bob Martinez"
        // Use separate single-term searches to avoid AND-logic splitting across names.

        // Act
        var thompsonResult = await _sut.SearchAsync("thompson", NutritionistId, isAdmin: false);
        var martinezResult = await _sut.SearchAsync("martinez", NutritionistId, isAdmin: false);

        // Assert — NutritionistId should see their own client but not the other practitioner's
        thompsonResult.Groups.Should().NotBeEmpty(
            because: "NutritionistId owns Alice Thompson");
        thompsonResult.Groups.First(g => g.EntityType == "Clients")
            .Items.Should().ContainSingle(i => i.PrimaryText == "Alice Thompson");

        martinezResult.Groups.Should().BeEmpty(
            because: "Bob Martinez belongs to OtherUserId and must not be visible to NutritionistId");
    }

    [Fact]
    public async Task SearchAsync_AsNonAdmin_FilteredClientsAlsoFilterAppointmentsAndMealPlans()
    {
        // Act — NutritionistId searches for "alice" — their own data
        var result = await _sut.SearchAsync("alice", NutritionistId, isAdmin: false);

        // Assert — Appointments group must only contain alice's appointment (NutritionistId-owned)
        var apptGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Appointments");
        apptGroup.Should().NotBeNull();
        apptGroup!.Items.Should().OnlyContain(i => i.SecondaryText!.Contains("Alice"),
            because: "non-admin appointment results must be scoped to the user's own clients");

        // Meal Plans group must only contain alice's plan
        var mpGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Meal Plans");
        mpGroup.Should().NotBeNull();
        mpGroup!.Items.Should().OnlyContain(i => i.SecondaryText!.Contains("Alice"),
            because: "non-admin meal plan results must be scoped to the user's own data");
    }

    [Fact]
    public async Task SearchAsync_AsAdmin_ReturnsAllData()
    {
        // Arrange — "thompson" matches Alice (NutritionistId), "martinez" matches Bob (OtherUserId)
        // Using a broad term that matches all seeded clients by last-name variety isn't reliable,
        // so we search separately and assert both groups are visible.

        // Act — admin sees alice's data
        var aliceResult = await _sut.SearchAsync("alice", NutritionistId, isAdmin: true);
        // Admin sees bob's data with same userId (the userId only matters for non-admin filtering)
        var bobResult = await _sut.SearchAsync("martinez", NutritionistId, isAdmin: true);

        // Assert
        aliceResult.Groups.Should().NotBeEmpty(because: "admin should see Alice's data");
        bobResult.Groups.Should().NotBeEmpty(because: "admin should see Bob's data even though he belongs to OtherUserId");

        var bobClientGroup = bobResult.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        bobClientGroup.Should().NotBeNull();
        bobClientGroup!.Items.Should().ContainSingle(i => i.PrimaryText == "Bob Martinez");
    }

    [Fact]
    public async Task SearchAsync_AsAdmin_CanSeeOtherPractitionersAppointments()
    {
        // Act — admin searches for "bob" which matches Bob Martinez (OtherUserId-owned appointment)
        var result = await _sut.SearchAsync("bob", NutritionistId, isAdmin: true);

        // Assert
        var apptGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Appointments");
        apptGroup.Should().NotBeNull(because: "admin should see appointments for all practitioners");
        apptGroup!.TotalInGroup.Should().BeGreaterThanOrEqualTo(1);
    }

    // ---------------------------------------------------------------------------
    // Result item shape tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_ClientGroup_ItemsHaveCorrectShape()
    {
        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert
        var clientGroup = result.Groups.First(g => g.EntityType == "Clients");
        var item = clientGroup.Items.First();

        item.Id.Should().Be(_clientId);
        item.PrimaryText.Should().Be("Alice Thompson");
        item.SecondaryText.Should().Be("alice.thompson@example.com");
        item.StatusLabel.Should().Be("Active");
        item.StatusVariant.Should().Be("success");
        item.Url.Should().Be($"/clients/{_clientId}");
        item.Initials.Should().Be("AT");
    }

    [Fact]
    public async Task SearchAsync_AppointmentGroup_ItemsHaveCorrectShape()
    {
        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert
        var apptGroup = result.Groups.First(g => g.EntityType == "Appointments");
        var item = apptGroup.Items.First();

        item.PrimaryText.Should().Be("Initial Consultation",
            because: "FormatAppointmentType should expand InitialConsultation to a readable label");
        item.SecondaryText.Should().Contain("Alice Thompson",
            because: "appointment secondary text includes client name");
        item.StatusLabel.Should().Be("Scheduled");
        item.StatusVariant.Should().Be("accent");
        item.Url.Should().StartWith("/appointments/");
        item.Initials.Should().Be("AT");
    }

    [Fact]
    public async Task SearchAsync_MealPlanGroup_ItemsHaveCorrectShape()
    {
        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert
        var mpGroup = result.Groups.First(g => g.EntityType == "Meal Plans");
        var item = mpGroup.Items.First();

        item.PrimaryText.Should().Be("Alice Weight Loss Plan");
        item.SecondaryText.Should().Contain("Alice Thompson",
            because: "meal plan secondary text includes client name");
        item.StatusLabel.Should().Be("Active");
        item.StatusVariant.Should().Be("primary");
        item.Url.Should().StartWith("/meal-plans/");
        item.Initials.Should().Be("AT");
    }

    // ---------------------------------------------------------------------------
    // Soft-delete exclusion test
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_SoftDeletedClient_IsExcludedFromResults()
    {
        // Arrange — soft-delete Alice Thompson
        var client = await _dbContext.Clients.FindAsync(_clientId);
        client!.IsDeleted = true;
        client.DeletedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert — soft-deleted entities must not appear in any group
        var clientGroup = result.Groups.FirstOrDefault(g => g.EntityType == "Clients");
        clientGroup.Should().BeNull(because: "soft-deleted clients must not appear in search results");
    }

    // ---------------------------------------------------------------------------
    // TotalCount aggregate test
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SearchAsync_TotalCount_IsSumOfAllGroupTotalInGroup()
    {
        // Act
        var result = await _sut.SearchAsync("alice", NutritionistId);

        // Assert
        var expectedTotal = result.Groups.Sum(g => g.TotalInGroup);
        result.TotalCount.Should().Be(expectedTotal,
            because: "TotalCount must equal the sum of TotalInGroup across all groups");
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
