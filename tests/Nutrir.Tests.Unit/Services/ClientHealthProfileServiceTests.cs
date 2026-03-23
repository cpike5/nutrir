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

public class ClientHealthProfileServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;

    private readonly IAuditLogService _auditLogService;
    private readonly IAllergenService _allergenService;
    private readonly IAllergenCheckService _allergenCheckService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IRetentionTracker _retentionTracker;

    private readonly ClientHealthProfileService _sut;

    private const string NutritionistId = "nutritionist-health-profile-test-001";
    private const string UserId = "acting-user-health-profile-001";

    private int _seededClientId;

    public ClientHealthProfileServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();

        _auditLogService = Substitute.For<IAuditLogService>();
        _allergenService = Substitute.For<IAllergenService>();
        _allergenCheckService = Substitute.For<IAllergenCheckService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        // Set up default allergen service response so allergy tests don't need to configure it every time
        _allergenService
            .GetOrCreateAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string>())
            .Returns(new AllergenDto(1, "DefaultAllergen", "Food"));

        // Set up allergen check service to return empty warnings by default
        _allergenCheckService
            .CheckAsync(Arg.Any<int>())
            .Returns(new List<AllergenWarningDto>());

        var dbContextFactory = new SharedConnectionContextFactory(_connection);

        _sut = new ClientHealthProfileService(
            _dbContext,
            dbContextFactory,
            _auditLogService,
            _allergenService,
            _allergenCheckService,
            _notificationDispatcher,
            _retentionTracker,
            NullLogger<ClientHealthProfileService>.Instance);

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
            UserName = "nutritionist@healthprofiletest.com",
            NormalizedUserName = "NUTRITIONIST@HEALTHPROFILETEST.COM",
            Email = "nutritionist@healthprofiletest.com",
            NormalizedEmail = "NUTRITIONIST@HEALTHPROFILETEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Test",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(nutritionist);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;
    }

    // ---------------------------------------------------------------------------
    // Allergy — CreateAllergyAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateAllergyAsync_WithValidDto_ReturnsAllergyDtoWithCorrectFields()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Peanuts",
            Severity: AllergySeverity.Severe,
            AllergyType: AllergyType.Food);

        // Act
        var result = await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0, because: "the database should assign a positive identity value");
        result.ClientId.Should().Be(_seededClientId);
        result.Name.Should().Be("Peanuts");
        result.Severity.Should().Be(AllergySeverity.Severe);
        result.AllergyType.Should().Be(AllergyType.Food);
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAllergyAsync_WithValidDto_PersistsAllergyToDatabase()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Tree Nuts",
            Severity: AllergySeverity.Moderate,
            AllergyType: AllergyType.Food);

        // Act
        var result = await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        var persisted = await _dbContext.ClientAllergies.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.Id == result.Id);
        persisted.Should().NotBeNull();
        persisted!.Name.Should().Be("Tree Nuts");
        persisted.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithValidDto_CallsAuditLog()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Shellfish",
            Severity: AllergySeverity.Severe,
            AllergyType: AllergyType.Food);

        // Act
        var result = await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AllergyCreated",
            "ClientAllergy",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Shellfish")));
    }

    [Fact]
    public async Task CreateAllergyAsync_WithValidDto_DispatchesCreatedNotification()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Milk",
            Severity: AllergySeverity.Mild,
            AllergyType: AllergyType.Food);

        // Act
        var result = await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientAllergy" &&
            n.ChangeType == EntityChangeType.Created &&
            n.EntityId == result.Id &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task CreateAllergyAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Wheat",
            Severity: AllergySeverity.Moderate,
            AllergyType: AllergyType.Food);

        // Act
        await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithFoodAllergyType_CallsAllergenServiceWithFoodCategory()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Eggs",
            Severity: AllergySeverity.Mild,
            AllergyType: AllergyType.Food);

        // Act
        await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _allergenService.Received(1).GetOrCreateAsync("Eggs", "Food", UserId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithDrugAllergyType_CallsAllergenServiceWithDrugCategory()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Penicillin",
            Severity: AllergySeverity.Severe,
            AllergyType: AllergyType.Drug);

        // Act
        await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _allergenService.Received(1).GetOrCreateAsync("Penicillin", "Drug", UserId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithEnvironmentalAllergyType_CallsAllergenServiceWithEnvironmentalCategory()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Pollen",
            Severity: AllergySeverity.Mild,
            AllergyType: AllergyType.Environmental);

        // Act
        await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _allergenService.Received(1).GetOrCreateAsync("Pollen", "Environmental", UserId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithOtherAllergyType_CallsAllergenServiceWithNullCategory()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: _seededClientId,
            Name: "Latex",
            Severity: AllergySeverity.Moderate,
            AllergyType: AllergyType.Other);

        // Act
        await _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await _allergenService.Received(1).GetOrCreateAsync("Latex", null, UserId);
    }

    [Fact]
    public async Task CreateAllergyAsync_WithNonExistentClientId_ThrowsInvalidOperationException()
    {
        // Arrange
        const int nonExistentClientId = 999_001;
        var dto = new CreateClientAllergyDto(
            ClientId: nonExistentClientId,
            Name: "Soy",
            Severity: AllergySeverity.Mild,
            AllergyType: AllergyType.Food);

        // Act
        var act = () => _sut.CreateAllergyAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{nonExistentClientId}*");
    }

    [Fact]
    public async Task CreateAllergyAsync_WithNonExistentClientId_DoesNotCallAuditLog()
    {
        // Arrange
        var dto = new CreateClientAllergyDto(
            ClientId: 999_002,
            Name: "Soy",
            Severity: AllergySeverity.Mild,
            AllergyType: AllergyType.Food);

        // Act
        await FluentActions.Invoking(() => _sut.CreateAllergyAsync(dto, UserId))
            .Should().ThrowAsync<InvalidOperationException>();

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Allergy — GetAllergyByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllergyByIdAsync_WithExistingId_ReturnsAllergyDto()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Gluten",
            Severity = AllergySeverity.Moderate,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetAllergyByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.Name.Should().Be("Gluten");
        result.Severity.Should().Be(AllergySeverity.Moderate);
        result.AllergyType.Should().Be(AllergyType.Food);
    }

    [Fact]
    public async Task GetAllergyByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const int nonExistentId = 999_003;

        // Act
        var result = await _sut.GetAllergyByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Allergy — GetAllergiesByClientIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetAllergiesByClientIdAsync_WithMultipleAllergies_ReturnsListSortedByName()
    {
        // Arrange
        _dbContext.ClientAllergies.AddRange(
            new ClientAllergy { ClientId = _seededClientId, Name = "Shellfish", Severity = AllergySeverity.Severe, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow },
            new ClientAllergy { ClientId = _seededClientId, Name = "Almonds", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow },
            new ClientAllergy { ClientId = _seededClientId, Name = "Milk", Severity = AllergySeverity.Moderate, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetAllergiesByClientIdAsync(_seededClientId);

        // Assert
        var names = results.Select(r => r.Name).ToList();
        names.Should().HaveCount(3);
        names.Should().BeInAscendingOrder(because: "GetAllergiesByClientIdAsync orders results by Name ascending");
    }

    [Fact]
    public async Task GetAllergiesByClientIdAsync_WithNoAllergies_ReturnsEmptyList()
    {
        // Arrange — add a second client with no allergies
        var emptyClient = new Client
        {
            FirstName = "Empty",
            LastName = "HealthProfile",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(emptyClient);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetAllergiesByClientIdAsync(emptyClient.Id);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllergiesByClientIdAsync_ExcludesSoftDeletedAllergies()
    {
        // Arrange
        var active = new ClientAllergy { ClientId = _seededClientId, Name = "ActiveAllergy", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow };
        var deleted = new ClientAllergy { ClientId = _seededClientId, Name = "DeletedAllergy", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow, IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = UserId };
        _dbContext.ClientAllergies.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetAllergiesByClientIdAsync(_seededClientId);

        // Assert
        results.Should().NotContain(r => r.Name == "DeletedAllergy",
            because: "the global query filter must exclude soft-deleted allergies");
        results.Should().Contain(r => r.Name == "ActiveAllergy");
    }

    // ---------------------------------------------------------------------------
    // Allergy — UpdateAllergyAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateAllergyAsync_WithExistingId_ReturnsTrueAndPersistsChanges()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "Original Name",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientAllergyDto(
            Name: "Updated Name",
            Severity: AllergySeverity.Severe,
            AllergyType: AllergyType.Environmental);

        // Act
        var result = await _sut.UpdateAllergyAsync(entity.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientAllergies.IgnoreQueryFilters()
            .FirstAsync(a => a.Id == entity.Id);
        persisted.Name.Should().Be("Updated Name");
        persisted.Severity.Should().Be(AllergySeverity.Severe);
        persisted.AllergyType.Should().Be(AllergyType.Environmental);
        persisted.UpdatedAt.Should().NotBeNull();
        persisted.UpdatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task UpdateAllergyAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "OriginalAllergyName",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientAllergyDto("RenamedAllergyName", AllergySeverity.Moderate, AllergyType.Food);

        // Act
        await _sut.UpdateAllergyAsync(entity.Id, updateDto, UserId);

        // Assert — the service logs the new name after mutation
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AllergyUpdated",
            "ClientAllergy",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("RenamedAllergyName")));
    }

    [Fact]
    public async Task UpdateAllergyAsync_WithExistingId_DispatchesUpdatedNotification()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "DispatchUpdateAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientAllergyDto("DispatchUpdateAllergy", AllergySeverity.Moderate, AllergyType.Food);

        // Act
        await _sut.UpdateAllergyAsync(entity.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientAllergy" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task UpdateAllergyAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "RetentionUpdateAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientAllergyDto("RetentionUpdateAllergy", AllergySeverity.Mild, AllergyType.Food);

        // Act
        await _sut.UpdateAllergyAsync(entity.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateAllergyAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_004;
        var updateDto = new UpdateClientAllergyDto("Any Name", AllergySeverity.Mild, AllergyType.Food);

        // Act
        var result = await _sut.UpdateAllergyAsync(nonExistentId, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAllergyAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_005;
        var updateDto = new UpdateClientAllergyDto("Any Name", AllergySeverity.Mild, AllergyType.Food);

        // Act
        await _sut.UpdateAllergyAsync(nonExistentId, updateDto, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Allergy — DeleteAllergyAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAllergyAsync_WithExistingId_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "SoftDeleteAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteAllergyAsync(entity.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientAllergies.IgnoreQueryFilters()
            .FirstAsync(a => a.Id == entity.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteAllergyAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "AuditDeleteAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAllergyAsync(entity.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "AllergyDeleted",
            "ClientAllergy",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditDeleteAllergy")));
    }

    [Fact]
    public async Task DeleteAllergyAsync_WithExistingId_DispatchesDeletedNotification()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "DispatchDeleteAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAllergyAsync(entity.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientAllergy" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task DeleteAllergyAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "RetentionDeleteAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAllergyAsync(entity.Id, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task DeleteAllergyAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_006;

        // Act
        var result = await _sut.DeleteAllergyAsync(nonExistentId, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAllergyAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_007;

        // Act
        await _sut.DeleteAllergyAsync(nonExistentId, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task DeleteAllergyAsync_SoftDeletedAllergyIsExcludedFromSubsequentQuery()
    {
        // Arrange
        var entity = new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "ExcludeAfterDeleteAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientAllergies.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteAllergyAsync(entity.Id, UserId);
        var results = await _sut.GetAllergiesByClientIdAsync(_seededClientId);

        // Assert
        results.Should().NotContain(r => r.Id == entity.Id,
            because: "soft-deleted allergies must be excluded from list queries via the global query filter");
    }

    // ---------------------------------------------------------------------------
    // Medication — CreateMedicationAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateMedicationAsync_WithValidDto_ReturnsMedicationDtoWithCorrectFields()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: _seededClientId,
            Name: "Metformin",
            Dosage: "500mg",
            Frequency: "Twice daily",
            PrescribedFor: "Type 2 Diabetes");

        // Act
        var result = await _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(_seededClientId);
        result.Name.Should().Be("Metformin");
        result.Dosage.Should().Be("500mg");
        result.Frequency.Should().Be("Twice daily");
        result.PrescribedFor.Should().Be("Type 2 Diabetes");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateMedicationAsync_WithNullOptionalFields_ReturnsDtoWithNullFields()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: _seededClientId,
            Name: "Vitamin D",
            Dosage: null,
            Frequency: null,
            PrescribedFor: null);

        // Act
        var result = await _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        result.Dosage.Should().BeNull();
        result.Frequency.Should().BeNull();
        result.PrescribedFor.Should().BeNull();
    }

    [Fact]
    public async Task CreateMedicationAsync_WithValidDto_CallsAuditLog()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: _seededClientId,
            Name: "Lisinopril",
            Dosage: "10mg",
            Frequency: "Once daily",
            PrescribedFor: "Hypertension");

        // Act
        var result = await _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "MedicationCreated",
            "ClientMedication",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Lisinopril")));
    }

    [Fact]
    public async Task CreateMedicationAsync_WithValidDto_DispatchesCreatedNotification()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: _seededClientId,
            Name: "Atorvastatin",
            Dosage: null,
            Frequency: null,
            PrescribedFor: null);

        // Act
        var result = await _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientMedication" &&
            n.ChangeType == EntityChangeType.Created &&
            n.EntityId == result.Id &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task CreateMedicationAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: _seededClientId,
            Name: "Omeprazole",
            Dosage: null,
            Frequency: null,
            PrescribedFor: null);

        // Act
        await _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateMedicationAsync_WithNonExistentClientId_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateClientMedicationDto(
            ClientId: 999_008,
            Name: "Aspirin",
            Dosage: null,
            Frequency: null,
            PrescribedFor: null);

        // Act
        var act = () => _sut.CreateMedicationAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999008*");
    }

    // ---------------------------------------------------------------------------
    // Medication — GetMedicationByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetMedicationByIdAsync_WithExistingId_ReturnsMedicationDto()
    {
        // Arrange
        var entity = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "Metoprolol",
            Dosage = "25mg",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetMedicationByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.Name.Should().Be("Metoprolol");
        result.Dosage.Should().Be("25mg");
    }

    [Fact]
    public async Task GetMedicationByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const int nonExistentId = 999_009;

        // Act
        var result = await _sut.GetMedicationByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Medication — GetMedicationsByClientIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetMedicationsByClientIdAsync_WithMultipleMedications_ReturnsListSortedByName()
    {
        // Arrange
        _dbContext.ClientMedications.AddRange(
            new ClientMedication { ClientId = _seededClientId, Name = "Zoloft", CreatedAt = DateTime.UtcNow },
            new ClientMedication { ClientId = _seededClientId, Name = "Aspirin", CreatedAt = DateTime.UtcNow },
            new ClientMedication { ClientId = _seededClientId, Name = "Metformin", CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetMedicationsByClientIdAsync(_seededClientId);

        // Assert
        var names = results.Select(r => r.Name).ToList();
        names.Should().HaveCount(3);
        names.Should().BeInAscendingOrder(because: "GetMedicationsByClientIdAsync orders results by Name ascending");
    }

    [Fact]
    public async Task GetMedicationsByClientIdAsync_WithNoMedications_ReturnsEmptyList()
    {
        // Arrange
        var emptyClient = new Client
        {
            FirstName = "EmptyMeds",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(emptyClient);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetMedicationsByClientIdAsync(emptyClient.Id);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetMedicationsByClientIdAsync_ExcludesSoftDeletedMedications()
    {
        // Arrange
        var active = new ClientMedication { ClientId = _seededClientId, Name = "ActiveMed", CreatedAt = DateTime.UtcNow };
        var deleted = new ClientMedication { ClientId = _seededClientId, Name = "DeletedMed", CreatedAt = DateTime.UtcNow, IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = UserId };
        _dbContext.ClientMedications.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetMedicationsByClientIdAsync(_seededClientId);

        // Assert
        results.Should().NotContain(r => r.Name == "DeletedMed",
            because: "the global query filter must exclude soft-deleted medications");
        results.Should().Contain(r => r.Name == "ActiveMed");
    }

    // ---------------------------------------------------------------------------
    // Medication — UpdateMedicationAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateMedicationAsync_WithExistingId_ReturnsTrueAndPersistsChanges()
    {
        // Arrange
        var entity = new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "OriginalMed",
            Dosage = "10mg",
            Frequency = "Once daily",
            PrescribedFor = "Original condition",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientMedicationDto(
            Name: "UpdatedMed",
            Dosage: "20mg",
            Frequency: "Twice daily",
            PrescribedFor: "Updated condition");

        // Act
        var result = await _sut.UpdateMedicationAsync(entity.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientMedications.IgnoreQueryFilters()
            .FirstAsync(m => m.Id == entity.Id);
        persisted.Name.Should().Be("UpdatedMed");
        persisted.Dosage.Should().Be("20mg");
        persisted.Frequency.Should().Be("Twice daily");
        persisted.PrescribedFor.Should().Be("Updated condition");
        persisted.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateMedicationAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "AuditMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientMedicationDto("AuditMedUpdated", null, null, null);

        // Act
        await _sut.UpdateMedicationAsync(entity.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "MedicationUpdated",
            "ClientMedication",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditMedUpdated")));
    }

    [Fact]
    public async Task UpdateMedicationAsync_WithExistingId_DispatchesUpdatedNotification()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "DispatchMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientMedicationDto("DispatchMed", null, null, null);

        // Act
        await _sut.UpdateMedicationAsync(entity.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientMedication" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task UpdateMedicationAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "RetentionMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientMedicationDto("RetentionMed", null, null, null);

        // Act
        await _sut.UpdateMedicationAsync(entity.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateMedicationAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_010;
        var updateDto = new UpdateClientMedicationDto("Any Name", null, null, null);

        // Act
        var result = await _sut.UpdateMedicationAsync(nonExistentId, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateMedicationAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_011;
        var updateDto = new UpdateClientMedicationDto("Any Name", null, null, null);

        // Act
        await _sut.UpdateMedicationAsync(nonExistentId, updateDto, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Medication — DeleteMedicationAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteMedicationAsync_WithExistingId_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "SoftDeleteMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteMedicationAsync(entity.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientMedications.IgnoreQueryFilters()
            .FirstAsync(m => m.Id == entity.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteMedicationAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "AuditDeleteMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteMedicationAsync(entity.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "MedicationDeleted",
            "ClientMedication",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditDeleteMed")));
    }

    [Fact]
    public async Task DeleteMedicationAsync_WithExistingId_DispatchesDeletedNotification()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "DispatchDeleteMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteMedicationAsync(entity.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientMedication" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task DeleteMedicationAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientMedication { ClientId = _seededClientId, Name = "RetentionDeleteMed", CreatedAt = DateTime.UtcNow };
        _dbContext.ClientMedications.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteMedicationAsync(entity.Id, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task DeleteMedicationAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_012;

        // Act
        var result = await _sut.DeleteMedicationAsync(nonExistentId, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteMedicationAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_013;

        // Act
        await _sut.DeleteMedicationAsync(nonExistentId, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Condition — CreateConditionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateConditionAsync_WithValidDto_ReturnsConditionDtoWithCorrectFields()
    {
        // Arrange
        var diagnosisDate = new DateOnly(2023, 6, 15);
        var dto = new CreateClientConditionDto(
            ClientId: _seededClientId,
            Name: "Type 2 Diabetes",
            Code: "E11",
            DiagnosisDate: diagnosisDate,
            Status: ConditionStatus.Active,
            Notes: "Initial diagnosis notes");

        // Act
        var result = await _sut.CreateConditionAsync(dto, UserId);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(_seededClientId);
        result.Name.Should().Be("Type 2 Diabetes");
        result.Code.Should().Be("E11");
        result.DiagnosisDate.Should().Be(diagnosisDate);
        result.Status.Should().Be(ConditionStatus.Active);
        result.Notes.Should().Be("Initial diagnosis notes");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateConditionAsync_WithNullOptionalFields_ReturnsDtoWithNullFields()
    {
        // Arrange
        var dto = new CreateClientConditionDto(
            ClientId: _seededClientId,
            Name: "Hypertension",
            Code: null,
            DiagnosisDate: null,
            Status: ConditionStatus.Managed,
            Notes: null);

        // Act
        var result = await _sut.CreateConditionAsync(dto, UserId);

        // Assert
        result.Code.Should().BeNull();
        result.DiagnosisDate.Should().BeNull();
        result.Notes.Should().BeNull();
    }

    [Fact]
    public async Task CreateConditionAsync_WithValidDto_CallsAuditLog()
    {
        // Arrange
        var dto = new CreateClientConditionDto(
            ClientId: _seededClientId,
            Name: "AuditCondition",
            Code: null,
            DiagnosisDate: null,
            Status: ConditionStatus.Active,
            Notes: null);

        // Act
        var result = await _sut.CreateConditionAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConditionCreated",
            "ClientCondition",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditCondition")));
    }

    [Fact]
    public async Task CreateConditionAsync_WithValidDto_DispatchesCreatedNotification()
    {
        // Arrange
        var dto = new CreateClientConditionDto(
            ClientId: _seededClientId,
            Name: "DispatchCondition",
            Code: null,
            DiagnosisDate: null,
            Status: ConditionStatus.Active,
            Notes: null);

        // Act
        var result = await _sut.CreateConditionAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientCondition" &&
            n.ChangeType == EntityChangeType.Created &&
            n.EntityId == result.Id &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task CreateConditionAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = new CreateClientConditionDto(
            ClientId: _seededClientId,
            Name: "RetentionCondition",
            Code: null,
            DiagnosisDate: null,
            Status: ConditionStatus.Active,
            Notes: null);

        // Act
        await _sut.CreateConditionAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateConditionAsync_WithNonExistentClientId_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateClientConditionDto(
            ClientId: 999_014,
            Name: "Asthma",
            Code: null,
            DiagnosisDate: null,
            Status: ConditionStatus.Active,
            Notes: null);

        // Act
        var act = () => _sut.CreateConditionAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999014*");
    }

    // ---------------------------------------------------------------------------
    // Condition — GetConditionByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetConditionByIdAsync_WithExistingId_ReturnsConditionDto()
    {
        // Arrange
        var entity = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "Celiac Disease",
            Status = ConditionStatus.Managed,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetConditionByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.Name.Should().Be("Celiac Disease");
        result.Status.Should().Be(ConditionStatus.Managed);
    }

    [Fact]
    public async Task GetConditionByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const int nonExistentId = 999_015;

        // Act
        var result = await _sut.GetConditionByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Condition — GetConditionsByClientIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetConditionsByClientIdAsync_WithMultipleConditions_ReturnsListSortedByName()
    {
        // Arrange
        _dbContext.ClientConditions.AddRange(
            new ClientCondition { ClientId = _seededClientId, Name = "Psoriasis", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow },
            new ClientCondition { ClientId = _seededClientId, Name = "Arthritis", Status = ConditionStatus.Managed, CreatedAt = DateTime.UtcNow },
            new ClientCondition { ClientId = _seededClientId, Name = "Fibromyalgia", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetConditionsByClientIdAsync(_seededClientId);

        // Assert
        var names = results.Select(r => r.Name).ToList();
        names.Should().HaveCount(3);
        names.Should().BeInAscendingOrder(because: "GetConditionsByClientIdAsync orders results by Name ascending");
    }

    [Fact]
    public async Task GetConditionsByClientIdAsync_WithNoConditions_ReturnsEmptyList()
    {
        // Arrange
        var emptyClient = new Client
        {
            FirstName = "EmptyConditions",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(emptyClient);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetConditionsByClientIdAsync(emptyClient.Id);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetConditionsByClientIdAsync_ExcludesSoftDeletedConditions()
    {
        // Arrange
        var active = new ClientCondition { ClientId = _seededClientId, Name = "ActiveCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        var deleted = new ClientCondition { ClientId = _seededClientId, Name = "DeletedCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow, IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = UserId };
        _dbContext.ClientConditions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetConditionsByClientIdAsync(_seededClientId);

        // Assert
        results.Should().NotContain(r => r.Name == "DeletedCondition",
            because: "the global query filter must exclude soft-deleted conditions");
        results.Should().Contain(r => r.Name == "ActiveCondition");
    }

    // ---------------------------------------------------------------------------
    // Condition — UpdateConditionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateConditionAsync_WithExistingId_ReturnsTrueAndPersistsChanges()
    {
        // Arrange
        var entity = new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "OriginalCondition",
            Code = "A00",
            Status = ConditionStatus.Active,
            Notes = "Original notes",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var diagnosisDate = new DateOnly(2022, 3, 10);
        var updateDto = new UpdateClientConditionDto(
            Name: "UpdatedCondition",
            Code: "B01",
            DiagnosisDate: diagnosisDate,
            Status: ConditionStatus.Resolved,
            Notes: "Updated notes");

        // Act
        var result = await _sut.UpdateConditionAsync(entity.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientConditions.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == entity.Id);
        persisted.Name.Should().Be("UpdatedCondition");
        persisted.Code.Should().Be("B01");
        persisted.DiagnosisDate.Should().Be(diagnosisDate);
        persisted.Status.Should().Be(ConditionStatus.Resolved);
        persisted.Notes.Should().Be("Updated notes");
        persisted.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateConditionAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "AuditConditionUpdate", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientConditionDto("AuditConditionUpdated", null, null, ConditionStatus.Managed, null);

        // Act
        await _sut.UpdateConditionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConditionUpdated",
            "ClientCondition",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditConditionUpdated")));
    }

    [Fact]
    public async Task UpdateConditionAsync_WithExistingId_DispatchesUpdatedNotification()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "DispatchConditionUpdate", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientConditionDto("DispatchConditionUpdate", null, null, ConditionStatus.Active, null);

        // Act
        await _sut.UpdateConditionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientCondition" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task UpdateConditionAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "RetentionConditionUpdate", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientConditionDto("RetentionConditionUpdate", null, null, ConditionStatus.Active, null);

        // Act
        await _sut.UpdateConditionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateConditionAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_016;
        var updateDto = new UpdateClientConditionDto("Any Name", null, null, ConditionStatus.Active, null);

        // Act
        var result = await _sut.UpdateConditionAsync(nonExistentId, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateConditionAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_017;
        var updateDto = new UpdateClientConditionDto("Any Name", null, null, ConditionStatus.Active, null);

        // Act
        await _sut.UpdateConditionAsync(nonExistentId, updateDto, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Condition — DeleteConditionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteConditionAsync_WithExistingId_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "SoftDeleteCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteConditionAsync(entity.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientConditions.IgnoreQueryFilters()
            .FirstAsync(c => c.Id == entity.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteConditionAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "AuditDeleteCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteConditionAsync(entity.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "ConditionDeleted",
            "ClientCondition",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("AuditDeleteCondition")));
    }

    [Fact]
    public async Task DeleteConditionAsync_WithExistingId_DispatchesDeletedNotification()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "DispatchDeleteCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteConditionAsync(entity.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientCondition" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task DeleteConditionAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientCondition { ClientId = _seededClientId, Name = "RetentionDeleteCondition", Status = ConditionStatus.Active, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientConditions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteConditionAsync(entity.Id, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task DeleteConditionAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_018;

        // Act
        var result = await _sut.DeleteConditionAsync(nonExistentId, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteConditionAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_019;

        // Act
        await _sut.DeleteConditionAsync(nonExistentId, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Dietary Restriction — CreateDietaryRestrictionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithValidDto_ReturnsDietaryRestrictionDtoWithCorrectFields()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: _seededClientId,
            RestrictionType: DietaryRestrictionType.Vegan,
            Notes: "Strictly vegan");

        // Act
        var result = await _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().BeGreaterThan(0);
        result.ClientId.Should().Be(_seededClientId);
        result.RestrictionType.Should().Be(DietaryRestrictionType.Vegan);
        result.Notes.Should().Be("Strictly vegan");
        result.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        result.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithNullNotes_ReturnsDtoWithNullNotes()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: _seededClientId,
            RestrictionType: DietaryRestrictionType.GlutenFree,
            Notes: null);

        // Act
        var result = await _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        result.Notes.Should().BeNull();
    }

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithValidDto_CallsAuditLog()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: _seededClientId,
            RestrictionType: DietaryRestrictionType.Kosher,
            Notes: null);

        // Act
        var result = await _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "DietaryRestrictionCreated",
            "ClientDietaryRestriction",
            result.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Kosher")));
    }

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithValidDto_DispatchesCreatedNotification()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: _seededClientId,
            RestrictionType: DietaryRestrictionType.Halal,
            Notes: null);

        // Act
        var result = await _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientDietaryRestriction" &&
            n.ChangeType == EntityChangeType.Created &&
            n.EntityId == result.Id &&
            n.ClientId == _seededClientId));
    }

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithValidDto_CallsRetentionTracker()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: _seededClientId,
            RestrictionType: DietaryRestrictionType.Ketogenic,
            Notes: null);

        // Act
        await _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task CreateDietaryRestrictionAsync_WithNonExistentClientId_ThrowsInvalidOperationException()
    {
        // Arrange
        var dto = new CreateClientDietaryRestrictionDto(
            ClientId: 999_020,
            RestrictionType: DietaryRestrictionType.Vegan,
            Notes: null);

        // Act
        var act = () => _sut.CreateDietaryRestrictionAsync(dto, UserId);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*999020*");
    }

    // ---------------------------------------------------------------------------
    // Dietary Restriction — GetDietaryRestrictionByIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDietaryRestrictionByIdAsync_WithExistingId_ReturnsDietaryRestrictionDto()
    {
        // Arrange
        var entity = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.NutFree,
            Notes = "Strict nut allergy",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetDietaryRestrictionByIdAsync(entity.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(entity.Id);
        result.RestrictionType.Should().Be(DietaryRestrictionType.NutFree);
        result.Notes.Should().Be("Strict nut allergy");
    }

    [Fact]
    public async Task GetDietaryRestrictionByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Arrange
        const int nonExistentId = 999_021;

        // Act
        var result = await _sut.GetDietaryRestrictionByIdAsync(nonExistentId);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Dietary Restriction — GetDietaryRestrictionsByClientIdAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetDietaryRestrictionsByClientIdAsync_WithMultipleRestrictions_ReturnsListSortedByRestrictionType()
    {
        // Arrange — RestrictionType is stored as a string in the database, so ordering is alphabetical.
        // Seed values in reverse alphabetical order to verify the service sorts them correctly.
        // Alphabetical ascending: DairyFree < GlutenFree < Vegan
        _dbContext.ClientDietaryRestrictions.AddRange(
            new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Vegan, CreatedAt = DateTime.UtcNow },
            new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.GlutenFree, CreatedAt = DateTime.UtcNow },
            new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.DairyFree, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetDietaryRestrictionsByClientIdAsync(_seededClientId);

        // Assert
        results.Should().HaveCount(3);
        // RestrictionType is stored as string, so the sort is alphabetical by name
        var restrictionTypeNames = results.Select(r => r.RestrictionType.ToString()).ToList();
        restrictionTypeNames.Should().BeInAscendingOrder(because: "GetDietaryRestrictionsByClientIdAsync orders results by RestrictionType name ascending");
    }

    [Fact]
    public async Task GetDietaryRestrictionsByClientIdAsync_WithNoRestrictions_ReturnsEmptyList()
    {
        // Arrange
        var emptyClient = new Client
        {
            FirstName = "EmptyRestrictions",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(emptyClient);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetDietaryRestrictionsByClientIdAsync(emptyClient.Id);

        // Assert
        results.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDietaryRestrictionsByClientIdAsync_ExcludesSoftDeletedRestrictions()
    {
        // Arrange
        var active = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Vegan, CreatedAt = DateTime.UtcNow };
        var deleted = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Kosher, CreatedAt = DateTime.UtcNow, IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = UserId };
        _dbContext.ClientDietaryRestrictions.AddRange(active, deleted);
        await _dbContext.SaveChangesAsync();

        // Act
        var results = await _sut.GetDietaryRestrictionsByClientIdAsync(_seededClientId);

        // Assert
        results.Should().NotContain(r => r.RestrictionType == DietaryRestrictionType.Kosher,
            because: "the global query filter must exclude soft-deleted dietary restrictions");
        results.Should().Contain(r => r.RestrictionType == DietaryRestrictionType.Vegan);
    }

    // ---------------------------------------------------------------------------
    // Dietary Restriction — UpdateDietaryRestrictionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithExistingId_ReturnsTrueAndPersistsChanges()
    {
        // Arrange
        var entity = new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.Vegetarian,
            Notes = "Original notes",
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientDietaryRestrictionDto(
            RestrictionType: DietaryRestrictionType.Vegan,
            Notes: "Updated notes");

        // Act
        var result = await _sut.UpdateDietaryRestrictionAsync(entity.Id, updateDto, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientDietaryRestrictions.IgnoreQueryFilters()
            .FirstAsync(d => d.Id == entity.Id);
        persisted.RestrictionType.Should().Be(DietaryRestrictionType.Vegan);
        persisted.Notes.Should().Be("Updated notes");
        persisted.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.LowSodium, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientDietaryRestrictionDto(DietaryRestrictionType.Ketogenic, null);

        // Act
        await _sut.UpdateDietaryRestrictionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "DietaryRestrictionUpdated",
            "ClientDietaryRestriction",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Ketogenic")));
    }

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithExistingId_DispatchesUpdatedNotification()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Other, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientDietaryRestrictionDto(DietaryRestrictionType.Other, null);

        // Act
        await _sut.UpdateDietaryRestrictionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientDietaryRestriction" &&
            n.ChangeType == EntityChangeType.Updated &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Halal, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        var updateDto = new UpdateClientDietaryRestrictionDto(DietaryRestrictionType.Halal, null);

        // Act
        await _sut.UpdateDietaryRestrictionAsync(entity.Id, updateDto, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_022;
        var updateDto = new UpdateClientDietaryRestrictionDto(DietaryRestrictionType.Vegan, null);

        // Act
        var result = await _sut.UpdateDietaryRestrictionAsync(nonExistentId, updateDto, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateDietaryRestrictionAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_023;
        var updateDto = new UpdateClientDietaryRestrictionDto(DietaryRestrictionType.Vegan, null);

        // Act
        await _sut.UpdateDietaryRestrictionAsync(nonExistentId, updateDto, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Dietary Restriction — DeleteDietaryRestrictionAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithExistingId_ReturnsTrueAndSoftDeletes()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.NutFree, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.DeleteDietaryRestrictionAsync(entity.Id, UserId);

        // Assert
        result.Should().BeTrue();
        var persisted = await _dbContext.ClientDietaryRestrictions.IgnoreQueryFilters()
            .FirstAsync(d => d.Id == entity.Id);
        persisted.IsDeleted.Should().BeTrue();
        persisted.DeletedAt.Should().NotBeNull();
        persisted.DeletedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        persisted.DeletedBy.Should().Be(UserId);
    }

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithExistingId_CallsAuditLog()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Kosher, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteDietaryRestrictionAsync(entity.Id, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "DietaryRestrictionDeleted",
            "ClientDietaryRestriction",
            entity.Id.ToString(),
            Arg.Is<string>(s => s.Contains("Kosher")));
    }

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithExistingId_DispatchesDeletedNotification()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Vegan, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteDietaryRestrictionAsync(entity.Id, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "ClientDietaryRestriction" &&
            n.ChangeType == EntityChangeType.Deleted &&
            n.EntityId == entity.Id));
    }

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithExistingId_CallsRetentionTracker()
    {
        // Arrange
        var entity = new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.GlutenFree, CreatedAt = DateTime.UtcNow };
        _dbContext.ClientDietaryRestrictions.Add(entity);
        await _dbContext.SaveChangesAsync();

        // Act
        await _sut.DeleteDietaryRestrictionAsync(entity.Id, UserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithNonExistentId_ReturnsFalse()
    {
        // Arrange
        const int nonExistentId = 999_024;

        // Act
        var result = await _sut.DeleteDietaryRestrictionAsync(nonExistentId, UserId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDietaryRestrictionAsync_WithNonExistentId_DoesNotCallAuditLog()
    {
        // Arrange
        const int nonExistentId = 999_025;

        // Act
        await _sut.DeleteDietaryRestrictionAsync(nonExistentId, UserId);

        // Assert
        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>());
    }

    // ---------------------------------------------------------------------------
    // Summary — GetHealthProfileSummaryAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetHealthProfileSummaryAsync_WithFullHealthProfile_ReturnsAllFourListsPopulated()
    {
        // Arrange
        _dbContext.ClientAllergies.Add(new ClientAllergy
        {
            ClientId = _seededClientId,
            Name = "SummaryAllergy",
            Severity = AllergySeverity.Mild,
            AllergyType = AllergyType.Food,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientMedications.Add(new ClientMedication
        {
            ClientId = _seededClientId,
            Name = "SummaryMedication",
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientConditions.Add(new ClientCondition
        {
            ClientId = _seededClientId,
            Name = "SummaryCondition",
            Status = ConditionStatus.Active,
            CreatedAt = DateTime.UtcNow
        });
        _dbContext.ClientDietaryRestrictions.Add(new ClientDietaryRestriction
        {
            ClientId = _seededClientId,
            RestrictionType = DietaryRestrictionType.Vegan,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetHealthProfileSummaryAsync(_seededClientId);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be(_seededClientId);
        result.Allergies.Should().ContainSingle(a => a.Name == "SummaryAllergy");
        result.Medications.Should().ContainSingle(m => m.Name == "SummaryMedication");
        result.Conditions.Should().ContainSingle(c => c.Name == "SummaryCondition");
        result.DietaryRestrictions.Should().ContainSingle(d => d.RestrictionType == DietaryRestrictionType.Vegan);
    }

    [Fact]
    public async Task GetHealthProfileSummaryAsync_WithNoHealthData_ReturnsAllEmptyLists()
    {
        // Arrange — add a fresh client with no health profile data
        var emptyClient = new Client
        {
            FirstName = "EmptySummary",
            LastName = "Client",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Clients.Add(emptyClient);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetHealthProfileSummaryAsync(emptyClient.Id);

        // Assert
        result.Should().NotBeNull();
        result.ClientId.Should().Be(emptyClient.Id);
        result.Allergies.Should().BeEmpty();
        result.Medications.Should().BeEmpty();
        result.Conditions.Should().BeEmpty();
        result.DietaryRestrictions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHealthProfileSummaryAsync_ExcludesSoftDeletedEntries()
    {
        // Arrange — one active and one soft-deleted allergy
        _dbContext.ClientAllergies.AddRange(
            new ClientAllergy { ClientId = _seededClientId, Name = "ActiveSummaryAllergy", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow },
            new ClientAllergy { ClientId = _seededClientId, Name = "DeletedSummaryAllergy", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow, IsDeleted = true, DeletedAt = DateTime.UtcNow, DeletedBy = UserId });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetHealthProfileSummaryAsync(_seededClientId);

        // Assert
        result.Allergies.Should().Contain(a => a.Name == "ActiveSummaryAllergy");
        result.Allergies.Should().NotContain(a => a.Name == "DeletedSummaryAllergy",
            because: "the global query filter must exclude soft-deleted allergies from the summary");
    }

    [Fact]
    public async Task GetHealthProfileSummaryAsync_AllergiesAreSortedByName()
    {
        // Arrange
        _dbContext.ClientAllergies.AddRange(
            new ClientAllergy { ClientId = _seededClientId, Name = "Zucchini", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow },
            new ClientAllergy { ClientId = _seededClientId, Name = "Apricot", Severity = AllergySeverity.Mild, AllergyType = AllergyType.Food, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetHealthProfileSummaryAsync(_seededClientId);

        // Assert
        var names = result.Allergies.Select(a => a.Name).ToList();
        names.Should().HaveCount(2);
        names.Should().BeInAscendingOrder(because: "summary allergies are ordered by Name");
    }

    [Fact]
    public async Task GetHealthProfileSummaryAsync_DietaryRestrictionsAreSortedByRestrictionType()
    {
        // Arrange — RestrictionType is stored as a string in the database (HasConversion<string>()),
        // so ordering is alphabetical. Seed in reverse alphabetical order to verify the service sorts correctly.
        // Alphabetical ascending: Vegan < Vegetarian
        _dbContext.ClientDietaryRestrictions.AddRange(
            new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Vegetarian, CreatedAt = DateTime.UtcNow },
            new ClientDietaryRestriction { ClientId = _seededClientId, RestrictionType = DietaryRestrictionType.Vegan, CreatedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetHealthProfileSummaryAsync(_seededClientId);

        // Assert
        var typeNames = result.DietaryRestrictions.Select(d => d.RestrictionType.ToString()).ToList();
        typeNames.Should().HaveCount(2);
        typeNames.Should().BeInAscendingOrder(because: "summary dietary restrictions are ordered by RestrictionType name ascending");
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
