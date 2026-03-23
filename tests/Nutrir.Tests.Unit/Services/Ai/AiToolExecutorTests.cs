using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services.Ai;

public class AiToolExecutorTests
{
    // --- Dependencies (all mocked) ---
    private readonly IClientService _clientService = Substitute.For<IClientService>();
    private readonly IClientHealthProfileService _healthProfileService = Substitute.For<IClientHealthProfileService>();
    private readonly IAppointmentService _appointmentService = Substitute.For<IAppointmentService>();
    private readonly IMealPlanService _mealPlanService = Substitute.For<IMealPlanService>();
    private readonly IAllergenCheckService _allergenCheckService = Substitute.For<IAllergenCheckService>();
    private readonly IProgressService _progressService = Substitute.For<IProgressService>();
    private readonly IUserManagementService _userManagementService = Substitute.For<IUserManagementService>();
    private readonly ISearchService _searchService = Substitute.For<ISearchService>();
    private readonly IDashboardService _dashboardService = Substitute.For<IDashboardService>();
    private readonly IAvailabilityService _availabilityService = Substitute.For<IAvailabilityService>();
    private readonly IIntakeFormService _intakeFormService = Substitute.For<IIntakeFormService>();

    private readonly AiToolExecutor _sut;

    public AiToolExecutorTests()
    {
        _sut = new AiToolExecutor(
            _clientService,
            _healthProfileService,
            _appointmentService,
            _mealPlanService,
            _allergenCheckService,
            _progressService,
            _userManagementService,
            _searchService,
            _dashboardService,
            _availabilityService,
            _intakeFormService,
            NullLogger<AiToolExecutor>.Instance);
    }

    // =========================================================================
    // Helper Methods
    // =========================================================================

    private static IReadOnlyDictionary<string, JsonElement> MakeInput(object input)
    {
        var json = JsonSerializer.Serialize(input);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone());
    }

    private static JsonDocument ParseResult(string json) => JsonDocument.Parse(json);

    private static ClientDto MakeClientDto(int id = 1, string firstName = "Alice", string lastName = "Smith") =>
        new(
            Id: id,
            FirstName: firstName,
            LastName: lastName,
            Email: "alice@example.com",
            Phone: "555-1234",
            DateOfBirth: new DateOnly(1985, 6, 15),
            PrimaryNutritionistId: "nutri-001",
            PrimaryNutritionistName: "Dr. Jones",
            ConsentGiven: true,
            ConsentTimestamp: DateTime.UtcNow,
            ConsentPolicyVersion: "1.0",
            Notes: null,
            IsDeleted: false,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            DeletedAt: null);

    private static AppointmentDto MakeAppointmentDto(int id = 10) =>
        new(
            Id: id,
            ClientId: 1,
            ClientFirstName: "Alice",
            ClientLastName: "Smith",
            NutritionistId: "nutri-001",
            NutritionistName: "Dr. Jones",
            Type: AppointmentType.FollowUp,
            Status: AppointmentStatus.Scheduled,
            StartTime: new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc),
            DurationMinutes: 60,
            EndTime: new DateTime(2026, 4, 1, 11, 0, 0, DateTimeKind.Utc),
            Location: AppointmentLocation.Virtual,
            VirtualMeetingUrl: "https://meet.example.com/abc",
            LocationNotes: null,
            Notes: null,
            PrepNotes: null,
            CancellationReason: null,
            CancelledAt: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null);

    // =========================================================================
    // Category 1: Tool Registration
    // =========================================================================

    public class ToolRegistrationTests : AiToolExecutorTests
    {
        [Theory]
        [InlineData("list_clients")]
        [InlineData("get_client")]
        [InlineData("list_appointments")]
        [InlineData("get_appointment")]
        [InlineData("list_meal_plans")]
        [InlineData("get_meal_plan")]
        [InlineData("export_meal_plan_pdf")]
        [InlineData("list_goals")]
        [InlineData("get_goal")]
        [InlineData("list_progress")]
        [InlineData("get_progress_entry")]
        [InlineData("list_users")]
        [InlineData("get_user")]
        [InlineData("search")]
        [InlineData("get_dashboard")]
        [InlineData("create_client")]
        [InlineData("update_client")]
        [InlineData("delete_client")]
        [InlineData("create_appointment")]
        [InlineData("update_appointment")]
        [InlineData("cancel_appointment")]
        [InlineData("delete_appointment")]
        [InlineData("create_meal_plan")]
        [InlineData("update_meal_plan")]
        [InlineData("activate_meal_plan")]
        [InlineData("archive_meal_plan")]
        [InlineData("duplicate_meal_plan")]
        [InlineData("delete_meal_plan")]
        [InlineData("create_goal")]
        [InlineData("update_goal")]
        [InlineData("achieve_goal")]
        [InlineData("abandon_goal")]
        [InlineData("delete_goal")]
        [InlineData("create_progress_entry")]
        [InlineData("delete_progress_entry")]
        [InlineData("add_client_allergy")]
        [InlineData("update_client_allergy")]
        [InlineData("remove_client_allergy")]
        [InlineData("add_client_medication")]
        [InlineData("update_client_medication")]
        [InlineData("remove_client_medication")]
        [InlineData("add_client_condition")]
        [InlineData("update_client_condition")]
        [InlineData("remove_client_condition")]
        [InlineData("add_client_dietary_restriction")]
        [InlineData("update_client_dietary_restriction")]
        [InlineData("remove_client_dietary_restriction")]
        [InlineData("create_user")]
        [InlineData("change_user_role")]
        [InlineData("deactivate_user")]
        [InlineData("reactivate_user")]
        [InlineData("reset_user_password")]
        [InlineData("list_intake_forms")]
        [InlineData("get_intake_form")]
        [InlineData("create_intake_form")]
        [InlineData("review_intake_form")]
        [InlineData("export_client_data")]
        public async Task ExecuteAsync_KnownTool_DoesNotReturnUnknownToolError(string toolName)
        {
            // For all known tools, ExecuteAsync should not return an "Unknown tool" error.
            // We supply empty input — the handler may return a parameter-missing error,
            // which is acceptable: we're only verifying the tool IS registered.
            var result = await _sut.ExecuteAsync(toolName, new Dictionary<string, JsonElement>(), "user-1", "Nutritionist");
            using var doc = ParseResult(result);

            var hasUnknownError = doc.RootElement.TryGetProperty("error", out var errorProp)
                && errorProp.GetString()?.StartsWith("Unknown tool:") == true;

            hasUnknownError.Should().BeFalse($"tool '{toolName}' should be registered but returned an 'Unknown tool' error");
        }

        [Fact]
        public async Task ExecuteAsync_UnknownToolName_ReturnsErrorJson()
        {
            var result = await _sut.ExecuteAsync("does_not_exist", new Dictionary<string, JsonElement>());

            using var doc = ParseResult(result);
            doc.RootElement.TryGetProperty("error", out var error).Should().BeTrue();
            error.GetString().Should().Contain("Unknown tool: does_not_exist");
        }

        [Fact]
        public void GetToolDefinitions_Returns57Tools()
        {
            var tools = AiToolExecutor.GetToolDefinitions();
            // The handler dictionary and tool definitions list are built independently;
            // both should have the same count (57 registered handlers).
            tools.Should().HaveCount(57);
        }
    }

    // =========================================================================
    // Category 2: GetToolDefinitions
    // =========================================================================

    public class GetToolDefinitionsTests : AiToolExecutorTests
    {
        [Fact]
        public void GetToolDefinitions_ReturnsNonEmptyList()
        {
            var tools = AiToolExecutor.GetToolDefinitions();
            tools.Should().NotBeEmpty();
        }

        [Fact]
        public void GetToolDefinitions_EachToolHasNameAndDescription()
        {
            var tools = AiToolExecutor.GetToolDefinitions();
            foreach (var tool in tools)
            {
                tool.Name.Should().NotBeNullOrWhiteSpace($"every tool must have a name");
                tool.Description.Should().NotBeNullOrWhiteSpace($"tool '{tool.Name}' must have a description");
            }
        }

        [Fact]
        public void GetToolDefinitions_AllNamesAreUnique()
        {
            var tools = AiToolExecutor.GetToolDefinitions();
            var names = tools.Select(t => t.Name).ToList();
            names.Should().OnlyHaveUniqueItems("tool names must not be duplicated");
        }
    }

    // =========================================================================
    // Category 3: Confirmation Tier
    // =========================================================================

    public class ConfirmationTierTests : AiToolExecutorTests
    {
        [Theory]
        [InlineData("list_clients")]
        [InlineData("get_client")]
        [InlineData("list_appointments")]
        [InlineData("get_appointment")]
        [InlineData("list_meal_plans")]
        [InlineData("get_meal_plan")]
        [InlineData("export_meal_plan_pdf")]
        [InlineData("list_goals")]
        [InlineData("get_goal")]
        [InlineData("list_progress")]
        [InlineData("get_progress_entry")]
        [InlineData("list_users")]
        [InlineData("get_user")]
        [InlineData("search")]
        [InlineData("get_dashboard")]
        [InlineData("list_intake_forms")]
        [InlineData("get_intake_form")]
        public void GetConfirmationTier_ReadOnlyTool_ReturnsNull(string toolName)
        {
            _sut.GetConfirmationTier(toolName).Should().BeNull(
                $"read-only tool '{toolName}' should not require confirmation");
        }

        [Theory]
        [InlineData("create_client")]
        [InlineData("update_client")]
        [InlineData("delete_client")]
        [InlineData("create_appointment")]
        [InlineData("update_appointment")]
        [InlineData("cancel_appointment")]
        [InlineData("delete_appointment")]
        [InlineData("create_meal_plan")]
        [InlineData("update_meal_plan")]
        [InlineData("activate_meal_plan")]
        [InlineData("archive_meal_plan")]
        [InlineData("duplicate_meal_plan")]
        [InlineData("delete_meal_plan")]
        [InlineData("create_goal")]
        [InlineData("update_goal")]
        [InlineData("achieve_goal")]
        [InlineData("abandon_goal")]
        [InlineData("delete_goal")]
        [InlineData("create_progress_entry")]
        [InlineData("delete_progress_entry")]
        [InlineData("add_client_allergy")]
        [InlineData("update_client_allergy")]
        [InlineData("remove_client_allergy")]
        [InlineData("add_client_medication")]
        [InlineData("update_client_medication")]
        [InlineData("remove_client_medication")]
        [InlineData("add_client_condition")]
        [InlineData("update_client_condition")]
        [InlineData("remove_client_condition")]
        [InlineData("add_client_dietary_restriction")]
        [InlineData("update_client_dietary_restriction")]
        [InlineData("remove_client_dietary_restriction")]
        [InlineData("create_intake_form")]
        [InlineData("review_intake_form")]
        public void GetConfirmationTier_StandardTierTool_ReturnsStandard(string toolName)
        {
            _sut.GetConfirmationTier(toolName).Should().Be(ConfirmationTier.Standard,
                $"tool '{toolName}' should have Standard confirmation tier");
        }

        [Theory]
        [InlineData("create_user")]
        [InlineData("change_user_role")]
        [InlineData("deactivate_user")]
        [InlineData("reactivate_user")]
        [InlineData("reset_user_password")]
        [InlineData("export_client_data")]
        public void GetConfirmationTier_ElevatedTierTool_ReturnsElevated(string toolName)
        {
            _sut.GetConfirmationTier(toolName).Should().Be(ConfirmationTier.Elevated,
                $"tool '{toolName}' should have Elevated confirmation tier");
        }

        [Fact]
        public void GetConfirmationTier_UnknownTool_ReturnsNull()
        {
            _sut.GetConfirmationTier("not_a_real_tool").Should().BeNull();
        }
    }

    // =========================================================================
    // Category 4: Read Handler Tests
    // =========================================================================

    public class ListClientsHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ListClients_NoFilter_ReturnsAllClients()
        {
            var clients = new List<ClientDto> { MakeClientDto(1), MakeClientDto(2, "Bob", "Jones") };
            _clientService.GetListAsync(null).Returns(clients);

            var result = await _sut.ExecuteAsync("list_clients", MakeInput(new { }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("count").GetInt32().Should().Be(2);
            doc.RootElement.GetProperty("clients").GetArrayLength().Should().Be(2);
        }

        [Fact]
        public async Task ExecuteAsync_ListClients_WithSearchTerm_PassesSearchTermToService()
        {
            _clientService.GetListAsync("alice").Returns(new List<ClientDto> { MakeClientDto() });

            var result = await _sut.ExecuteAsync("list_clients", MakeInput(new { search_term = "alice" }), "user-1", "Nutritionist");

            await _clientService.Received(1).GetListAsync(Arg.Is("alice"));
            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("count").GetInt32().Should().Be(1);
        }
    }

    public class GetClientHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_GetClient_Found_ReturnsClientAndHealthProfile()
        {
            var client = MakeClientDto(42);
            var healthProfile = new ClientHealthProfileSummaryDto(42, [], [], [], []);

            _clientService.GetByIdAsync(42).Returns(client);
            _healthProfileService.GetHealthProfileSummaryAsync(42).Returns(healthProfile);

            var result = await _sut.ExecuteAsync("get_client", MakeInput(new { id = 42 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.TryGetProperty("client", out _).Should().BeTrue("response should contain 'client'");
            doc.RootElement.TryGetProperty("health_profile", out _).Should().BeTrue("response should contain 'health_profile'");
        }

        [Fact]
        public async Task ExecuteAsync_GetClient_NotFound_ReturnsErrorJson()
        {
            _clientService.GetByIdAsync(99).Returns((ClientDto?)null);

            var result = await _sut.ExecuteAsync("get_client", MakeInput(new { id = 99 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("99");
        }
    }

    public class ListAppointmentsHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ListAppointments_NonAdmin_AutoScopesToOwnNutritionistId()
        {
            _appointmentService
                .GetListAsync(Arg.Any<DateTime?>(), Arg.Any<DateTime?>(), Arg.Any<int?>(),
                    Arg.Any<AppointmentStatus?>(), Arg.Is<string?>("nutri-user-42"))
                .Returns(new List<AppointmentDto>());

            await _sut.ExecuteAsync("list_appointments", MakeInput(new { }), "nutri-user-42", "Nutritionist");

            await _appointmentService.Received(1)
                .GetListAsync(null, null, null, null, "nutri-user-42");
        }

        [Fact]
        public async Task ExecuteAsync_ListAppointments_Admin_DoesNotAutoScopeToUserId()
        {
            _appointmentService
                .GetListAsync(null, null, null, null, null)
                .Returns(new List<AppointmentDto>());

            await _sut.ExecuteAsync("list_appointments", MakeInput(new { }), "admin-user-1", "Admin");

            // Admin with no nutritionist_id filter should pass null (sees all)
            await _appointmentService.Received(1)
                .GetListAsync(null, null, null, null, null);
        }
    }

    public class GetDashboardHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_GetDashboard_AdminRole_UsesGlobalDashboardService()
        {
            var metrics = new DashboardMetricsDto(10, 2, 3);
            _dashboardService.GetMetricsAsync().Returns(metrics);
            _dashboardService.GetTodaysAppointmentsAsync().Returns(new List<AppointmentDto>());
            _dashboardService.GetThisWeekAppointmentCountAsync().Returns(5);
            _dashboardService.GetActiveMealPlanCountAsync().Returns(7);

            var result = await _sut.ExecuteAsync("get_dashboard", MakeInput(new { }), "admin-1", "Admin");

            await _dashboardService.Received(1).GetTodaysAppointmentsAsync();
            await _dashboardService.Received(1).GetThisWeekAppointmentCountAsync();
            await _appointmentService.DidNotReceive().GetTodaysAppointmentsAsync(Arg.Any<string>());

            using var doc = ParseResult(result);
            doc.RootElement.TryGetProperty("metrics", out _).Should().BeTrue();
            doc.RootElement.TryGetProperty("todays_appointments", out _).Should().BeTrue();
            doc.RootElement.GetProperty("active_meal_plan_count").GetInt32().Should().Be(7);
        }

        [Fact]
        public async Task ExecuteAsync_GetDashboard_NonAdminRole_UsesPerNutritionistAppointmentService()
        {
            var metrics = new DashboardMetricsDto(5, 1, 1);
            _dashboardService.GetMetricsAsync().Returns(metrics);
            _appointmentService.GetTodaysAppointmentsAsync("nutri-user-1").Returns(new List<AppointmentDto>());
            _appointmentService.GetWeekCountAsync("nutri-user-1").Returns(3);
            _dashboardService.GetActiveMealPlanCountAsync().Returns(4);

            var result = await _sut.ExecuteAsync("get_dashboard", MakeInput(new { }), "nutri-user-1", "Nutritionist");

            await _appointmentService.Received(1).GetTodaysAppointmentsAsync("nutri-user-1");
            await _appointmentService.Received(1).GetWeekCountAsync("nutri-user-1");
            await _dashboardService.DidNotReceive().GetTodaysAppointmentsAsync();
        }
    }

    // =========================================================================
    // Category 5: Write Handler Tests — Clients
    // =========================================================================

    public class CreateClientHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateClient_ConsentGivenTrue_CreatesClientAndReturnsSuccess()
        {
            var createdClient = MakeClientDto(101);
            _clientService
                .CreateAsync(Arg.Any<ClientDto>(), Arg.Any<string>())
                .Returns(createdClient);

            var result = await _sut.ExecuteAsync("create_client", MakeInput(new
            {
                first_name = "Alice",
                last_name = "Smith",
                primary_nutritionist_id = "nutri-001",
                consent_given = true
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("client", out _).Should().BeTrue();

            await _clientService.Received(1)
                .CreateAsync(Arg.Is<ClientDto>(d =>
                    d.FirstName == "Alice" &&
                    d.LastName == "Smith" &&
                    d.ConsentGiven == true), "user-1");
        }

        [Fact]
        public async Task ExecuteAsync_CreateClient_ConsentGivenFalse_ReturnsConsentError()
        {
            var result = await _sut.ExecuteAsync("create_client", MakeInput(new
            {
                first_name = "Bob",
                last_name = "Jones",
                primary_nutritionist_id = "nutri-001",
                consent_given = false
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("consent_given must be true");

            await _clientService.DidNotReceive().CreateAsync(Arg.Any<ClientDto>(), Arg.Any<string>());
        }

        [Fact]
        public async Task ExecuteAsync_CreateClient_ConsentNotProvided_ReturnsConsentError()
        {
            // Missing consent_given entirely — defaults to false
            var result = await _sut.ExecuteAsync("create_client", MakeInput(new
            {
                first_name = "Carol",
                last_name = "White",
                primary_nutritionist_id = "nutri-001"
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("consent_given must be true");
        }

        [Fact]
        public async Task ExecuteAsync_CreateClient_MissingFirstName_ReturnsErrorJson()
        {
            // first_name is required — missing it triggers ArgumentException caught by ExecuteAsync
            var result = await _sut.ExecuteAsync("create_client", MakeInput(new
            {
                last_name = "Smith",
                primary_nutritionist_id = "nutri-001",
                consent_given = true
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("Error executing create_client");
        }
    }

    public class DeleteClientHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_DeleteClient_Success_ReturnsTrueAndMessage()
        {
            _clientService.SoftDeleteAsync(5, "user-1").Returns(true);

            var result = await _sut.ExecuteAsync("delete_client", MakeInput(new { id = 5 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("message").GetString().Should().Contain("#5");

            await _clientService.Received(1).SoftDeleteAsync(5, "user-1");
        }

        [Fact]
        public async Task ExecuteAsync_DeleteClient_NotFound_ReturnsErrorJson()
        {
            _clientService.SoftDeleteAsync(99, Arg.Any<string>()).Returns(false);

            var result = await _sut.ExecuteAsync("delete_client", MakeInput(new { id = 99 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("99");
        }
    }

    // =========================================================================
    // Category 6: Write Handler Tests — Appointments
    // =========================================================================

    public class CreateAppointmentHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateAppointment_ValidInput_CreatesAppointmentAndReturnsSuccess()
        {
            var created = MakeAppointmentDto(20);
            _appointmentService.CreateAsync(Arg.Any<CreateAppointmentDto>(), Arg.Any<string>()).Returns(created);

            var result = await _sut.ExecuteAsync("create_appointment", MakeInput(new
            {
                client_id = 1,
                type = "FollowUp",
                start_time = "2026-04-01T10:00:00Z",
                duration_minutes = 60,
                location = "Virtual"
            }), "nutri-001", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("appointment", out _).Should().BeTrue();

            await _appointmentService.Received(1)
                .CreateAsync(Arg.Is<CreateAppointmentDto>(d =>
                    d.ClientId == 1 &&
                    d.Type == AppointmentType.FollowUp &&
                    d.DurationMinutes == 60 &&
                    d.Location == AppointmentLocation.Virtual), "nutri-001");
        }
    }

    public class CancelAppointmentHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CancelAppointment_Success_CallsUpdateStatusWithCancelled()
        {
            _appointmentService.UpdateStatusAsync(10, AppointmentStatus.Cancelled, "user-1", "Client request").Returns(true);

            var result = await _sut.ExecuteAsync("cancel_appointment", MakeInput(new
            {
                id = 10,
                cancellation_reason = "Client request"
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();

            await _appointmentService.Received(1)
                .UpdateStatusAsync(10, AppointmentStatus.Cancelled, "user-1", "Client request");
        }

        [Fact]
        public async Task ExecuteAsync_CancelAppointment_NotFound_ReturnsErrorJson()
        {
            _appointmentService.UpdateStatusAsync(Arg.Any<int>(), Arg.Any<AppointmentStatus>(), Arg.Any<string>(), Arg.Any<string?>())
                .Returns(false);

            var result = await _sut.ExecuteAsync("cancel_appointment", MakeInput(new { id = 55 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("55");
        }
    }

    // =========================================================================
    // Category 7: Write Handler Tests — Meal Plans
    // =========================================================================

    public class ActivateMealPlanHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ActivateMealPlan_SuccessWithNoWarnings_ReturnsSuccess()
        {
            _mealPlanService.UpdateStatusAsync(3, MealPlanStatus.Active, "user-1")
                .Returns(new UpdateStatusResultDto(true));
            _allergenCheckService.CheckAsync(3).Returns(new List<AllergenWarningDto>());

            var result = await _sut.ExecuteAsync("activate_meal_plan", MakeInput(new { id = 3 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("warnings", out _).Should().BeFalse("no warnings should be present");
        }

        [Fact]
        public async Task ExecuteAsync_ActivateMealPlan_SuccessWithModerateWarnings_ReturnsSuccessWithWarnings()
        {
            _mealPlanService.UpdateStatusAsync(3, MealPlanStatus.Active, "user-1")
                .Returns(new UpdateStatusResultDto(true));

            var warning = new AllergenWarningDto(
                MealItemId: 1, FoodName: "Peanut Butter", DayNumber: 1, DayLabel: "Day 1",
                MealType: MealType.Breakfast, AllergenCategory: AllergenCategory.Peanut,
                MatchedAllergyName: "Peanuts", Severity: AllergySeverity.Moderate,
                IsOverridden: false, OverrideNote: null, AcknowledgedByUserId: null, AcknowledgedAt: null);

            _allergenCheckService.CheckAsync(3).Returns(new List<AllergenWarningDto> { warning });

            var result = await _sut.ExecuteAsync("activate_meal_plan", MakeInput(new { id = 3 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("warnings", out var warningsEl).Should().BeTrue();
            warningsEl.GetArrayLength().Should().Be(1);
        }

        [Fact]
        public async Task ExecuteAsync_ActivateMealPlan_ServiceFails_ReturnsErrorJson()
        {
            _mealPlanService.UpdateStatusAsync(3, MealPlanStatus.Active, Arg.Any<string>())
                .Returns(new UpdateStatusResultDto(false, "Cannot activate: severe allergen warnings exist"));

            var result = await _sut.ExecuteAsync("activate_meal_plan", MakeInput(new { id = 3 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("severe allergen warnings");
        }
    }

    public class ExportMealPlanPdfHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ExportMealPlanPdf_PlanFound_ReturnsDownloadUrl()
        {
            _mealPlanService.GetByIdAsync(7).Returns(BuildMealPlanDetailDto(7, "Week 1 Plan"));

            var result = await _sut.ExecuteAsync("export_meal_plan_pdf", MakeInput(new { id = 7 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("download_url").GetString().Should().Contain("/api/meal-plans/7/pdf");
            doc.RootElement.GetProperty("message").GetString().Should().Contain("Week 1 Plan");
        }

        [Fact]
        public async Task ExecuteAsync_ExportMealPlanPdf_PlanNotFound_ReturnsErrorJson()
        {
            _mealPlanService.GetByIdAsync(99).Returns((MealPlanDetailDto?)null);

            var result = await _sut.ExecuteAsync("export_meal_plan_pdf", MakeInput(new { id = 99 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("99");
        }

        private static MealPlanDetailDto BuildMealPlanDetailDto(int id, string title) =>
            new(Id: id, Title: title, Description: null, Status: MealPlanStatus.Draft,
                ClientId: 1, ClientFirstName: "Alice", ClientLastName: "Smith",
                CreatedByUserId: "user-1", CreatedByName: null,
                StartDate: null, EndDate: null,
                CalorieTarget: null, ProteinTargetG: null, CarbsTargetG: null, FatTargetG: null,
                Notes: null, Instructions: null,
                Days: [], CreatedAt: DateTime.UtcNow, UpdatedAt: null);
    }

    public class ExportClientDataHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ExportClientData_ValidJsonFormat_ReturnsDownloadUrl()
        {
            _clientService.GetByIdAsync(5).Returns(MakeClientDto(5, "Eve", "Green"));

            var result = await _sut.ExecuteAsync("export_client_data", MakeInput(new { client_id = 5, format = "json" }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("download_url").GetString().Should().Contain("/api/clients/5/export?format=json");
            doc.RootElement.GetProperty("format").GetString().Should().Be("json");
            doc.RootElement.GetProperty("client_name").GetString().Should().Be("Eve Green");
        }

        [Fact]
        public async Task ExecuteAsync_ExportClientData_InvalidFormat_ReturnsErrorJson()
        {
            var result = await _sut.ExecuteAsync("export_client_data", MakeInput(new { client_id = 5, format = "csv" }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("Invalid format");
        }

        [Fact]
        public async Task ExecuteAsync_ExportClientData_ClientNotFound_ReturnsErrorJson()
        {
            _clientService.GetByIdAsync(999).Returns((ClientDto?)null);

            var result = await _sut.ExecuteAsync("export_client_data", MakeInput(new { client_id = 999 }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("999");
        }
    }

    // =========================================================================
    // Category 8: Write Handler Tests — Goals
    // =========================================================================

    public class CreateGoalHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateGoal_ValidInput_CallsCreateGoalAsyncWithCorrectDto()
        {
            var created = new ProgressGoalDetailDto(
                Id: 1, ClientId: 10, ClientFirstName: "Alice", ClientLastName: "Smith",
                CreatedByUserId: "user-1", CreatedByName: null,
                Title: "Lose 5kg", Description: null, GoalType: GoalType.Weight,
                Status: GoalStatus.Active, TargetValue: 5m, TargetUnit: "kg",
                TargetDate: new DateOnly(2026, 6, 1),
                CreatedAt: DateTime.UtcNow, UpdatedAt: null);

            _progressService
                .CreateGoalAsync(Arg.Any<CreateProgressGoalDto>(), Arg.Any<string>())
                .Returns(created);

            var result = await _sut.ExecuteAsync("create_goal", MakeInput(new
            {
                client_id = 10,
                title = "Lose 5kg",
                goal_type = "Weight",
                target_value = 5.0,
                target_unit = "kg"
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("goal", out _).Should().BeTrue();

            await _progressService.Received(1).CreateGoalAsync(
                Arg.Is<CreateProgressGoalDto>(d =>
                    d.ClientId == 10 &&
                    d.Title == "Lose 5kg" &&
                    d.GoalType == GoalType.Weight),
                "user-1");
        }
    }

    // =========================================================================
    // Category 9: Write Handler Tests — Progress Entries
    // =========================================================================

    public class CreateProgressEntryHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateProgressEntry_WithMeasurements_PassesMeasurementsToService()
        {
            var created = new ProgressEntryDetailDto(
                Id: 1, ClientId: 5, ClientFirstName: "Alice", ClientLastName: "Smith",
                CreatedByUserId: "user-1", CreatedByName: null,
                EntryDate: new DateOnly(2026, 3, 20),
                Notes: "Feeling great",
                Measurements: [new ProgressMeasurementDto(1, MetricType.Weight, null, 72.5m, "kg")],
                CreatedAt: DateTime.UtcNow, UpdatedAt: null);

            _progressService.CreateEntryAsync(Arg.Any<CreateProgressEntryDto>(), Arg.Any<string>()).Returns(created);

            var result = await _sut.ExecuteAsync("create_progress_entry", MakeInput(new
            {
                client_id = 5,
                entry_date = "2026-03-20",
                notes = "Feeling great",
                measurements = new[]
                {
                    new { metric_type = "Weight", value = 72.5, unit = "kg" }
                }
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("progress_entry", out _).Should().BeTrue();

            await _progressService.Received(1).CreateEntryAsync(
                Arg.Is<CreateProgressEntryDto>(d =>
                    d.ClientId == 5 &&
                    d.Measurements.Count == 1 &&
                    d.Measurements[0].MetricType == MetricType.Weight &&
                    d.Measurements[0].Value == 72.5m),
                "user-1");
        }
    }

    // =========================================================================
    // Category 10: Write Handler Tests — Health Profile
    // =========================================================================

    public class AddClientAllergyHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_AddClientAllergy_ValidInput_CallsCreateAllergyAsyncWithCorrectDto()
        {
            var created = new ClientAllergyDto(
                Id: 1, ClientId: 3, Name: "Peanuts", Severity: AllergySeverity.Severe,
                AllergyType: AllergyType.Food, CreatedAt: DateTime.UtcNow, UpdatedAt: null);

            _healthProfileService
                .CreateAllergyAsync(Arg.Any<CreateClientAllergyDto>(), Arg.Any<string>())
                .Returns(created);

            var result = await _sut.ExecuteAsync("add_client_allergy", MakeInput(new
            {
                client_id = 3,
                name = "Peanuts",
                severity = "Severe",
                allergy_type = "Food"
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("allergy", out _).Should().BeTrue();

            await _healthProfileService.Received(1).CreateAllergyAsync(
                Arg.Is<CreateClientAllergyDto>(d =>
                    d.ClientId == 3 &&
                    d.Name == "Peanuts" &&
                    d.Severity == AllergySeverity.Severe &&
                    d.AllergyType == AllergyType.Food),
                "user-1");
        }
    }

    // =========================================================================
    // Category 11: Write Handler Tests — User Management
    // =========================================================================

    public class CreateUserHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateUser_ValidInput_CallsCreateUserAsyncAndReturnsUser()
        {
            var userDetail = new UserDetailDto(
                "new-user-guid", "Jane", "Doe", "Jane Doe", "jane@example.com",
                "Nutritionist", true, DateTime.UtcNow, null, false);
            var createResult = new CreateUserResultDto(userDetail, "Temp@1234!");

            _userManagementService
                .CreateUserAsync(Arg.Any<CreateUserDto>(), Arg.Any<string>())
                .Returns(createResult);

            var result = await _sut.ExecuteAsync("create_user", MakeInput(new
            {
                first_name = "Jane",
                last_name = "Doe",
                email = "jane@example.com",
                role = "Nutritionist"
            }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.TryGetProperty("user", out _).Should().BeTrue();
            doc.RootElement.GetProperty("generated_password").GetString().Should().Be("Temp@1234!");

            await _userManagementService.Received(1).CreateUserAsync(
                Arg.Is<CreateUserDto>(d =>
                    d.FirstName == "Jane" &&
                    d.LastName == "Doe" &&
                    d.Email == "jane@example.com" &&
                    d.Role == "Nutritionist"),
                "admin-1");
        }
    }

    public class ChangeUserRoleHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ChangeUserRole_Success_ReturnsSuccessMessage()
        {
            _userManagementService.ChangeRoleAsync("user-guid", "Admin", "admin-1").Returns(true);

            var result = await _sut.ExecuteAsync("change_user_role", MakeInput(new
            {
                user_id = "user-guid",
                new_role = "Admin"
            }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("message").GetString().Should().Contain("Admin");
        }

        [Fact]
        public async Task ExecuteAsync_ChangeUserRole_UserNotFound_ReturnsErrorJson()
        {
            _userManagementService.ChangeRoleAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(false);

            var result = await _sut.ExecuteAsync("change_user_role", MakeInput(new
            {
                user_id = "ghost-user",
                new_role = "Admin"
            }), "admin-1", "Admin");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("ghost-user");
        }
    }

    // =========================================================================
    // Category 12: Write Handler Tests — Intake Forms
    // =========================================================================

    public class CreateIntakeFormHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_CreateIntakeForm_ValidInput_ReturnsTokenAndIntakeUrl()
        {
            var form = new IntakeFormDto(
                Id: 77, ClientId: null, AppointmentId: null,
                Token: "abc-token-xyz", Status: IntakeFormStatus.Pending,
                ClientEmail: "newclient@example.com",
                ExpiresAt: DateTime.UtcNow.AddDays(7),
                SubmittedAt: null, ReviewedAt: null, ReviewedByUserId: null,
                CreatedByUserId: "user-1", CreatedAt: DateTime.UtcNow,
                Responses: []);

            _intakeFormService
                .CreateFormAsync("newclient@example.com", null, null, "user-1")
                .Returns(form);

            var result = await _sut.ExecuteAsync("create_intake_form", MakeInput(new
            {
                client_email = "newclient@example.com"
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("form_id").GetInt32().Should().Be(77);
            doc.RootElement.GetProperty("token").GetString().Should().Be("abc-token-xyz");
            doc.RootElement.GetProperty("intake_url").GetString().Should().Be("/intake/abc-token-xyz");
        }
    }

    public class ReviewIntakeFormHandlerTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ReviewIntakeForm_Success_ReturnsSuccessWithClientId()
        {
            _intakeFormService.ReviewFormAsync(12, "user-1").Returns((true, 55, (string?)null));

            var result = await _sut.ExecuteAsync("review_intake_form", MakeInput(new { form_id = 12 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("client_id").GetInt32().Should().Be(55);
            doc.RootElement.GetProperty("message").GetString().Should().Contain("#12");
        }

        [Fact]
        public async Task ExecuteAsync_ReviewIntakeForm_Failure_ReturnsErrorJson()
        {
            _intakeFormService.ReviewFormAsync(12, Arg.Any<string>()).Returns((false, (int?)null, "Form not submitted yet"));

            var result = await _sut.ExecuteAsync("review_intake_form", MakeInput(new { form_id = 12 }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Be("Form not submitted yet");
        }
    }

    // =========================================================================
    // Category 13: Error Handling Tests
    // =========================================================================

    public class ErrorHandlingTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_ServiceThrowsException_ReturnsErrorJsonWithoutRethrow()
        {
            _clientService.GetListAsync(Arg.Any<string?>())
                .Throws(new InvalidOperationException("Database connection lost"));

            var result = await _sut.ExecuteAsync("list_clients", MakeInput(new { }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("Error executing list_clients");
        }

        [Fact]
        public async Task ExecuteAsync_MissingRequiredParameter_ReturnsErrorJsonWithoutRethrow()
        {
            // 'id' is required for get_client but is omitted
            var result = await _sut.ExecuteAsync("get_client", MakeInput(new { }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("Error executing get_client");
        }

        [Fact]
        public async Task ExecuteAsync_UnknownTool_ReturnsErrorJsonNotException()
        {
            Func<Task> act = () => _sut.ExecuteAsync("totally_fake_tool", new Dictionary<string, JsonElement>());

            await act.Should().NotThrowAsync("ExecuteAsync must never throw — it always returns a JSON string");

            var result = await _sut.ExecuteAsync("totally_fake_tool", new Dictionary<string, JsonElement>());
            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString().Should().Contain("Unknown tool");
        }

        [Fact]
        public async Task ExecuteAsync_ServiceThrowsOnWriteOperation_ReturnsErrorJsonWithoutRethrow()
        {
            _clientService
                .CreateAsync(Arg.Any<ClientDto>(), Arg.Any<string>())
                .Throws(new TimeoutException("Db timeout"));

            var result = await _sut.ExecuteAsync("create_client", MakeInput(new
            {
                first_name = "Alice",
                last_name = "Smith",
                primary_nutritionist_id = "nutri-001",
                consent_given = true
            }), "user-1", "Nutritionist");

            using var doc = ParseResult(result);
            doc.RootElement.GetProperty("error").GetString()
                .Should().Contain("Error executing create_client");
        }
    }

    // =========================================================================
    // Category 14: BuildConfirmationDescriptionAsync Tests
    // =========================================================================

    public class BuildConfirmationDescriptionTests : AiToolExecutorTests
    {
        [Fact]
        public async Task BuildConfirmationDescriptionAsync_CreateClient_IncludesNameAndConsentStatus()
        {
            var input = JsonSerializer.SerializeToElement(new
            {
                first_name = "Alice",
                last_name = "Smith",
                primary_nutritionist_id = "nutri-001",
                consent_given = true
            });

            var (description, entity) = await _sut.BuildConfirmationDescriptionAsync("create_client", input);

            description.Should().Contain("Alice Smith");
            description.Should().Contain("confirmed");
            entity.Should().NotBeNull();
            entity!.EntityType.Should().Be("Client");
            entity.OperationType.Should().Be("create");
        }

        [Fact]
        public async Task BuildConfirmationDescriptionAsync_CreateClient_ConsentFalse_IncludesNotConfirmed()
        {
            var input = JsonSerializer.SerializeToElement(new
            {
                first_name = "Bob",
                last_name = "Jones",
                primary_nutritionist_id = "nutri-001",
                consent_given = false
            });

            var (description, _) = await _sut.BuildConfirmationDescriptionAsync("create_client", input);

            description.Should().Contain("not confirmed");
        }

        [Fact]
        public async Task BuildConfirmationDescriptionAsync_UpdateClient_ResolvesClientNameFromService()
        {
            _clientService.GetByIdAsync(7).Returns(MakeClientDto(7, "Carol", "White"));

            var input = JsonSerializer.SerializeToElement(new
            {
                id = 7,
                first_name = "Carol",
                last_name = "White",
                primary_nutritionist_id = "nutri-001"
            });

            var (description, entity) = await _sut.BuildConfirmationDescriptionAsync("update_client", input);

            description.Should().Contain("Carol White");
            description.Should().Contain("#7");
            entity.Should().NotBeNull();
        }

        [Fact]
        public async Task BuildConfirmationDescriptionAsync_UnknownTool_ReturnsFallbackDescription()
        {
            var input = JsonSerializer.SerializeToElement(new { });

            var (description, entity) = await _sut.BuildConfirmationDescriptionAsync("some_new_tool", input);

            // Fallback formats the tool name with spaces
            description.Should().Contain("some new tool");
            entity.Should().BeNull();
        }

        [Fact]
        public async Task BuildConfirmationDescriptionAsync_ServiceFails_FallsBackGracefully()
        {
            _clientService.GetByIdAsync(Arg.Any<int>())
                .Throws(new Exception("Service unavailable"));

            var input = JsonSerializer.SerializeToElement(new
            {
                id = 99,
                first_name = "Dave",
                last_name = "Brown",
                primary_nutritionist_id = "nutri-001"
            });

            // Should not throw — falls back to basic description
            Func<Task> act = () => _sut.BuildConfirmationDescriptionAsync("update_client", input);
            await act.Should().NotThrowAsync();

            var (description, _) = await _sut.BuildConfirmationDescriptionAsync("update_client", input);
            description.Should().NotBeNullOrWhiteSpace();
        }
    }

    // =========================================================================
    // Category 15: UserId Propagation Tests
    // =========================================================================

    public class UserIdPropagationTests : AiToolExecutorTests
    {
        [Fact]
        public async Task ExecuteAsync_PassedUserId_IsForwardedToServiceCalls()
        {
            _clientService.SoftDeleteAsync(5, "acting-user-xyz").Returns(true);

            await _sut.ExecuteAsync("delete_client", MakeInput(new { id = 5 }), "acting-user-xyz", "Nutritionist");

            await _clientService.Received(1).SoftDeleteAsync(5, "acting-user-xyz");
        }

        [Fact]
        public async Task ExecuteAsync_NullUserId_FallsBackToSystemString()
        {
            _clientService.SoftDeleteAsync(5, "system").Returns(true);

            // Pass null userId — should fall back to "system"
            await _sut.ExecuteAsync("delete_client", MakeInput(new { id = 5 }), null, null);

            await _clientService.Received(1).SoftDeleteAsync(5, "system");
        }
    }
}
