using System.Text.Json;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class AiToolExecutor
{
    private readonly IClientService _clientService;
    private readonly IAppointmentService _appointmentService;
    private readonly IMealPlanService _mealPlanService;
    private readonly IProgressService _progressService;
    private readonly IUserManagementService _userManagementService;
    private readonly ISearchService _searchService;
    private readonly IDashboardService _dashboardService;
    private readonly ILogger<AiToolExecutor> _logger;

    private readonly Dictionary<string, Func<JsonElement, Task<string>>> _handlers;

    public AiToolExecutor(
        IClientService clientService,
        IAppointmentService appointmentService,
        IMealPlanService mealPlanService,
        IProgressService progressService,
        IUserManagementService userManagementService,
        ISearchService searchService,
        IDashboardService dashboardService,
        ILogger<AiToolExecutor> logger)
    {
        _clientService = clientService;
        _appointmentService = appointmentService;
        _mealPlanService = mealPlanService;
        _progressService = progressService;
        _userManagementService = userManagementService;
        _searchService = searchService;
        _dashboardService = dashboardService;
        _logger = logger;

        _handlers = new Dictionary<string, Func<JsonElement, Task<string>>>
        {
            ["list_clients"] = HandleListClients,
            ["get_client"] = HandleGetClient,
            ["list_appointments"] = HandleListAppointments,
            ["get_appointment"] = HandleGetAppointment,
            ["list_meal_plans"] = HandleListMealPlans,
            ["get_meal_plan"] = HandleGetMealPlan,
            ["list_goals"] = HandleListGoals,
            ["get_goal"] = HandleGetGoal,
            ["list_progress"] = HandleListProgress,
            ["get_progress_entry"] = HandleGetProgressEntry,
            ["list_users"] = HandleListUsers,
            ["get_user"] = HandleGetUser,
            ["search"] = HandleSearch,
            ["get_dashboard"] = HandleGetDashboard,
        };
    }

    public async Task<string> ExecuteAsync(string toolName, IReadOnlyDictionary<string, JsonElement> input)
    {
        // Convert dictionary to JsonElement for handler convenience
        var jsonElement = JsonSerializer.SerializeToElement(input);

        if (_handlers.TryGetValue(toolName, out var handler))
        {
            try
            {
                return await handler(jsonElement);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
                return JsonSerializer.Serialize(new { error = $"Error executing {toolName}: {ex.Message}" });
            }
        }

        return JsonSerializer.Serialize(new { error = $"Unknown tool: {toolName}" });
    }

    public static IReadOnlyList<Tool> GetToolDefinitions()
    {
        return
        [
            CreateTool("list_clients", "List all clients in the practice. Optionally filter by search term.",
                new Dictionary<string, object>
                {
                    ["search_term"] = new { type = "string", description = "Optional search term to filter clients by name or email" }
                }),

            CreateTool("get_client", "Get detailed information about a specific client by ID.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The client ID" }
                },
                "id"),

            CreateTool("list_appointments", "List appointments with optional filters for date range, client, and status.",
                new Dictionary<string, object>
                {
                    ["from_date"] = new { type = "string", description = "Start date filter (ISO 8601 UTC, e.g. 2025-01-01T00:00:00Z)" },
                    ["to_date"] = new { type = "string", description = "End date filter (ISO 8601 UTC, e.g. 2025-12-31T23:59:59Z)" },
                    ["client_id"] = new { type = "integer", description = "Filter by client ID" },
                    ["status"] = new { type = "string", description = "Filter by status: Scheduled, Confirmed, Completed, NoShow, LateCancellation, Cancelled" }
                }),

            CreateTool("get_appointment", "Get detailed information about a specific appointment by ID.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The appointment ID" }
                },
                "id"),

            CreateTool("list_meal_plans", "List meal plans with optional filters for client and status.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "Filter by client ID" },
                    ["status"] = new { type = "string", description = "Filter by status: Draft, Active, Archived" }
                }),

            CreateTool("get_meal_plan", "Get detailed information about a specific meal plan including all days, slots, and items.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID" }
                },
                "id"),

            CreateTool("list_goals", "List all progress goals for a specific client.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" }
                },
                "client_id"),

            CreateTool("get_goal", "Get detailed information about a specific progress goal.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The goal ID" }
                },
                "id"),

            CreateTool("list_progress", "List all progress entries (measurements) for a specific client.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" }
                },
                "client_id"),

            CreateTool("get_progress_entry", "Get detailed information about a specific progress entry including measurements.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The progress entry ID" }
                },
                "id"),

            CreateTool("list_users", "List system users (nutritionists, admins) with optional filters.",
                new Dictionary<string, object>
                {
                    ["search"] = new { type = "string", description = "Search term to filter by name or email" },
                    ["role"] = new { type = "string", description = "Filter by role: Admin, Nutritionist" },
                    ["is_active"] = new { type = "boolean", description = "Filter by active status" }
                }),

            CreateTool("get_user", "Get detailed information about a specific user by ID.",
                new Dictionary<string, object>
                {
                    ["user_id"] = new { type = "string", description = "The user ID (GUID string)" }
                },
                "user_id"),

            CreateTool("search", "Search across all entities (clients, appointments, meal plans) by keyword.",
                new Dictionary<string, object>
                {
                    ["query"] = new { type = "string", description = "The search query" }
                },
                "query"),

            CreateTool("get_dashboard", "Get dashboard overview: metrics, today's appointments, weekly appointment count, and active meal plan count.",
                new Dictionary<string, object>()),
        ];
    }

    private static Tool CreateTool(string name, string description, Dictionary<string, object> properties, params string[] required)
    {
        var schemaDict = new Dictionary<string, JsonElement>
        {
            ["type"] = JsonSerializer.SerializeToElement("object"),
            ["properties"] = JsonSerializer.SerializeToElement(properties),
            ["required"] = JsonSerializer.SerializeToElement(required),
        };

        return new Tool
        {
            Name = name,
            Description = description,
            InputSchema = InputSchema.FromRawUnchecked(schemaDict),
        };
    }

    // --- Tool Handlers ---

    private async Task<string> HandleListClients(JsonElement input)
    {
        var searchTerm = GetOptionalString(input, "search_term");
        var clients = await _clientService.GetListAsync(searchTerm);
        return JsonSerializer.Serialize(new { count = clients.Count, clients }, SerializerOptions);
    }

    private async Task<string> HandleGetClient(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var client = await _clientService.GetByIdAsync(id);
        return client is null
            ? JsonSerializer.Serialize(new { error = $"Client with ID {id} not found" })
            : JsonSerializer.Serialize(client, SerializerOptions);
    }

    private async Task<string> HandleListAppointments(JsonElement input)
    {
        DateTime? fromDate = GetOptionalDate(input, "from_date");
        DateTime? toDate = GetOptionalDate(input, "to_date");
        int? clientId = GetOptionalInt(input, "client_id");
        AppointmentStatus? status = GetOptionalEnum<AppointmentStatus>(input, "status");

        var appointments = await _appointmentService.GetListAsync(fromDate, toDate, clientId, status);
        return JsonSerializer.Serialize(new { count = appointments.Count, appointments }, SerializerOptions);
    }

    private async Task<string> HandleGetAppointment(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var appointment = await _appointmentService.GetByIdAsync(id);
        return appointment is null
            ? JsonSerializer.Serialize(new { error = $"Appointment with ID {id} not found" })
            : JsonSerializer.Serialize(appointment, SerializerOptions);
    }

    private async Task<string> HandleListMealPlans(JsonElement input)
    {
        int? clientId = GetOptionalInt(input, "client_id");
        MealPlanStatus? status = GetOptionalEnum<MealPlanStatus>(input, "status");

        var plans = await _mealPlanService.GetListAsync(clientId, status);
        return JsonSerializer.Serialize(new { count = plans.Count, meal_plans = plans }, SerializerOptions);
    }

    private async Task<string> HandleGetMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var plan = await _mealPlanService.GetByIdAsync(id);
        return plan is null
            ? JsonSerializer.Serialize(new { error = $"Meal plan with ID {id} not found" })
            : JsonSerializer.Serialize(plan, SerializerOptions);
    }

    private async Task<string> HandleListGoals(JsonElement input)
    {
        var clientId = GetRequiredInt(input, "client_id");
        var goals = await _progressService.GetGoalsByClientAsync(clientId);
        return JsonSerializer.Serialize(new { count = goals.Count, goals }, SerializerOptions);
    }

    private async Task<string> HandleGetGoal(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var goal = await _progressService.GetGoalByIdAsync(id);
        return goal is null
            ? JsonSerializer.Serialize(new { error = $"Goal with ID {id} not found" })
            : JsonSerializer.Serialize(goal, SerializerOptions);
    }

    private async Task<string> HandleListProgress(JsonElement input)
    {
        var clientId = GetRequiredInt(input, "client_id");
        var entries = await _progressService.GetEntriesByClientAsync(clientId);
        return JsonSerializer.Serialize(new { count = entries.Count, entries }, SerializerOptions);
    }

    private async Task<string> HandleGetProgressEntry(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var entry = await _progressService.GetEntryByIdAsync(id);
        return entry is null
            ? JsonSerializer.Serialize(new { error = $"Progress entry with ID {id} not found" })
            : JsonSerializer.Serialize(entry, SerializerOptions);
    }

    private async Task<string> HandleListUsers(JsonElement input)
    {
        var search = GetOptionalString(input, "search");
        var role = GetOptionalString(input, "role");
        bool? isActive = GetOptionalBool(input, "is_active");

        var users = await _userManagementService.GetUsersAsync(search, role, isActive);
        return JsonSerializer.Serialize(new { count = users.Count, users }, SerializerOptions);
    }

    private async Task<string> HandleGetUser(JsonElement input)
    {
        var userId = GetRequiredString(input, "user_id");
        var user = await _userManagementService.GetUserByIdAsync(userId);
        return user is null
            ? JsonSerializer.Serialize(new { error = $"User with ID {userId} not found" })
            : JsonSerializer.Serialize(user, SerializerOptions);
    }

    private async Task<string> HandleSearch(JsonElement input)
    {
        var query = GetRequiredString(input, "query");
        var results = await _searchService.SearchAsync(query, "", isAdmin: true, maxPerGroup: 5);
        return JsonSerializer.Serialize(results, SerializerOptions);
    }

    private async Task<string> HandleGetDashboard(JsonElement _)
    {
        // Sequential calls — EF Core DbContext is not thread-safe
        var metrics = await _dashboardService.GetMetricsAsync();
        var todaysAppointments = await _dashboardService.GetTodaysAppointmentsAsync();
        var weekCount = await _dashboardService.GetThisWeekAppointmentCountAsync();
        var activeMealPlanCount = await _dashboardService.GetActiveMealPlanCountAsync();

        return JsonSerializer.Serialize(new
        {
            metrics,
            todays_appointments = new { count = todaysAppointments.Count, appointments = todaysAppointments },
            this_week_appointment_count = weekCount,
            active_meal_plan_count = activeMealPlanCount,
        }, SerializerOptions);
    }

    // --- Helpers ---

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };

    private static string? GetOptionalString(JsonElement input, string property)
    {
        return input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;
    }

    private static string GetRequiredString(JsonElement input, string property)
    {
        if (input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
            return val.GetString()!;
        throw new ArgumentException($"Required parameter '{property}' is missing or not a string");
    }

    private static int GetRequiredInt(JsonElement input, string property)
    {
        if (input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        throw new ArgumentException($"Required parameter '{property}' is missing or not a number");
    }

    private static int? GetOptionalInt(JsonElement input, string property)
    {
        return input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number
            ? val.GetInt32()
            : null;
    }

    private static bool? GetOptionalBool(JsonElement input, string property)
    {
        if (input.TryGetProperty(property, out var val))
        {
            if (val.ValueKind == JsonValueKind.True) return true;
            if (val.ValueKind == JsonValueKind.False) return false;
        }
        return null;
    }

    private static DateTime? GetOptionalDate(JsonElement input, string property)
    {
        var str = GetOptionalString(input, property);
        if (str is null) return null;
        return DateTime.TryParse(str, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    private static TEnum? GetOptionalEnum<TEnum>(JsonElement input, string property) where TEnum : struct, Enum
    {
        var str = GetOptionalString(input, property);
        return str is not null && Enum.TryParse<TEnum>(str, ignoreCase: true, out var val) ? val : null;
    }
}
