using System.Globalization;
using System.Text.Json;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
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

    private string? _currentUserId;

    // --- Confirmation Tier Map ---

    private static readonly Dictionary<string, ConfirmationTier> ConfirmationTierMap = new()
    {
        // Standard tier — CRUD on domain entities
        ["create_client"] = ConfirmationTier.Standard,
        ["update_client"] = ConfirmationTier.Standard,
        ["delete_client"] = ConfirmationTier.Standard,
        ["create_appointment"] = ConfirmationTier.Standard,
        ["update_appointment"] = ConfirmationTier.Standard,
        ["cancel_appointment"] = ConfirmationTier.Standard,
        ["delete_appointment"] = ConfirmationTier.Standard,
        ["create_meal_plan"] = ConfirmationTier.Standard,
        ["update_meal_plan"] = ConfirmationTier.Standard,
        ["activate_meal_plan"] = ConfirmationTier.Standard,
        ["archive_meal_plan"] = ConfirmationTier.Standard,
        ["duplicate_meal_plan"] = ConfirmationTier.Standard,
        ["delete_meal_plan"] = ConfirmationTier.Standard,
        ["create_goal"] = ConfirmationTier.Standard,
        ["update_goal"] = ConfirmationTier.Standard,
        ["achieve_goal"] = ConfirmationTier.Standard,
        ["abandon_goal"] = ConfirmationTier.Standard,
        ["delete_goal"] = ConfirmationTier.Standard,
        ["create_progress_entry"] = ConfirmationTier.Standard,
        ["delete_progress_entry"] = ConfirmationTier.Standard,

        // Elevated tier — user/identity management
        ["create_user"] = ConfirmationTier.Elevated,
        ["change_user_role"] = ConfirmationTier.Elevated,
        ["deactivate_user"] = ConfirmationTier.Elevated,
        ["reactivate_user"] = ConfirmationTier.Elevated,
        ["reset_user_password"] = ConfirmationTier.Elevated,
    };

    public ConfirmationTier? GetConfirmationTier(string toolName)
    {
        return ConfirmationTierMap.TryGetValue(toolName, out var tier) ? tier : null;
    }

    // --- Description Builder ---

    public async Task<(string Description, EntityContext? Entity)> BuildConfirmationDescriptionAsync(string toolName, JsonElement input)
    {
        try
        {
            return toolName switch
            {
                "create_client" => BuildCreateClientContext(input),
                "update_client" => await BuildClientContext("update", input),
                "delete_client" => await BuildClientContext("delete", input),

                "create_appointment" => await BuildCreateAppointmentContext(input),
                "update_appointment" => await BuildAppointmentContext("update", input),
                "cancel_appointment" => await BuildAppointmentContext("cancel", input),
                "delete_appointment" => await BuildAppointmentContext("delete", input),

                "create_meal_plan" => await BuildCreateMealPlanContext(input),
                "update_meal_plan" => await BuildMealPlanContext("update", input),
                "activate_meal_plan" => await BuildMealPlanContext("activate", input),
                "archive_meal_plan" => await BuildMealPlanContext("archive", input),
                "duplicate_meal_plan" => await BuildMealPlanContext("duplicate", input),
                "delete_meal_plan" => await BuildMealPlanContext("delete", input),

                "create_goal" => BuildCreateGoalContext(input),
                "update_goal" => await BuildGoalContext("update", input),
                "achieve_goal" => await BuildGoalContext("achieve", input),
                "abandon_goal" => await BuildGoalContext("abandon", input),
                "delete_goal" => await BuildGoalContext("delete", input),

                "create_progress_entry" => await BuildCreateProgressEntryContext(input),
                "delete_progress_entry" => await BuildDeleteProgressEntryContext(input),

                "create_user" => BuildCreateUserContext(input),
                "change_user_role" => await BuildUserContext("change_role", input),
                "deactivate_user" => await BuildUserContext("deactivate", input),
                "reactivate_user" => await BuildUserContext("reactivate", input),
                "reset_user_password" => await BuildUserContext("reset_password", input),

                _ => (toolName.Replace('_', ' '), null),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to build enriched confirmation description for {ToolName}, falling back to basic", toolName);
            return (BuildFallbackDescription(toolName, input), null);
        }
    }

    private static string BuildFallbackDescription(string toolName, JsonElement input)
    {
        var id = GetOptionalInt(input, "id")?.ToString() ?? GetOptionalString(input, "user_id");
        return id is not null ? $"{toolName.Replace('_', ' ')} #{id}" : toolName.Replace('_', ' ');
    }

    // --- Entity Context Builders ---

    private static string FormatFieldLabel(string snakeCaseName)
    {
        return string.Join(" ", snakeCaseName.Split('_').Select(w =>
            w.Length > 0 ? char.ToUpper(w[0]) + w[1..] : w));
    }

    private static List<FieldChange> BuildFieldChangesFromInput(JsonElement input, params string[] excludeKeys)
    {
        var exclude = new HashSet<string>(excludeKeys, StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldChange>();
        if (input.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in input.EnumerateObject())
            {
                if (!exclude.Contains(prop.Name))
                {
                    var value = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
                    fields.Add(new FieldChange(FormatFieldLabel(prop.Name), null, value));
                }
            }
        }
        return fields;
    }

    private static List<FieldChange> BuildUpdateFieldChanges(
        JsonElement input, Dictionary<string, string?> currentValues, params string[] excludeKeys)
    {
        var exclude = new HashSet<string>(excludeKeys, StringComparer.OrdinalIgnoreCase);
        var fields = new List<FieldChange>();
        if (input.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in input.EnumerateObject())
            {
                if (!exclude.Contains(prop.Name))
                {
                    var proposed = prop.Value.ValueKind == JsonValueKind.Null ? null : prop.Value.ToString();
                    currentValues.TryGetValue(prop.Name, out var current);
                    fields.Add(new FieldChange(FormatFieldLabel(prop.Name), current, proposed));
                }
            }
        }
        return fields;
    }

    private static (string Description, EntityContext Entity) BuildCreateClientContext(JsonElement input)
    {
        var firstName = GetOptionalString(input, "first_name") ?? "?";
        var lastName = GetOptionalString(input, "last_name") ?? "?";
        var displayName = $"{firstName} {lastName}";
        var desc = $"create a client named {displayName} (consent: {(GetOptionalBool(input, "consent_given") == true ? "confirmed" : "not confirmed")})";
        var fields = BuildFieldChangesFromInput(input);
        return (desc, new EntityContext("Client", "create", null, displayName, fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildClientContext(string verb, JsonElement input)
    {
        var id = GetOptionalInt(input, "id");
        var client = await ResolveClientAsync(id);
        var name = client is not null ? $"{client.FirstName} {client.LastName}" : null;
        var desc = name is not null
            ? $"{verb} client \"{name}\" (#{id})"
            : $"{verb} client #{id?.ToString() ?? "?"}";

        if (verb == "update" && client is not null)
        {
            var currentValues = new Dictionary<string, string?>
            {
                ["first_name"] = client.FirstName,
                ["last_name"] = client.LastName,
                ["email"] = client.Email,
                ["phone"] = client.Phone,
                ["date_of_birth"] = client.DateOfBirth?.ToString("yyyy-MM-dd"),
                ["notes"] = client.Notes,
            };
            var fields = BuildUpdateFieldChanges(input, currentValues, "id");
            desc = AppendChangedFields(desc, verb, input, "id");
            return (desc, new EntityContext("Client", verb, id, name ?? $"#{id}", fields));
        }

        if (verb == "delete" && client is not null)
        {
            var fields = new List<FieldChange>
            {
                new("Name", $"{client.FirstName} {client.LastName}", null),
                new("Email", client.Email, null),
            };
            return (desc, new EntityContext("Client", verb, id, name!, fields));
        }

        return (desc, new EntityContext("Client", verb, id, name ?? $"#{id}", null));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildCreateAppointmentContext(JsonElement input)
    {
        var clientId = GetOptionalInt(input, "client_id");
        var clientName = await ResolveClientNameAsync(clientId);
        var desc = clientName is not null
            ? $"create an appointment for \"{clientName}\" (client #{clientId})"
            : $"create an appointment for client #{clientId?.ToString() ?? "?"}";
        var fields = BuildFieldChangesFromInput(input);
        return (desc, new EntityContext("Appointment", "create", null, clientName is not null ? $"Appointment for {clientName}" : "New Appointment", fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildAppointmentContext(string verb, JsonElement input)
    {
        var id = GetOptionalInt(input, "id");
        var appt = await ResolveAppointmentAsync(id);
        string? label = null;
        string? displayName = null;
        if (appt is not null)
        {
            var clientName = $"{appt.ClientFirstName} {appt.ClientLastName}";
            var date = appt.StartTime.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            label = $"for \"{clientName}\" on {date}";
            displayName = $"{appt.Type} with {clientName} on {date}";
        }

        var desc = label is not null
            ? $"{verb} appointment {label} (#{id})"
            : $"{verb} appointment #{id?.ToString() ?? "?"}";

        if (verb == "update" && appt is not null)
        {
            var currentValues = new Dictionary<string, string?>
            {
                ["type"] = appt.Type.ToString(),
                ["status"] = appt.Status.ToString(),
                ["start_time"] = appt.StartTime.ToString("yyyy-MM-dd HH:mm"),
                ["duration_minutes"] = appt.DurationMinutes.ToString(),
                ["location"] = appt.Location.ToString(),
                ["virtual_meeting_url"] = appt.VirtualMeetingUrl,
                ["location_notes"] = appt.LocationNotes,
                ["notes"] = appt.Notes,
            };
            var fields = BuildUpdateFieldChanges(input, currentValues, "id");
            desc = AppendChangedFields(desc, verb, input, "id");
            return (desc, new EntityContext("Appointment", verb, id, displayName!, fields));
        }

        if ((verb == "delete" || verb == "cancel") && appt is not null)
        {
            var fields = new List<FieldChange>
            {
                new("Client", $"{appt.ClientFirstName} {appt.ClientLastName}", null),
                new("Date", appt.StartTime.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture), null),
                new("Type", appt.Type.ToString(), null),
                new("Status", appt.Status.ToString(), null),
            };
            return (desc, new EntityContext("Appointment", verb, id, displayName!, fields));
        }

        return (desc, new EntityContext("Appointment", verb, id, displayName ?? $"#{id}", null));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildCreateMealPlanContext(JsonElement input)
    {
        var clientId = GetOptionalInt(input, "client_id");
        var title = GetOptionalString(input, "title") ?? "?";
        var clientName = await ResolveClientNameAsync(clientId);
        var desc = clientName is not null
            ? $"create a meal plan \"{title}\" for \"{clientName}\" (client #{clientId})"
            : $"create a meal plan titled \"{title}\" for client #{clientId?.ToString() ?? "?"}";
        var fields = BuildFieldChangesFromInput(input);
        return (desc, new EntityContext("Meal Plan", "create", null, title, fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildMealPlanContext(string verb, JsonElement input)
    {
        var id = GetOptionalInt(input, "id");
        var plan = await ResolveMealPlanAsync(id);
        var title = plan?.Title;
        var desc = title is not null
            ? $"{verb} meal plan \"{title}\" (#{id})"
            : $"{verb} meal plan #{id?.ToString() ?? "?"}";

        if (verb == "update" && plan is not null)
        {
            var currentValues = new Dictionary<string, string?>
            {
                ["title"] = plan.Title,
                ["start_date"] = plan.StartDate?.ToString("yyyy-MM-dd"),
                ["end_date"] = plan.EndDate?.ToString("yyyy-MM-dd"),
                ["calorie_target"] = plan.CalorieTarget?.ToString(CultureInfo.InvariantCulture),
                ["protein_target_g"] = plan.ProteinTargetG?.ToString(CultureInfo.InvariantCulture),
                ["carbs_target_g"] = plan.CarbsTargetG?.ToString(CultureInfo.InvariantCulture),
                ["fat_target_g"] = plan.FatTargetG?.ToString(CultureInfo.InvariantCulture),
            };
            var fields = BuildUpdateFieldChanges(input, currentValues, "id");
            desc = AppendChangedFields(desc, verb, input, "id");
            return (desc, new EntityContext("Meal Plan", verb, id, title!, fields));
        }

        if (verb == "delete" && plan is not null)
        {
            var fields = new List<FieldChange>
            {
                new("Title", plan.Title, null),
                new("Status", plan.Status.ToString(), null),
                new("Client", $"{plan.ClientFirstName} {plan.ClientLastName}", null),
            };
            return (desc, new EntityContext("Meal Plan", verb, id, title!, fields));
        }

        return (desc, new EntityContext("Meal Plan", verb, id, title ?? $"#{id}", null));
    }

    private static (string Description, EntityContext Entity) BuildCreateGoalContext(JsonElement input)
    {
        var title = GetOptionalString(input, "title") ?? "?";
        var clientId = GetOptionalInt(input, "client_id");
        var desc = $"create a goal titled \"{title}\" for client #{clientId?.ToString() ?? "?"}";
        var fields = BuildFieldChangesFromInput(input);
        return (desc, new EntityContext("Goal", "create", null, title, fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildGoalContext(string verb, JsonElement input)
    {
        var id = GetOptionalInt(input, "id");
        var goal = await ResolveGoalAsync(id);
        var title = goal?.Title;
        var entityLabel = title is not null
            ? $"goal \"{title}\" (#{id})"
            : $"goal #{id?.ToString() ?? "?"}";

        var desc = verb switch
        {
            "achieve" => $"mark {entityLabel} as achieved",
            "abandon" => $"mark {entityLabel} as abandoned",
            _ => $"{verb} {entityLabel}"
        };

        if (verb == "update" && goal is not null)
        {
            var currentValues = new Dictionary<string, string?>
            {
                ["title"] = goal.Title,
                ["description"] = goal.Description,
                ["target_value"] = goal.TargetValue?.ToString(CultureInfo.InvariantCulture),
                ["target_unit"] = goal.TargetUnit,
                ["target_date"] = goal.TargetDate?.ToString("yyyy-MM-dd"),
            };
            var fields = BuildUpdateFieldChanges(input, currentValues, "id");
            desc = AppendChangedFields(desc, verb, input, "id");
            return (desc, new EntityContext("Goal", verb, id, title!, fields));
        }

        if (verb == "delete" && goal is not null)
        {
            var fields = new List<FieldChange>
            {
                new("Title", goal.Title, null),
                new("Type", goal.GoalType.ToString(), null),
                new("Status", goal.Status.ToString(), null),
            };
            return (desc, new EntityContext("Goal", verb, id, title!, fields));
        }

        return (desc, new EntityContext("Goal", verb, id, title ?? $"#{id}", null));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildCreateProgressEntryContext(JsonElement input)
    {
        var clientId = GetOptionalInt(input, "client_id");
        var clientName = await ResolveClientNameAsync(clientId);
        var desc = clientName is not null
            ? $"log a progress entry for \"{clientName}\" (client #{clientId})"
            : $"log a progress entry for client #{clientId?.ToString() ?? "?"}";
        var fields = BuildFieldChangesFromInput(input, "client_id");
        return (desc, new EntityContext("Progress Entry", "create", null, clientName is not null ? $"Entry for {clientName}" : "New Entry", fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildDeleteProgressEntryContext(JsonElement input)
    {
        var id = GetOptionalInt(input, "id");
        var entry = await ResolveProgressEntryAsync(id);
        string? label = null;
        string? displayName = null;
        if (entry is not null)
        {
            var clientName = $"{entry.ClientFirstName} {entry.ClientLastName}";
            var date = entry.EntryDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
            label = $"for \"{clientName}\" on {date}";
            displayName = $"Entry for {clientName} on {date}";
            var fields = new List<FieldChange>
            {
                new("Client", clientName, null),
                new("Date", date, null),
            };
            var desc = $"delete progress entry {label} (#{id})";
            return (desc, new EntityContext("Progress Entry", "delete", id, displayName, fields));
        }

        return ($"delete progress entry #{id?.ToString() ?? "?"}", new EntityContext("Progress Entry", "delete", id, $"#{id}", null));
    }

    private static (string Description, EntityContext Entity) BuildCreateUserContext(JsonElement input)
    {
        var firstName = GetOptionalString(input, "first_name") ?? "?";
        var lastName = GetOptionalString(input, "last_name") ?? "?";
        var displayName = $"{firstName} {lastName}";
        var desc = $"create a user account for {displayName}";
        var fields = BuildFieldChangesFromInput(input);
        return (desc, new EntityContext("User", "create", null, displayName, fields));
    }

    private async Task<(string Description, EntityContext? Entity)> BuildUserContext(string operation, JsonElement input)
    {
        var userId = GetOptionalString(input, "user_id");
        var user = await ResolveUserAsync(userId);
        var displayName = user?.DisplayName;
        var entityLabel = displayName is not null
            ? $"user \"{displayName}\""
            : $"user {userId ?? "?"}";

        var (verb, suffix) = operation switch
        {
            "change_role" => ("change role of", $"to {GetOptionalString(input, "new_role") ?? "?"}"),
            "deactivate" => ("deactivate", (string?)null),
            "reactivate" => ("reactivate", (string?)null),
            "reset_password" => ("reset password for", (string?)null),
            _ => (operation, (string?)null),
        };

        var desc = suffix is not null
            ? $"{verb} {entityLabel} {suffix}"
            : $"{verb} {entityLabel}";

        if (user is not null)
        {
            var fields = new List<FieldChange>
            {
                new("Name", user.DisplayName, null),
                new("Email", user.Email, null),
                new("Role", user.Role, operation == "change_role" ? GetOptionalString(input, "new_role") : null),
                new("Active", user.IsActive.ToString(), null),
            };
            return (desc, new EntityContext("User", operation, null, displayName!, fields));
        }

        return (desc, new EntityContext("User", operation, null, displayName ?? userId ?? "?", null));
    }

    // --- Entity Resolvers ---

    private async Task<ClientDto?> ResolveClientAsync(int? id)
    {
        if (id is null) return null;
        try { return await _clientService.GetByIdAsync(id.Value); }
        catch { return null; }
    }

    private async Task<string?> ResolveClientNameAsync(int? id)
    {
        var client = await ResolveClientAsync(id);
        return client is not null ? $"{client.FirstName} {client.LastName}" : null;
    }

    private async Task<AppointmentDto?> ResolveAppointmentAsync(int? id)
    {
        if (id is null) return null;
        try { return await _appointmentService.GetByIdAsync(id.Value); }
        catch { return null; }
    }

    private async Task<MealPlanDetailDto?> ResolveMealPlanAsync(int? id)
    {
        if (id is null) return null;
        try { return await _mealPlanService.GetByIdAsync(id.Value); }
        catch { return null; }
    }

    private async Task<ProgressGoalDetailDto?> ResolveGoalAsync(int? id)
    {
        if (id is null) return null;
        try { return await _progressService.GetGoalByIdAsync(id.Value); }
        catch { return null; }
    }

    private async Task<ProgressEntryDetailDto?> ResolveProgressEntryAsync(int? id)
    {
        if (id is null) return null;
        try { return await _progressService.GetEntryByIdAsync(id.Value); }
        catch { return null; }
    }

    private async Task<UserDetailDto?> ResolveUserAsync(string? userId)
    {
        if (userId is null) return null;
        try { return await _userManagementService.GetUserByIdAsync(userId); }
        catch { return null; }
    }

    // --- Changed Fields Helper ---

    private static string AppendChangedFields(string description, string verb, JsonElement input, params string[] excludeKeys)
    {
        if (verb != "update") return description;

        var fields = GetProvidedFields(input, excludeKeys);
        return fields.Count > 0
            ? $"{description} — changing: {string.Join(", ", fields)}"
            : description;
    }

    private static List<string> GetProvidedFields(JsonElement input, params string[] excludeKeys)
    {
        var exclude = new HashSet<string>(excludeKeys, StringComparer.OrdinalIgnoreCase);
        var fields = new List<string>();

        if (input.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in input.EnumerateObject())
            {
                if (!exclude.Contains(prop.Name))
                    fields.Add(prop.Name);
            }
        }

        return fields;
    }

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
            // Read tools
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

            // Write tools — Clients
            ["create_client"] = HandleCreateClient,
            ["update_client"] = HandleUpdateClient,
            ["delete_client"] = HandleDeleteClient,

            // Write tools — Appointments
            ["create_appointment"] = HandleCreateAppointment,
            ["update_appointment"] = HandleUpdateAppointment,
            ["cancel_appointment"] = HandleCancelAppointment,
            ["delete_appointment"] = HandleDeleteAppointment,

            // Write tools — Meal Plans
            ["create_meal_plan"] = HandleCreateMealPlan,
            ["update_meal_plan"] = HandleUpdateMealPlan,
            ["activate_meal_plan"] = HandleActivateMealPlan,
            ["archive_meal_plan"] = HandleArchiveMealPlan,
            ["duplicate_meal_plan"] = HandleDuplicateMealPlan,
            ["delete_meal_plan"] = HandleDeleteMealPlan,

            // Write tools — Goals
            ["create_goal"] = HandleCreateGoal,
            ["update_goal"] = HandleUpdateGoal,
            ["achieve_goal"] = HandleAchieveGoal,
            ["abandon_goal"] = HandleAbandonGoal,
            ["delete_goal"] = HandleDeleteGoal,

            // Write tools — Progress Entries
            ["create_progress_entry"] = HandleCreateProgressEntry,
            ["delete_progress_entry"] = HandleDeleteProgressEntry,

            // Write tools — User Management
            ["create_user"] = HandleCreateUser,
            ["change_user_role"] = HandleChangeUserRole,
            ["deactivate_user"] = HandleDeactivateUser,
            ["reactivate_user"] = HandleReactivateUser,
            ["reset_user_password"] = HandleResetUserPassword,
        };
    }

    public async Task<string> ExecuteAsync(string toolName, IReadOnlyDictionary<string, JsonElement> input, string? userId = null)
    {
        _currentUserId = userId;

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
            // --- Read Tools ---

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

            // --- Write Tools: Clients ---

            CreateTool("create_client", "Create a new client in the practice. IMPORTANT: Before calling this tool, you must explicitly ask the practitioner to confirm that the client has consented to their data being stored. Set consent_given to true only after the practitioner confirms.",
                new Dictionary<string, object>
                {
                    ["first_name"] = new { type = "string", description = "Client's first name" },
                    ["last_name"] = new { type = "string", description = "Client's last name" },
                    ["email"] = new { type = "string", description = "Client's email address" },
                    ["phone"] = new { type = "string", description = "Client's phone number" },
                    ["date_of_birth"] = new { type = "string", description = "Date of birth (yyyy-MM-dd)" },
                    ["primary_nutritionist_id"] = new { type = "string", description = "User ID of the primary nutritionist" },
                    ["notes"] = new { type = "string", description = "Optional notes about the client" },
                    ["consent_given"] = new { type = "boolean", description = "Whether the client has given consent for their data to be stored. Must be explicitly confirmed by the practitioner before setting to true." },
                },
                "first_name", "last_name", "primary_nutritionist_id", "consent_given"),

            CreateTool("update_client", "Update an existing client's information.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The client ID to update" },
                    ["first_name"] = new { type = "string", description = "Client's first name" },
                    ["last_name"] = new { type = "string", description = "Client's last name" },
                    ["email"] = new { type = "string", description = "Client's email address" },
                    ["phone"] = new { type = "string", description = "Client's phone number" },
                    ["date_of_birth"] = new { type = "string", description = "Date of birth (yyyy-MM-dd)" },
                    ["primary_nutritionist_id"] = new { type = "string", description = "User ID of the primary nutritionist" },
                    ["notes"] = new { type = "string", description = "Optional notes about the client" },
                },
                "id", "first_name", "last_name", "primary_nutritionist_id"),

            CreateTool("delete_client", "Soft-delete a client.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The client ID to delete" },
                },
                "id"),

            // --- Write Tools: Appointments ---

            CreateTool("create_appointment", "Create a new appointment.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" },
                    ["type"] = new { type = "string", description = "Appointment type: InitialConsultation, FollowUp, CheckIn" },
                    ["start_time"] = new { type = "string", description = "Start time (ISO 8601 UTC, e.g. 2025-06-15T10:00:00Z)" },
                    ["duration_minutes"] = new { type = "integer", description = "Duration in minutes" },
                    ["location"] = new { type = "string", description = "Location: InPerson, Virtual, Phone" },
                    ["virtual_meeting_url"] = new { type = "string", description = "URL for virtual meetings" },
                    ["location_notes"] = new { type = "string", description = "Notes about the location" },
                    ["notes"] = new { type = "string", description = "Appointment notes" },
                },
                "client_id", "type", "start_time", "duration_minutes", "location"),

            CreateTool("update_appointment", "Update an existing appointment.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The appointment ID" },
                    ["type"] = new { type = "string", description = "Appointment type: InitialConsultation, FollowUp, CheckIn" },
                    ["status"] = new { type = "string", description = "Status: Scheduled, Confirmed, Completed, NoShow, LateCancellation, Cancelled" },
                    ["start_time"] = new { type = "string", description = "Start time (ISO 8601 UTC)" },
                    ["duration_minutes"] = new { type = "integer", description = "Duration in minutes" },
                    ["location"] = new { type = "string", description = "Location: InPerson, Virtual, Phone" },
                    ["virtual_meeting_url"] = new { type = "string", description = "URL for virtual meetings" },
                    ["location_notes"] = new { type = "string", description = "Notes about the location" },
                    ["notes"] = new { type = "string", description = "Appointment notes" },
                },
                "id", "type", "status", "start_time", "duration_minutes", "location"),

            CreateTool("cancel_appointment", "Cancel an appointment.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The appointment ID to cancel" },
                    ["cancellation_reason"] = new { type = "string", description = "Reason for cancellation" },
                },
                "id"),

            CreateTool("delete_appointment", "Soft-delete an appointment.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The appointment ID to delete" },
                },
                "id"),

            // --- Write Tools: Meal Plans ---

            CreateTool("create_meal_plan", "Create a new meal plan for a client.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" },
                    ["title"] = new { type = "string", description = "Meal plan title" },
                    ["description"] = new { type = "string", description = "Meal plan description" },
                    ["start_date"] = new { type = "string", description = "Start date (yyyy-MM-dd)" },
                    ["end_date"] = new { type = "string", description = "End date (yyyy-MM-dd)" },
                    ["calorie_target"] = new { type = "number", description = "Daily calorie target" },
                    ["protein_target_g"] = new { type = "number", description = "Daily protein target in grams" },
                    ["carbs_target_g"] = new { type = "number", description = "Daily carbs target in grams" },
                    ["fat_target_g"] = new { type = "number", description = "Daily fat target in grams" },
                    ["notes"] = new { type = "string", description = "Notes for the meal plan" },
                    ["instructions"] = new { type = "string", description = "Instructions for the client" },
                    ["number_of_days"] = new { type = "integer", description = "Number of days in the plan" },
                },
                "client_id", "title", "number_of_days"),

            CreateTool("update_meal_plan", "Update an existing meal plan's metadata.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID" },
                    ["client_id"] = new { type = "integer", description = "The client ID" },
                    ["title"] = new { type = "string", description = "Meal plan title" },
                    ["description"] = new { type = "string", description = "Meal plan description" },
                    ["start_date"] = new { type = "string", description = "Start date (yyyy-MM-dd)" },
                    ["end_date"] = new { type = "string", description = "End date (yyyy-MM-dd)" },
                    ["calorie_target"] = new { type = "number", description = "Daily calorie target" },
                    ["protein_target_g"] = new { type = "number", description = "Daily protein target in grams" },
                    ["carbs_target_g"] = new { type = "number", description = "Daily carbs target in grams" },
                    ["fat_target_g"] = new { type = "number", description = "Daily fat target in grams" },
                    ["notes"] = new { type = "string", description = "Notes for the meal plan" },
                    ["instructions"] = new { type = "string", description = "Instructions for the client" },
                    ["number_of_days"] = new { type = "integer", description = "Number of days in the plan" },
                },
                "id", "client_id", "title", "number_of_days"),

            CreateTool("activate_meal_plan", "Set a meal plan's status to Active.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID" },
                },
                "id"),

            CreateTool("archive_meal_plan", "Archive a meal plan.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID" },
                },
                "id"),

            CreateTool("duplicate_meal_plan", "Create a copy of an existing meal plan.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID to duplicate" },
                },
                "id"),

            CreateTool("delete_meal_plan", "Soft-delete a meal plan.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The meal plan ID to delete" },
                },
                "id"),

            // --- Write Tools: Goals ---

            CreateTool("create_goal", "Create a new progress goal for a client.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" },
                    ["title"] = new { type = "string", description = "Goal title" },
                    ["description"] = new { type = "string", description = "Goal description" },
                    ["goal_type"] = new { type = "string", description = "Goal type: Weight, BodyComposition, Dietary, Custom" },
                    ["target_value"] = new { type = "number", description = "Target value" },
                    ["target_unit"] = new { type = "string", description = "Unit for the target value (e.g. kg, lbs, %)" },
                    ["target_date"] = new { type = "string", description = "Target date (yyyy-MM-dd)" },
                },
                "client_id", "title", "goal_type"),

            CreateTool("update_goal", "Update an existing progress goal.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The goal ID" },
                    ["title"] = new { type = "string", description = "Goal title" },
                    ["description"] = new { type = "string", description = "Goal description" },
                    ["goal_type"] = new { type = "string", description = "Goal type: Weight, BodyComposition, Dietary, Custom" },
                    ["target_value"] = new { type = "number", description = "Target value" },
                    ["target_unit"] = new { type = "string", description = "Unit for the target value" },
                    ["target_date"] = new { type = "string", description = "Target date (yyyy-MM-dd)" },
                },
                "id", "title", "goal_type"),

            CreateTool("achieve_goal", "Mark a goal as achieved.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The goal ID" },
                },
                "id"),

            CreateTool("abandon_goal", "Mark a goal as abandoned.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The goal ID" },
                },
                "id"),

            CreateTool("delete_goal", "Soft-delete a goal.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The goal ID to delete" },
                },
                "id"),

            // --- Write Tools: Progress Entries ---

            CreateTool("create_progress_entry", "Log a new progress entry with measurements for a client.",
                new Dictionary<string, object>
                {
                    ["client_id"] = new { type = "integer", description = "The client ID" },
                    ["entry_date"] = new { type = "string", description = "Entry date (yyyy-MM-dd)" },
                    ["notes"] = new { type = "string", description = "Notes for this entry" },
                    ["measurements"] = new
                    {
                        type = "array",
                        description = "List of measurements",
                        items = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                ["metric_type"] = new { type = "string", description = "Metric type: Weight, BodyFatPercentage, WaistCircumference, HipCircumference, BMI, BloodPressureSystolic, BloodPressureDiastolic, RestingHeartRate, Custom" },
                                ["custom_metric_name"] = new { type = "string", description = "Custom metric name (required if metric_type is Custom)" },
                                ["value"] = new { type = "number", description = "Measurement value" },
                                ["unit"] = new { type = "string", description = "Unit of measurement (e.g. kg, lbs, cm, %)" },
                            },
                            required = new[] { "metric_type", "value" },
                        },
                    },
                },
                "client_id", "entry_date", "measurements"),

            CreateTool("delete_progress_entry", "Soft-delete a progress entry.",
                new Dictionary<string, object>
                {
                    ["id"] = new { type = "integer", description = "The progress entry ID to delete" },
                },
                "id"),

            // --- Write Tools: User Management ---

            CreateTool("create_user", "Create a new user account (nutritionist or admin).",
                new Dictionary<string, object>
                {
                    ["first_name"] = new { type = "string", description = "User's first name" },
                    ["last_name"] = new { type = "string", description = "User's last name" },
                    ["email"] = new { type = "string", description = "User's email address" },
                    ["role"] = new { type = "string", description = "Role: Admin, Nutritionist" },
                    ["password"] = new { type = "string", description = "Optional password (generated if not provided)" },
                },
                "first_name", "last_name", "email", "role"),

            CreateTool("change_user_role", "Change a user's role.",
                new Dictionary<string, object>
                {
                    ["user_id"] = new { type = "string", description = "The user ID" },
                    ["new_role"] = new { type = "string", description = "New role: Admin, Nutritionist" },
                },
                "user_id", "new_role"),

            CreateTool("deactivate_user", "Deactivate a user account.",
                new Dictionary<string, object>
                {
                    ["user_id"] = new { type = "string", description = "The user ID to deactivate" },
                },
                "user_id"),

            CreateTool("reactivate_user", "Reactivate a previously deactivated user account.",
                new Dictionary<string, object>
                {
                    ["user_id"] = new { type = "string", description = "The user ID to reactivate" },
                },
                "user_id"),

            CreateTool("reset_user_password", "Reset a user's password.",
                new Dictionary<string, object>
                {
                    ["user_id"] = new { type = "string", description = "The user ID" },
                    ["new_password"] = new { type = "string", description = "The new password" },
                },
                "user_id", "new_password"),
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

    // =====================================================
    // Read Tool Handlers
    // =====================================================

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

    // =====================================================
    // Write Tool Handlers — Clients
    // =====================================================

    private async Task<string> HandleCreateClient(JsonElement input)
    {
        var consentGiven = GetOptionalBool(input, "consent_given") ?? false;
        if (!consentGiven)
        {
            return JsonSerializer.Serialize(new
            {
                error = "consent_given must be true. You must ask the practitioner to confirm that the client has given consent for their data to be stored before creating a client record."
            }, SerializerOptions);
        }

        var dto = new ClientDto(
            Id: 0,
            FirstName: GetRequiredString(input, "first_name"),
            LastName: GetRequiredString(input, "last_name"),
            Email: GetOptionalString(input, "email"),
            Phone: GetOptionalString(input, "phone"),
            DateOfBirth: GetOptionalDateOnly(input, "date_of_birth"),
            PrimaryNutritionistId: GetRequiredString(input, "primary_nutritionist_id"),
            PrimaryNutritionistName: null,
            ConsentGiven: true,
            ConsentTimestamp: null,
            ConsentPolicyVersion: "1.0",
            Notes: GetOptionalString(input, "notes"),
            IsDeleted: false,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            DeletedAt: null);

        var result = await _clientService.CreateAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, client = result }, SerializerOptions);
    }

    private async Task<string> HandleUpdateClient(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var dto = new ClientDto(
            Id: id,
            FirstName: GetRequiredString(input, "first_name"),
            LastName: GetRequiredString(input, "last_name"),
            Email: GetOptionalString(input, "email"),
            Phone: GetOptionalString(input, "phone"),
            DateOfBirth: GetOptionalDateOnly(input, "date_of_birth"),
            PrimaryNutritionistId: GetRequiredString(input, "primary_nutritionist_id"),
            PrimaryNutritionistName: null,
            ConsentGiven: false,
            ConsentTimestamp: null,
            ConsentPolicyVersion: null,
            Notes: GetOptionalString(input, "notes"),
            IsDeleted: false,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            DeletedAt: null);

        var success = await _clientService.UpdateAsync(id, dto, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Client #{id} updated" })
            : JsonSerializer.Serialize(new { error = $"Client #{id} not found or update failed" });
    }

    private async Task<string> HandleDeleteClient(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _clientService.SoftDeleteAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Client #{id} deleted" })
            : JsonSerializer.Serialize(new { error = $"Client #{id} not found or delete failed" });
    }

    // =====================================================
    // Write Tool Handlers — Appointments
    // =====================================================

    private async Task<string> HandleCreateAppointment(JsonElement input)
    {
        var dto = new CreateAppointmentDto(
            ClientId: GetRequiredInt(input, "client_id"),
            Type: GetRequiredEnum<AppointmentType>(input, "type"),
            StartTime: GetRequiredDate(input, "start_time"),
            DurationMinutes: GetRequiredInt(input, "duration_minutes"),
            Location: GetRequiredEnum<AppointmentLocation>(input, "location"),
            VirtualMeetingUrl: GetOptionalString(input, "virtual_meeting_url"),
            LocationNotes: GetOptionalString(input, "location_notes"),
            Notes: GetOptionalString(input, "notes"));

        var result = await _appointmentService.CreateAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, appointment = result }, SerializerOptions);
    }

    private async Task<string> HandleUpdateAppointment(JsonElement input)
    {
        var dto = new UpdateAppointmentDto(
            Id: GetRequiredInt(input, "id"),
            Type: GetRequiredEnum<AppointmentType>(input, "type"),
            Status: GetRequiredEnum<AppointmentStatus>(input, "status"),
            StartTime: GetRequiredDate(input, "start_time"),
            DurationMinutes: GetRequiredInt(input, "duration_minutes"),
            Location: GetRequiredEnum<AppointmentLocation>(input, "location"),
            VirtualMeetingUrl: GetOptionalString(input, "virtual_meeting_url"),
            LocationNotes: GetOptionalString(input, "location_notes"),
            Notes: GetOptionalString(input, "notes"));

        var success = await _appointmentService.UpdateAsync(dto, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Appointment #{dto.Id} updated" })
            : JsonSerializer.Serialize(new { error = $"Appointment #{dto.Id} not found or update failed" });
    }

    private async Task<string> HandleCancelAppointment(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var reason = GetOptionalString(input, "cancellation_reason");
        var success = await _appointmentService.UpdateStatusAsync(id, AppointmentStatus.Cancelled, _currentUserId ?? "system", reason);
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Appointment #{id} cancelled" })
            : JsonSerializer.Serialize(new { error = $"Appointment #{id} not found or cancellation failed" });
    }

    private async Task<string> HandleDeleteAppointment(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _appointmentService.SoftDeleteAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Appointment #{id} deleted" })
            : JsonSerializer.Serialize(new { error = $"Appointment #{id} not found or delete failed" });
    }

    // =====================================================
    // Write Tool Handlers — Meal Plans
    // =====================================================

    private async Task<string> HandleCreateMealPlan(JsonElement input)
    {
        var dto = new CreateMealPlanDto(
            ClientId: GetRequiredInt(input, "client_id"),
            Title: GetRequiredString(input, "title"),
            Description: GetOptionalString(input, "description"),
            StartDate: GetOptionalDateOnly(input, "start_date"),
            EndDate: GetOptionalDateOnly(input, "end_date"),
            CalorieTarget: GetOptionalDecimal(input, "calorie_target"),
            ProteinTargetG: GetOptionalDecimal(input, "protein_target_g"),
            CarbsTargetG: GetOptionalDecimal(input, "carbs_target_g"),
            FatTargetG: GetOptionalDecimal(input, "fat_target_g"),
            Notes: GetOptionalString(input, "notes"),
            Instructions: GetOptionalString(input, "instructions"),
            NumberOfDays: GetRequiredInt(input, "number_of_days"));

        var result = await _mealPlanService.CreateAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, meal_plan = result }, SerializerOptions);
    }

    private async Task<string> HandleUpdateMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var dto = new CreateMealPlanDto(
            ClientId: GetRequiredInt(input, "client_id"),
            Title: GetRequiredString(input, "title"),
            Description: GetOptionalString(input, "description"),
            StartDate: GetOptionalDateOnly(input, "start_date"),
            EndDate: GetOptionalDateOnly(input, "end_date"),
            CalorieTarget: GetOptionalDecimal(input, "calorie_target"),
            ProteinTargetG: GetOptionalDecimal(input, "protein_target_g"),
            CarbsTargetG: GetOptionalDecimal(input, "carbs_target_g"),
            FatTargetG: GetOptionalDecimal(input, "fat_target_g"),
            Notes: GetOptionalString(input, "notes"),
            Instructions: GetOptionalString(input, "instructions"),
            NumberOfDays: GetRequiredInt(input, "number_of_days"));

        var success = await _mealPlanService.UpdateMetadataAsync(id, dto, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Meal plan #{id} updated" })
            : JsonSerializer.Serialize(new { error = $"Meal plan #{id} not found or update failed" });
    }

    private async Task<string> HandleActivateMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _mealPlanService.UpdateStatusAsync(id, MealPlanStatus.Active, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Meal plan #{id} activated" })
            : JsonSerializer.Serialize(new { error = $"Meal plan #{id} not found or activation failed" });
    }

    private async Task<string> HandleArchiveMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _mealPlanService.UpdateStatusAsync(id, MealPlanStatus.Archived, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Meal plan #{id} archived" })
            : JsonSerializer.Serialize(new { error = $"Meal plan #{id} not found or archive failed" });
    }

    private async Task<string> HandleDuplicateMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _mealPlanService.DuplicateAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Meal plan #{id} duplicated" })
            : JsonSerializer.Serialize(new { error = $"Meal plan #{id} not found or duplication failed" });
    }

    private async Task<string> HandleDeleteMealPlan(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _mealPlanService.SoftDeleteAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Meal plan #{id} deleted" })
            : JsonSerializer.Serialize(new { error = $"Meal plan #{id} not found or delete failed" });
    }

    // =====================================================
    // Write Tool Handlers — Goals
    // =====================================================

    private async Task<string> HandleCreateGoal(JsonElement input)
    {
        var dto = new CreateProgressGoalDto(
            ClientId: GetRequiredInt(input, "client_id"),
            Title: GetRequiredString(input, "title"),
            Description: GetOptionalString(input, "description"),
            GoalType: GetRequiredEnum<GoalType>(input, "goal_type"),
            TargetValue: GetOptionalDecimal(input, "target_value"),
            TargetUnit: GetOptionalString(input, "target_unit"),
            TargetDate: GetOptionalDateOnly(input, "target_date"));

        var result = await _progressService.CreateGoalAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, goal = result }, SerializerOptions);
    }

    private async Task<string> HandleUpdateGoal(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var dto = new UpdateProgressGoalDto(
            Title: GetRequiredString(input, "title"),
            Description: GetOptionalString(input, "description"),
            GoalType: GetRequiredEnum<GoalType>(input, "goal_type"),
            TargetValue: GetOptionalDecimal(input, "target_value"),
            TargetUnit: GetOptionalString(input, "target_unit"),
            TargetDate: GetOptionalDateOnly(input, "target_date"));

        var success = await _progressService.UpdateGoalAsync(id, dto, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Goal #{id} updated" })
            : JsonSerializer.Serialize(new { error = $"Goal #{id} not found or update failed" });
    }

    private async Task<string> HandleAchieveGoal(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _progressService.UpdateGoalStatusAsync(id, GoalStatus.Achieved, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Goal #{id} marked as achieved" })
            : JsonSerializer.Serialize(new { error = $"Goal #{id} not found or status update failed" });
    }

    private async Task<string> HandleAbandonGoal(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _progressService.UpdateGoalStatusAsync(id, GoalStatus.Abandoned, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Goal #{id} marked as abandoned" })
            : JsonSerializer.Serialize(new { error = $"Goal #{id} not found or status update failed" });
    }

    private async Task<string> HandleDeleteGoal(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _progressService.SoftDeleteGoalAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Goal #{id} deleted" })
            : JsonSerializer.Serialize(new { error = $"Goal #{id} not found or delete failed" });
    }

    // =====================================================
    // Write Tool Handlers — Progress Entries
    // =====================================================

    private async Task<string> HandleCreateProgressEntry(JsonElement input)
    {
        var measurements = new List<CreateProgressMeasurementDto>();

        if (input.TryGetProperty("measurements", out var measArray) && measArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in measArray.EnumerateArray())
            {
                measurements.Add(new CreateProgressMeasurementDto(
                    MetricType: GetRequiredEnum<MetricType>(m, "metric_type"),
                    CustomMetricName: GetOptionalString(m, "custom_metric_name"),
                    Value: GetRequiredDecimal(m, "value"),
                    Unit: GetOptionalString(m, "unit")));
            }
        }

        var dto = new CreateProgressEntryDto(
            ClientId: GetRequiredInt(input, "client_id"),
            EntryDate: GetRequiredDateOnly(input, "entry_date"),
            Notes: GetOptionalString(input, "notes"),
            Measurements: measurements);

        var result = await _progressService.CreateEntryAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, progress_entry = result }, SerializerOptions);
    }

    private async Task<string> HandleDeleteProgressEntry(JsonElement input)
    {
        var id = GetRequiredInt(input, "id");
        var success = await _progressService.SoftDeleteEntryAsync(id, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Progress entry #{id} deleted" })
            : JsonSerializer.Serialize(new { error = $"Progress entry #{id} not found or delete failed" });
    }

    // =====================================================
    // Write Tool Handlers — User Management
    // =====================================================

    private async Task<string> HandleCreateUser(JsonElement input)
    {
        var dto = new CreateUserDto(
            FirstName: GetRequiredString(input, "first_name"),
            LastName: GetRequiredString(input, "last_name"),
            Email: GetRequiredString(input, "email"),
            Role: GetRequiredString(input, "role"),
            Password: GetOptionalString(input, "password"));

        var result = await _userManagementService.CreateUserAsync(dto, _currentUserId ?? "system");
        return JsonSerializer.Serialize(new { success = true, user = result.User, generated_password = result.GeneratedPassword }, SerializerOptions);
    }

    private async Task<string> HandleChangeUserRole(JsonElement input)
    {
        var userId = GetRequiredString(input, "user_id");
        var newRole = GetRequiredString(input, "new_role");
        var success = await _userManagementService.ChangeRoleAsync(userId, newRole, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"User {userId} role changed to {newRole}" })
            : JsonSerializer.Serialize(new { error = $"User {userId} not found or role change failed" });
    }

    private async Task<string> HandleDeactivateUser(JsonElement input)
    {
        var userId = GetRequiredString(input, "user_id");
        var success = await _userManagementService.DeactivateAsync(userId, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"User {userId} deactivated" })
            : JsonSerializer.Serialize(new { error = $"User {userId} not found or deactivation failed" });
    }

    private async Task<string> HandleReactivateUser(JsonElement input)
    {
        var userId = GetRequiredString(input, "user_id");
        var success = await _userManagementService.ReactivateAsync(userId, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"User {userId} reactivated" })
            : JsonSerializer.Serialize(new { error = $"User {userId} not found or reactivation failed" });
    }

    private async Task<string> HandleResetUserPassword(JsonElement input)
    {
        var userId = GetRequiredString(input, "user_id");
        var newPassword = GetRequiredString(input, "new_password");
        var success = await _userManagementService.ResetPasswordAsync(userId, newPassword, _currentUserId ?? "system");
        return success
            ? JsonSerializer.Serialize(new { success = true, message = $"Password reset for user {userId}" })
            : JsonSerializer.Serialize(new { error = $"User {userId} not found or password reset failed" });
    }

    // =====================================================
    // Helpers
    // =====================================================

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

    private static decimal GetRequiredDecimal(JsonElement input, string property)
    {
        if (input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetDecimal();
        throw new ArgumentException($"Required parameter '{property}' is missing or not a number");
    }

    private static decimal? GetOptionalDecimal(JsonElement input, string property)
    {
        return input.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number
            ? val.GetDecimal()
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
        return DateTime.TryParse(str, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var dt) ? dt : null;
    }

    private static DateTime GetRequiredDate(JsonElement input, string property)
    {
        var str = GetRequiredString(input, property);
        if (DateTime.TryParse(str, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var dt))
            return dt;
        throw new ArgumentException($"Required parameter '{property}' is not a valid date/time");
    }

    private static DateOnly? GetOptionalDateOnly(JsonElement input, string property)
    {
        var str = GetOptionalString(input, property);
        if (str is null) return null;
        return DateOnly.TryParseExact(str, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d
            : null;
    }

    private static DateOnly GetRequiredDateOnly(JsonElement input, string property)
    {
        var str = GetRequiredString(input, property);
        if (DateOnly.TryParseExact(str, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        throw new ArgumentException($"Required parameter '{property}' is not a valid date (expected yyyy-MM-dd)");
    }

    private static TEnum? GetOptionalEnum<TEnum>(JsonElement input, string property) where TEnum : struct, Enum
    {
        var str = GetOptionalString(input, property);
        return str is not null && Enum.TryParse<TEnum>(str, ignoreCase: true, out var val) ? val : null;
    }

    private static TEnum GetRequiredEnum<TEnum>(JsonElement input, string property) where TEnum : struct, Enum
    {
        var str = GetRequiredString(input, property);
        if (Enum.TryParse<TEnum>(str, ignoreCase: true, out var val))
            return val;
        throw new ArgumentException($"Required parameter '{property}' is not a valid {typeof(TEnum).Name}");
    }
}
