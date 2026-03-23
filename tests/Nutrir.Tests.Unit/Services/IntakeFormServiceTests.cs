using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Data;
using Nutrir.Infrastructure.Services;
using Nutrir.Tests.Unit.Helpers;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class IntakeFormServiceTests : IDisposable
{
    private readonly AppDbContext _dbContext;
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection;
    private readonly SharedConnectionContextFactory _dbContextFactory;

    private readonly IAuditLogService _auditLogService;
    private readonly IConsentService _consentService;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IRetentionTracker _retentionTracker;

    private readonly IntakeFormService _sut;

    private const string NutritionistId = "nutritionist-intakeform-test-001";
    private const string UserId = "acting-user-intakeform-001";
    private const string ReviewerUserId = "reviewer-intakeform-001";
    private const string TestEmail = "patient@example.com";

    // Captured after SeedData() so tests can reference without hard-coding
    private int _seededClientId;
    private int _seededAppointmentId;

    public IntakeFormServiceTests()
    {
        (_dbContext, _connection) = TestDbContextFactory.Create();
        _dbContextFactory = new SharedConnectionContextFactory(_connection);

        _auditLogService = Substitute.For<IAuditLogService>();
        _consentService = Substitute.For<IConsentService>();
        _notificationDispatcher = Substitute.For<INotificationDispatcher>();
        _retentionTracker = Substitute.For<IRetentionTracker>();

        var options = Options.Create(new IntakeFormOptions
        {
            ExpiryDays = 7,
            ConsentPolicyVersion = "1.0"
        });

        _sut = new IntakeFormService(
            _dbContextFactory,
            _auditLogService,
            _consentService,
            _notificationDispatcher,
            _retentionTracker,
            options,
            NullLogger<IntakeFormService>.Instance);

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
            UserName = "nutritionist@intakeformtest.com",
            NormalizedUserName = "NUTRITIONIST@INTAKEFORMTEST.COM",
            Email = "nutritionist@intakeformtest.com",
            NormalizedEmail = "NUTRITIONIST@INTAKEFORMTEST.COM",
            FirstName = "Jane",
            LastName = "Smith",
            DisplayName = "Jane Smith",
            CreatedDate = DateTime.UtcNow
        };

        // Seed the acting user so CreatedByUserId FK constraints are satisfied
        var actingUser = new ApplicationUser
        {
            Id = UserId,
            UserName = "actinguser@intakeformtest.com",
            NormalizedUserName = "ACTINGUSER@INTAKEFORMTEST.COM",
            Email = "actinguser@intakeformtest.com",
            NormalizedEmail = "ACTINGUSER@INTAKEFORMTEST.COM",
            FirstName = "Acting",
            LastName = "User",
            DisplayName = "Acting User",
            CreatedDate = DateTime.UtcNow
        };

        var reviewerUser = new ApplicationUser
        {
            Id = ReviewerUserId,
            UserName = "reviewer@intakeformtest.com",
            NormalizedUserName = "REVIEWER@INTAKEFORMTEST.COM",
            Email = "reviewer@intakeformtest.com",
            NormalizedEmail = "REVIEWER@INTAKEFORMTEST.COM",
            FirstName = "Reviewer",
            LastName = "User",
            DisplayName = "Reviewer User",
            CreatedDate = DateTime.UtcNow
        };

        var client = new Client
        {
            FirstName = "Alice",
            LastName = "Intake",
            Email = "alice.intake@example.com",
            PrimaryNutritionistId = NutritionistId,
            ConsentGiven = true,
            ConsentTimestamp = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.Users.AddRange(nutritionist, actingUser, reviewerUser);
        _dbContext.Clients.Add(client);
        _dbContext.SaveChanges();

        _seededClientId = client.Id;

        // Seed an Appointment so tests that set AppointmentId satisfy the FK constraint
        var appointment = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(1),
            DurationMinutes = 60,
            Location = AppointmentLocation.Virtual,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment);
        _dbContext.SaveChanges();

        _seededAppointmentId = appointment.Id;
    }

    /// <summary>
    /// Seeds a pending intake form into the database and returns its entity.
    /// The ExpiresAt is in the future by default so it is valid for submission.
    /// </summary>
    private IntakeForm SeedForm(
        string? token = null,
        IntakeFormStatus status = IntakeFormStatus.Pending,
        int? clientId = null,
        int? appointmentId = null,
        DateTime? expiresAt = null,
        string createdByUserId = UserId)
    {
        var form = new IntakeForm
        {
            Token = token ?? Guid.NewGuid().ToString("N"),
            Status = status,
            ClientEmail = TestEmail,
            ClientId = clientId,
            AppointmentId = appointmentId,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddDays(7),
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };

        _dbContext.IntakeForms.Add(form);
        _dbContext.SaveChanges();
        return form;
    }

    /// <summary>
    /// Seeds a submitted form with a full set of responses ready for ReviewFormAsync tests.
    /// </summary>
    private IntakeForm SeedSubmittedForm(
        int? clientId = null,
        List<IntakeFormResponse>? responses = null)
    {
        var form = SeedForm(status: IntakeFormStatus.Submitted, clientId: clientId);

        var defaultResponses = responses ?? BuildFullResponses(form.Id);
        _dbContext.Set<IntakeFormResponse>().AddRange(defaultResponses);
        _dbContext.SaveChanges();

        return form;
    }

    /// <summary>
    /// Builds a representative set of responses covering all mapping paths.
    /// </summary>
    private static List<IntakeFormResponse> BuildFullResponses(int formId) =>
    [
        new() { IntakeFormId = formId, SectionKey = "personal_info", FieldKey = "first_name",    Value = "Bob" },
        new() { IntakeFormId = formId, SectionKey = "personal_info", FieldKey = "last_name",     Value = "Reviewed" },
        new() { IntakeFormId = formId, SectionKey = "personal_info", FieldKey = "email",         Value = "bob.reviewed@example.com" },
        new() { IntakeFormId = formId, SectionKey = "personal_info", FieldKey = "phone",         Value = "555-0199" },
        new() { IntakeFormId = formId, SectionKey = "personal_info", FieldKey = "date_of_birth", Value = "1990-03-15" },
        new() { IntakeFormId = formId, SectionKey = "consent",       FieldKey = "consent_given",         Value = "true" },
        new() { IntakeFormId = formId, SectionKey = "consent",       FieldKey = "consent_policy_version", Value = "1.0" },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "allergies",
            Value = JsonSerializer.Serialize(new List<string> { "Peanuts", "Shellfish" }) },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "medications",
            Value = JsonSerializer.Serialize(new List<string> { "Metformin", "Lisinopril" }) },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "conditions",
            Value = JsonSerializer.Serialize(new List<string> { "Type 2 Diabetes", "Hypertension" }) },
        new() { IntakeFormId = formId, SectionKey = "dietary_habits", FieldKey = "dietary_restrictions",
            Value = JsonSerializer.Serialize(new List<string> { "Vegan", "Custom restriction" }) },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "surgeries",         Value = "Appendectomy 2010" },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "family_history",    Value = "Heart disease" },
        new() { IntakeFormId = formId, SectionKey = "medical_history", FieldKey = "additional_notes",  Value = "Takes vitamins" },
        new() { IntakeFormId = formId, SectionKey = "goals",           FieldKey = "specific_concerns", Value = "Lose weight" },
        new() { IntakeFormId = formId, SectionKey = "goals",           FieldKey = "timeline_expectations", Value = "3 months" }
    ];

    private static List<IntakeFormResponseDto> BuildSubmitResponseDtos() =>
    [
        new("personal_info", "first_name",    "Carol"),
        new("personal_info", "last_name",     "Submit"),
        new("personal_info", "email",         "carol.submit@example.com"),
        new("consent",       "consent_given", "true")
    ];

    // ---------------------------------------------------------------------------
    // CreateFormAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_ReturnsFormWithUniqueToken()
    {
        // Arrange / Act
        var result1 = await _sut.CreateFormAsync(TestEmail, null, null, UserId);
        var result2 = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        result1.Token.Should().NotBeNullOrEmpty();
        result2.Token.Should().NotBeNullOrEmpty();
        result1.Token.Should().NotBe(result2.Token, because: "each form must receive a distinct token");
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_ReturnsPendingStatus()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        result.Status.Should().Be(IntakeFormStatus.Pending);
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_SetsCorrectEmail()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        result.ClientEmail.Should().Be(TestEmail);
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_SetsExpiryDateBasedOnOptions()
    {
        // Arrange
        var before = DateTime.UtcNow.AddDays(7).AddSeconds(-2);

        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert — ExpiresAt should be ~7 days from now (ExpiryDays = 7)
        result.ExpiresAt.Should().BeAfter(before, because: "expiry must be at least 7 days from now");
        result.ExpiresAt.Should().BeBefore(DateTime.UtcNow.AddDays(8), because: "expiry must not exceed 8 days from now");
    }

    [Fact]
    public async Task CreateFormAsync_WithClientId_LinksFormToClient()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, _seededClientId, UserId);

        // Assert
        result.ClientId.Should().Be(_seededClientId);
    }

    [Fact]
    public async Task CreateFormAsync_WithoutClientId_LeavesClientIdNull()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        result.ClientId.Should().BeNull();
    }

    [Fact]
    public async Task CreateFormAsync_WithAppointmentId_LinksFormToAppointment()
    {
        // Arrange — use the seeded appointment to satisfy the FK constraint
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, _seededAppointmentId, null, UserId);

        // Assert
        result.AppointmentId.Should().Be(_seededAppointmentId);
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_PersistsFormToDatabase()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        var persisted = await _dbContext.IntakeForms.FindAsync(result.Id);
        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(IntakeFormStatus.Pending);
        persisted.ClientEmail.Should().Be(TestEmail);
        persisted.CreatedByUserId.Should().Be(UserId);
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_CallsAuditLog()
    {
        // Act
        var result = await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            UserId,
            "IntakeFormCreated",
            "IntakeForm",
            result.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task CreateFormAsync_WithValidArgs_DispatchesCreatedNotification()
    {
        // Act
        await _sut.CreateFormAsync(TestEmail, null, null, UserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "IntakeForm" &&
            n.ChangeType == EntityChangeType.Created));
    }

    // ---------------------------------------------------------------------------
    // GetByTokenAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByTokenAsync_WithValidToken_ReturnsFormDto()
    {
        // Arrange
        var form = SeedForm(token: "valid-token-abc");

        // Act
        var result = await _sut.GetByTokenAsync("valid-token-abc");

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(form.Id);
        result.Token.Should().Be("valid-token-abc");
        result.Status.Should().Be(IntakeFormStatus.Pending);
    }

    [Fact]
    public async Task GetByTokenAsync_WithValidToken_IncludesResponses()
    {
        // Arrange
        var form = SeedForm(token: "token-with-responses");
        _dbContext.Set<IntakeFormResponse>().AddRange(
            new IntakeFormResponse { IntakeFormId = form.Id, SectionKey = "personal_info", FieldKey = "first_name", Value = "Test" },
            new IntakeFormResponse { IntakeFormId = form.Id, SectionKey = "personal_info", FieldKey = "last_name",  Value = "Person" }
        );
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByTokenAsync("token-with-responses");

        // Assert
        result.Should().NotBeNull();
        result!.Responses.Should().HaveCount(2);
        result.Responses.Should().Contain(r => r.SectionKey == "personal_info" && r.FieldKey == "first_name" && r.Value == "Test");
    }

    [Fact]
    public async Task GetByTokenAsync_WithNonExistentToken_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByTokenAsync("does-not-exist-token");

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetByIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByIdAsync_WithValidId_ReturnsFormDto()
    {
        // Arrange
        var form = SeedForm();

        // Act
        var result = await _sut.GetByIdAsync(form.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(form.Id);
        result.ClientEmail.Should().Be(TestEmail);
    }

    [Fact]
    public async Task GetByIdAsync_WithValidId_IncludesResponses()
    {
        // Arrange
        var form = SeedForm();
        _dbContext.Set<IntakeFormResponse>().Add(new IntakeFormResponse
        {
            IntakeFormId = form.Id,
            SectionKey = "personal_info",
            FieldKey = "first_name",
            Value = "WithId"
        });
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.GetByIdAsync(form.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Responses.Should().HaveCount(1);
        result.Responses[0].Value.Should().Be("WithId");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByIdAsync(999_999);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // GetByAppointmentIdAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetByAppointmentIdAsync_WithValidAppointmentId_ReturnsListDto()
    {
        // Arrange — use the seeded appointment to satisfy the FK constraint
        var form = SeedForm(appointmentId: _seededAppointmentId);

        // Act
        var result = await _sut.GetByAppointmentIdAsync(_seededAppointmentId);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(form.Id);
        result.AppointmentId.Should().Be(_seededAppointmentId);
        result.Status.Should().Be(IntakeFormStatus.Pending);
    }

    [Fact]
    public async Task GetByAppointmentIdAsync_WithLinkedClient_ResolvesClientName()
    {
        // Arrange — use the seeded appointment (already linked to _seededClientId)
        // Create a second appointment for this test so we don't conflict with other tests using _seededAppointmentId
        var appointment2 = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.InitialConsultation,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(2),
            DurationMinutes = 60,
            Location = AppointmentLocation.InPerson,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment2);
        await _dbContext.SaveChangesAsync();

        SeedForm(appointmentId: appointment2.Id, clientId: _seededClientId);

        // Act
        var result = await _sut.GetByAppointmentIdAsync(appointment2.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ClientName.Should().Be("Alice Intake",
            because: "the service resolves ClientName from the Clients table when ClientId is set");
    }

    [Fact]
    public async Task GetByAppointmentIdAsync_WithoutLinkedClient_ReturnsNullClientName()
    {
        // Arrange — a third distinct appointment for this specific test scenario
        var appointment3 = new Appointment
        {
            ClientId = _seededClientId,
            NutritionistId = NutritionistId,
            Type = AppointmentType.FollowUp,
            Status = AppointmentStatus.Scheduled,
            StartTime = DateTime.UtcNow.AddDays(3),
            DurationMinutes = 30,
            Location = AppointmentLocation.Phone,
            CreatedAt = DateTime.UtcNow
        };
        _dbContext.Appointments.Add(appointment3);
        await _dbContext.SaveChangesAsync();

        SeedForm(appointmentId: appointment3.Id, clientId: null);

        // Act
        var result = await _sut.GetByAppointmentIdAsync(appointment3.Id);

        // Assert
        result.Should().NotBeNull();
        result!.ClientName.Should().BeNull(
            because: "ClientName must be null when no client is linked to the form");
    }

    [Fact]
    public async Task GetByAppointmentIdAsync_WithNonExistentAppointmentId_ReturnsNull()
    {
        // Act
        var result = await _sut.GetByAppointmentIdAsync(999_888);

        // Assert
        result.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // ListFormsAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListFormsAsync_WithNoFilter_ReturnsAllForms()
    {
        // Arrange — seed two forms with distinct tokens to isolate from other test data
        var token1 = "list-no-filter-001";
        var token2 = "list-no-filter-002";
        SeedForm(token: token1);
        SeedForm(token: token2, status: IntakeFormStatus.Submitted);

        // Act
        var result = await _sut.ListFormsAsync();

        // Assert
        result.Should().Contain(f => f.ClientEmail == TestEmail,
            because: "both seeded forms should appear when no status filter is applied");
        result.Should().Contain(f => f.Status == IntakeFormStatus.Pending);
        result.Should().Contain(f => f.Status == IntakeFormStatus.Submitted);
    }

    [Fact]
    public async Task ListFormsAsync_WithStatusFilter_ReturnsOnlyMatchingForms()
    {
        // Arrange
        SeedForm(token: "list-filter-pending");
        SeedForm(token: "list-filter-submitted", status: IntakeFormStatus.Submitted);

        // Act
        var result = await _sut.ListFormsAsync(IntakeFormStatus.Submitted);

        // Assert
        result.Should().OnlyContain(f => f.Status == IntakeFormStatus.Submitted,
            because: "the status filter must exclude forms with a different status");
    }

    [Fact]
    public async Task ListFormsAsync_WithLinkedClients_IncludesClientNames()
    {
        // Arrange
        SeedForm(token: "list-with-client", clientId: _seededClientId);

        // Act
        var result = await _sut.ListFormsAsync();

        // Assert
        var withClient = result.FirstOrDefault(f => f.ClientId == _seededClientId);
        withClient.Should().NotBeNull();
        withClient!.ClientName.Should().Be("Alice Intake",
            because: "ListFormsAsync must resolve client names from the Clients table");
    }

    [Fact]
    public async Task ListFormsAsync_WithNoLinkedClient_LeavesClientNameNull()
    {
        // Arrange
        SeedForm(token: "list-no-client");

        // Act
        var result = await _sut.ListFormsAsync();

        // Assert
        var withoutClient = result.FirstOrDefault(f => f.ClientId == null);
        withoutClient.Should().NotBeNull();
        withoutClient!.ClientName.Should().BeNull();
    }

    [Fact]
    public async Task ListFormsAsync_ReturnsFormsOrderedByCreatedAtDescending()
    {
        // Arrange — seed forms with explicit CreatedAt values spread 1 minute apart
        var oldest = new IntakeForm
        {
            Token = "list-order-oldest",
            Status = IntakeFormStatus.Pending,
            ClientEmail = TestEmail,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = UserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10)
        };
        var newest = new IntakeForm
        {
            Token = "list-order-newest",
            Status = IntakeFormStatus.Pending,
            ClientEmail = TestEmail,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedByUserId = UserId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-1)
        };
        _dbContext.IntakeForms.AddRange(oldest, newest);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _sut.ListFormsAsync();

        // Assert — filter to just the two we seeded for this test
        var testForms = result
            .Where(f => f.ClientEmail == TestEmail)
            .ToList();

        testForms.Should().BeInDescendingOrder(f => f.CreatedAt,
            because: "ListFormsAsync must order results by CreatedAt descending");
    }

    // ---------------------------------------------------------------------------
    // SubmitFormAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_ReturnsTrueAndNoError()
    {
        // Arrange
        var form = SeedForm(token: "submit-valid");
        var responses = BuildSubmitResponseDtos();

        // Act
        var (success, error) = await _sut.SubmitFormAsync("submit-valid", responses);

        // Assert
        success.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_TransitionsToSubmittedStatus()
    {
        // Arrange
        var form = SeedForm(token: "submit-status");
        var responses = BuildSubmitResponseDtos();

        // Act
        await _sut.SubmitFormAsync("submit-status", responses);

        // Assert — use AsNoTracking to bypass the change-tracker cache and read the
        // value actually written by the service's factory-created DbContext
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.Status.Should().Be(IntakeFormStatus.Submitted);
    }

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_SetsSubmittedAt()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var form = SeedForm(token: "submit-timestamp");
        var responses = BuildSubmitResponseDtos();

        // Act
        await _sut.SubmitFormAsync("submit-timestamp", responses);

        // Assert — AsNoTracking bypasses stale cache from the seeded entity
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.SubmittedAt.Should().NotBeNull();
        persisted.SubmittedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_SavesResponsesToDatabase()
    {
        // Arrange
        var form = SeedForm(token: "submit-responses");
        var responses = new List<IntakeFormResponseDto>
        {
            new("personal_info", "first_name", "Test"),
            new("personal_info", "last_name",  "Submitted"),
            new("consent",       "consent_given", "true")
        };

        // Act
        await _sut.SubmitFormAsync("submit-responses", responses);

        // Assert
        var persisted = await _dbContext.IntakeForms
            .Include(f => f.Responses)
            .FirstAsync(f => f.Id == form.Id);
        persisted.Responses.Should().HaveCount(3);
        persisted.Responses.Should().Contain(r => r.SectionKey == "personal_info" && r.FieldKey == "first_name" && r.Value == "Test");
    }

    [Fact]
    public async Task SubmitFormAsync_WithNonExistentToken_ReturnsFalseWithError()
    {
        // Act
        var (success, error) = await _sut.SubmitFormAsync("does-not-exist", []);

        // Assert
        success.Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SubmitFormAsync_WhenAlreadySubmitted_ReturnsFalseWithError()
    {
        // Arrange
        SeedForm(token: "submit-already-submitted", status: IntakeFormStatus.Submitted);

        // Act
        var (success, error) = await _sut.SubmitFormAsync("submit-already-submitted", []);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("already been submitted");
    }

    [Fact]
    public async Task SubmitFormAsync_WhenAlreadyReviewed_ReturnsFalseWithError()
    {
        // Arrange
        SeedForm(token: "submit-already-reviewed", status: IntakeFormStatus.Reviewed);

        // Act
        var (success, error) = await _sut.SubmitFormAsync("submit-already-reviewed", []);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("already been submitted");
    }

    [Fact]
    public async Task SubmitFormAsync_WhenExpired_ReturnsFalseWithError()
    {
        // Arrange — ExpiresAt in the past
        SeedForm(token: "submit-expired", expiresAt: DateTime.UtcNow.AddDays(-1));

        // Act
        var (success, error) = await _sut.SubmitFormAsync("submit-expired", []);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("expired");
    }

    [Fact]
    public async Task SubmitFormAsync_WhenExpired_MarksFormAsExpired()
    {
        // Arrange
        var form = SeedForm(token: "submit-expired-status", expiresAt: DateTime.UtcNow.AddDays(-1));

        // Act
        await _sut.SubmitFormAsync("submit-expired-status", []);

        // Assert — AsNoTracking bypasses stale cache
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.Status.Should().Be(IntakeFormStatus.Expired,
            because: "the service must mark the form Expired when its token has passed the ExpiresAt threshold");
    }

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_CallsAuditLog()
    {
        // Arrange
        var form = SeedForm(token: "submit-audit");

        // Act
        await _sut.SubmitFormAsync("submit-audit", BuildSubmitResponseDtos());

        // Assert
        await _auditLogService.Received(1).LogAsync(
            "system",
            "IntakeFormSubmitted",
            "IntakeForm",
            form.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task SubmitFormAsync_WithLinkedClient_CallsRetentionTracker()
    {
        // Arrange
        SeedForm(token: "submit-retention", clientId: _seededClientId);

        // Act
        await _sut.SubmitFormAsync("submit-retention", BuildSubmitResponseDtos());

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(_seededClientId);
    }

    [Fact]
    public async Task SubmitFormAsync_WithoutLinkedClient_DoesNotCallRetentionTracker()
    {
        // Arrange
        SeedForm(token: "submit-no-retention", clientId: null);

        // Act
        await _sut.SubmitFormAsync("submit-no-retention", BuildSubmitResponseDtos());

        // Assert
        await _retentionTracker.DidNotReceive().UpdateLastInteractionAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task SubmitFormAsync_WithValidToken_DispatchesUpdatedNotification()
    {
        // Arrange
        SeedForm(token: "submit-dispatch");

        // Act
        await _sut.SubmitFormAsync("submit-dispatch", BuildSubmitResponseDtos());

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "IntakeForm" &&
            n.ChangeType == EntityChangeType.Updated));
    }

    // ---------------------------------------------------------------------------
    // ReviewFormAsync tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_ReturnsTrueAndClientId()
    {
        // Arrange
        var form = SeedSubmittedForm();

        // Act
        var (success, clientId, error) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        success.Should().BeTrue();
        clientId.Should().NotBeNull();
        clientId.Should().BeGreaterThan(0);
        error.Should().BeNull();
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_TransitionsToReviewedStatus()
    {
        // Arrange
        var form = SeedSubmittedForm();

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert — AsNoTracking bypasses stale EF change-tracker cache
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.Status.Should().Be(IntakeFormStatus.Reviewed);
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_SetsReviewerInfo()
    {
        // Arrange
        var before = DateTime.UtcNow.AddSeconds(-1);
        var form = SeedSubmittedForm();

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert — AsNoTracking bypasses stale EF change-tracker cache
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.ReviewedByUserId.Should().Be(ReviewerUserId);
        persisted.ReviewedAt.Should().NotBeNull();
        persisted.ReviewedAt.Should().BeAfter(before);
    }

    [Fact]
    public async Task ReviewFormAsync_WithNonExistentFormId_ReturnsFalseWithError()
    {
        // Act
        var (success, clientId, error) = await _sut.ReviewFormAsync(999_777, ReviewerUserId);

        // Assert
        success.Should().BeFalse();
        clientId.Should().BeNull();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReviewFormAsync_WithPendingForm_ReturnsFalseWithError()
    {
        // Arrange
        var form = SeedForm(status: IntakeFormStatus.Pending);

        // Act
        var (success, clientId, error) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        success.Should().BeFalse();
        clientId.Should().BeNull();
        error.Should().Contain("submitted");
    }

    [Fact]
    public async Task ReviewFormAsync_WithAlreadyReviewedForm_ReturnsFalseWithError()
    {
        // Arrange
        var form = SeedForm(status: IntakeFormStatus.Reviewed);

        // Act
        var (success, clientId, error) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        success.Should().BeFalse();
        error.Should().Contain("submitted");
    }

    [Fact]
    public async Task ReviewFormAsync_WithNoLinkedClient_CreatesNewClientFromResponses()
    {
        // Arrange — no clientId on the form so a new Client should be created
        var form = SeedSubmittedForm(clientId: null);
        var countBefore = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();

        // Act
        var (success, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        success.Should().BeTrue();
        var countAfter = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();
        countAfter.Should().Be(countBefore + 1, because: "a new client row must be inserted when no clientId is linked");
        clientId.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReviewFormAsync_WithLinkedClient_DoesNotCreateNewClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: _seededClientId);
        var countBefore = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var countAfter = await _dbContext.Clients.IgnoreQueryFilters().CountAsync();
        countAfter.Should().Be(countBefore, because: "no new client should be created when a clientId is already linked");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsPersonalInfoFirstNameToClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.FirstName.Should().Be("Bob",
            because: "the personal_info.first_name response must be mapped to Client.FirstName");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsPersonalInfoLastNameToClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.LastName.Should().Be("Reviewed");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsPersonalInfoEmailToClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.Email.Should().Be("bob.reviewed@example.com");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsPersonalInfoPhoneToClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.Phone.Should().Be("555-0199");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsPersonalInfoDateOfBirthToClient()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.DateOfBirth.Should().Be(new DateOnly(1990, 3, 15));
    }

    [Fact]
    public async Task ReviewFormAsync_MapsAllergiesToClientAllergies()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var allergies = await _dbContext.ClientAllergies
            .Where(a => a.ClientId == clientId)
            .ToListAsync();

        allergies.Should().HaveCount(2,
            because: "the two allergy names in the JSON array must each produce a ClientAllergy row");
        allergies.Should().Contain(a => a.Name == "Peanuts");
        allergies.Should().Contain(a => a.Name == "Shellfish");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsAllergiesWithFoodTypeAndMildSeverity()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var allergies = await _dbContext.ClientAllergies
            .Where(a => a.ClientId == clientId)
            .ToListAsync();

        allergies.Should().OnlyContain(a => a.AllergyType == AllergyType.Food && a.Severity == AllergySeverity.Mild);
    }

    [Fact]
    public async Task ReviewFormAsync_MapsMedicationsToClientMedications()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var medications = await _dbContext.ClientMedications
            .Where(m => m.ClientId == clientId)
            .ToListAsync();

        medications.Should().HaveCount(2);
        medications.Should().Contain(m => m.Name == "Metformin");
        medications.Should().Contain(m => m.Name == "Lisinopril");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsConditionsToClientConditions()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var conditions = await _dbContext.ClientConditions
            .Where(c => c.ClientId == clientId)
            .ToListAsync();

        conditions.Should().HaveCount(2);
        conditions.Should().Contain(c => c.Name == "Type 2 Diabetes");
        conditions.Should().Contain(c => c.Name == "Hypertension");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsConditionsWithActiveStatus()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var conditions = await _dbContext.ClientConditions
            .Where(c => c.ClientId == clientId)
            .ToListAsync();

        conditions.Should().OnlyContain(c => c.Status == ConditionStatus.Active);
    }

    [Fact]
    public async Task ReviewFormAsync_MapsKnownDietaryRestrictionToEnumValue()
    {
        // Arrange — "Vegan" maps to DietaryRestrictionType.Vegan
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var restrictions = await _dbContext.ClientDietaryRestrictions
            .Where(r => r.ClientId == clientId)
            .ToListAsync();

        restrictions.Should().Contain(r => r.RestrictionType == DietaryRestrictionType.Vegan && r.Notes == null,
            because: "a known enum value must map directly without a Notes fallback");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsUnknownDietaryRestrictionToOtherWithNotes()
    {
        // Arrange — "Custom restriction" does not match any enum member, so it maps to Other
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var restrictions = await _dbContext.ClientDietaryRestrictions
            .Where(r => r.ClientId == clientId)
            .ToListAsync();

        restrictions.Should().Contain(r =>
            r.RestrictionType == DietaryRestrictionType.Other &&
            r.Notes == "Custom restriction",
            because: "unrecognised restriction strings must fall back to Other with the raw value stored in Notes");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsMedicalHistoryNotesToClientNotes()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.Notes.Should().Contain("Appendectomy 2010",   because: "medical_history.surgeries must appear in Notes");
        client.Notes.Should().Contain("Heart disease",       because: "medical_history.family_history must appear in Notes");
        client.Notes.Should().Contain("Takes vitamins",      because: "medical_history.additional_notes must appear in Notes");
    }

    [Fact]
    public async Task ReviewFormAsync_MapsGoalSectionNotesToClientNotes()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.Notes.Should().Contain("Lose weight",  because: "goals.specific_concerns must appear in Notes");
        client.Notes.Should().Contain("3 months",     because: "goals.timeline_expectations must appear in Notes");
    }

    [Fact]
    public async Task ReviewFormAsync_WhenConsentGiven_SetsConsentFieldsOnClient()
    {
        // Arrange — responses include consent.consent_given = "true"
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        var client = await _dbContext.Clients.IgnoreQueryFilters().FirstAsync(c => c.Id == clientId);
        client.ConsentGiven.Should().BeTrue();
        client.ConsentTimestamp.Should().NotBeNull();
        client.ConsentPolicyVersion.Should().Be("1.0");
    }

    [Fact]
    public async Task ReviewFormAsync_WhenConsentGivenForNewClient_GrantsConsentViaService()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        await _consentService.Received(1).GrantConsentAsync(
            clientId!.Value,
            "Treatment and care",
            "1.0",
            ReviewerUserId);
    }

    [Fact]
    public async Task ReviewFormAsync_WhenConsentGivenForExistingClient_DoesNotGrantConsent()
    {
        // Arrange — form already has a linked client so no new consent grant should happen
        var form = SeedSubmittedForm(clientId: _seededClientId);

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        await _consentService.DidNotReceive().GrantConsentAsync(
            Arg.Any<int>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_CallsAuditLog()
    {
        // Arrange
        var form = SeedSubmittedForm();

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        await _auditLogService.Received(1).LogAsync(
            ReviewerUserId,
            "IntakeFormReviewed",
            "IntakeForm",
            form.Id.ToString(),
            Arg.Any<string>());
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_CallsRetentionTracker()
    {
        // Arrange
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        await _retentionTracker.Received(1).UpdateLastInteractionAsync(clientId!.Value);
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_DispatchesUpdatedNotification()
    {
        // Arrange
        var form = SeedSubmittedForm();

        // Act
        await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert
        await _notificationDispatcher.Received(1).DispatchAsync(Arg.Is<EntityChangeNotification>(n =>
            n.EntityType == "IntakeForm" &&
            n.ChangeType == EntityChangeType.Updated));
    }

    [Fact]
    public async Task ReviewFormAsync_WithSubmittedForm_LinksClientIdOnForm()
    {
        // Arrange — no prior client, so one is created
        var form = SeedSubmittedForm(clientId: null);

        // Act
        var (_, clientId, _) = await _sut.ReviewFormAsync(form.Id, ReviewerUserId);

        // Assert — AsNoTracking bypasses stale EF change-tracker cache
        var persisted = await _dbContext.IntakeForms
            .AsNoTracking()
            .FirstAsync(f => f.Id == form.Id);
        persisted.ClientId.Should().Be(clientId,
            because: "the form's ClientId must be updated to point to the mapped/created client after review");
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
