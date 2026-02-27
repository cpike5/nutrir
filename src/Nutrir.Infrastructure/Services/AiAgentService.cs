using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Services;

public class AiAgentService : IAiAgentService
{
    private readonly AnthropicOptions _options;
    private readonly AiToolExecutor _toolExecutor;
    private readonly IAuditSourceProvider _auditSourceProvider;
    private readonly IAiConversationStore _conversationStore;
    private readonly IAiRateLimiter _rateLimiter;
    private readonly IAiUsageTracker _usageTracker;
    private readonly ITimeZoneService _timeZoneService;
    private readonly ILogger<AiAgentService> _logger;
    private readonly List<MessageParam> _conversationHistory = [];
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pendingConfirmations = new();

    private string _userName = "User";
    private string _userRole = "Unknown";
    private string? _userId;
    private string? _pageEntityType;
    private string? _pageEntityId;
    private bool _historyLoaded;

    private const int MaxToolLoopIterations = 10;

    private string BuildSystemPrompt() => $"""
        Today's date is {_timeZoneService.UserNow:yyyy-MM-dd (dddd)}. You are speaking with {_userName} ({_userRole}).

        You are Nutrir Assistant, an AI helper for a nutrition practice management application used by dietitians and nutritionists.

        ## Capabilities

        ### Read Operations
        - Use tools to look up real data before answering. Never guess or make up data.
        - If a search returns no results, say so clearly.

        ### Write Operations
        You can create, update, and delete data across all domains:
        - **Clients**: Create, update, and delete client records. **Consent requirement**: Before creating a new client, you MUST explicitly ask the practitioner to confirm that the client has given consent for their data to be stored. This is a regulatory/compliance requirement — do not skip it. Only set `consent_given: true` after the practitioner has explicitly confirmed consent.
        - **Appointments**: Create, update, cancel, and delete appointments
        - **Meal Plans**: Create, update metadata, activate, archive, duplicate, and delete meal plans (note: you cannot edit meal plan content — days, slots, and items — via chat)
        - **Goals**: Create, update, achieve, abandon, and delete progress goals
        - **Progress Entries**: Create and delete progress measurement entries
        - **Users** (elevated): Create users, change roles, deactivate/reactivate, reset passwords

        ### Confirmation Flow
        All write operations require user confirmation before execution. The user will see a confirmation dialog describing what you want to do. User management operations show an elevated (warning-styled) confirmation.
        - If the user **allows** the action, it will execute and you'll receive the result.
        - If the user **denies** the action, you'll receive a denial result. Acknowledge it gracefully and do not retry the same action. Ask if they'd like to do something different instead.

        ### Multi-Step Workflows
        When the user refers to entities by name rather than ID:
        1. First search for the entity using `search` or the appropriate `list_` tool
        2. Confirm you found the right entity with the user if ambiguous
        3. Then proceed with the write operation using the resolved ID

        ## Data Model Reference

        ### Entities
        - **Clients**: Patients/clients of the nutrition practice. Have name, email, phone, DOB, consent status, primary nutritionist.
        - **Appointments**: Scheduled sessions. Types: InitialConsultation, FollowUp, CheckIn. Statuses: Scheduled, Confirmed, Completed, NoShow, LateCancellation, Cancelled. Locations: InPerson, Virtual, Phone.
        - **Meal Plans**: Nutritional plans for clients. Statuses: Draft, Active, Archived. Contain days → meal slots (Breakfast, MorningSnack, Lunch, AfternoonSnack, Dinner, EveningSnack) → items with macros.
        - **Progress Goals**: Client goals. Types: Weight, BodyComposition, Dietary, Custom. Statuses: Active, Achieved, Abandoned.
        - **Progress Entries**: Measurement records with metrics like Weight, BodyFatPercentage, WaistCircumference, BMI, etc.
        - **Users**: System users (Admin, Nutritionist roles).

        ### Conventions
        - Dates use ISO 8601 format and en-CA locale
        - Date-only values use yyyy-MM-dd format
        - IDs are integers for most entities, GUIDs for users
        - "Today" means the current server date

        ## Tool Usage Tips
        - For "today's appointments" or general daily overview, prefer `get_dashboard` — it returns today's appointments with no parameters needed.
        - Use `list_appointments` for specific date ranges, client filters, or status filters. Always pass dates as full UTC timestamps (e.g. `2025-06-15T00:00:00Z`), not bare dates.
        - Use `search` for finding entities by name/keyword before drilling into details with a specific get tool.
        - For write operations, gather all required fields before calling the tool. Ask the user for missing required information rather than guessing.
        - When creating appointments, always resolve the client ID first if the user gives a name.
        - There is no undo/rollback capability. Inform the user if they ask to undo something.

        ## Response Guidelines
        - Be concise and professional
        - Format data in readable markdown tables or lists when showing multiple items
        - Include entity IDs for reference (e.g., "Client #3 - Maria Santos")
        - If results are empty, say so clearly ("No appointments found for today")
        - When showing appointments, include date, time, client name, type, and status
        - When showing clients, include name, email, and consent status
        - Round nutritional values to whole numbers
        - For multi-step lookups (e.g., "Tell me about Maria Santos"), use the search tool first, then get details with the specific ID
        - After a successful write operation, briefly confirm what was done (e.g., "Client #12 - Sarah Johnson has been created.")

        ## Entity References
        When you mention a specific entity by name after retrieving it via a tool, use this link format:
        - Clients: [[client:ID:Display Name]]
        - Appointments: [[appointment:ID:Display Name]]
        - Meal Plans: [[meal_plan:ID:Display Name]]
        - Users: [[user:ID:Display Name]]

        Examples:
        - "I found [[client:3:Maria Santos]], who has 2 upcoming appointments."
        - "Created [[appointment:15:Follow-up with Maria Santos on Jan 15]]."

        Only use this for entities confirmed via tool results. Never fabricate IDs.
        Do NOT use for goals or progress entries (no standalone detail pages).
        In tables, use selectively for the most relevant entities — not every row.
        {BuildPageContextPrompt()}
        """;

    private string BuildPageContextPrompt()
    {
        if (_pageEntityType is null || _pageEntityId is null)
            return "";

        return $"""

        ## Current Page Context
        The user is currently viewing a {_pageEntityType} detail page (ID: {_pageEntityId}).
        When they say "this client", "this appointment", etc., they mean {_pageEntityType} #{_pageEntityId}.
        Use this ID directly without searching — but confirm with the user if ambiguous.
        """;
    }

    public AiAgentService(
        IOptions<AnthropicOptions> options,
        AiToolExecutor toolExecutor,
        IAuditSourceProvider auditSourceProvider,
        IAiConversationStore conversationStore,
        IAiRateLimiter rateLimiter,
        IAiUsageTracker usageTracker,
        ITimeZoneService timeZoneService,
        ILogger<AiAgentService> logger)
    {
        _options = options.Value;
        _toolExecutor = toolExecutor;
        _auditSourceProvider = auditSourceProvider;
        _conversationStore = conversationStore;
        _rateLimiter = rateLimiter;
        _usageTracker = usageTracker;
        _timeZoneService = timeZoneService;
        _logger = logger;
    }

    public async IAsyncEnumerable<AgentStreamEvent> SendMessageAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            yield return new AgentStreamEvent { Error = "Anthropic API key is not configured. Please set the API key in user secrets or environment variables." };
            yield break;
        }

        // Rate limit check
        if (_userId is not null)
        {
            var (allowed, rateLimitMessage) = _rateLimiter.CheckAndRecord(_userId);
            if (!allowed)
            {
                yield return new AgentStreamEvent { Error = rateLimitMessage };
                yield break;
            }
        }

        await _timeZoneService.InitializeAsync();
        var overallStopwatch = Stopwatch.StartNew();

        _conversationHistory.Add(new MessageParam
        {
            Role = Role.User,
            Content = userMessage,
        });

        // Track new messages added during this exchange for persistence
        var newMessages = new List<MessageParam>();
        var displayTexts = new List<string?>();

        newMessages.Add(new MessageParam { Role = Role.User, Content = userMessage });
        displayTexts.Add(userMessage);

        var client = new AnthropicClient { ApiKey = _options.ApiKey };
        var tools = AiToolExecutor.GetToolDefinitions();

        int totalInputTokens = 0;
        int totalOutputTokens = 0;
        int totalToolCalls = 0;

        for (int iteration = 0; iteration < MaxToolLoopIterations; iteration++)
        {
            var parameters = new MessageCreateParams
            {
                Model = _options.Model,
                MaxTokens = _options.MaxTokens,
                System = BuildSystemPrompt(),
                Messages = _conversationHistory.ToArray(),
                Tools = tools.Select(t => (ToolUnion)t).ToArray(),
            };

            // Log message summary for debugging API errors
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                for (int m = 0; m < _conversationHistory.Count; m++)
                {
                    var h = _conversationHistory[m];
                    var contentTypes = "string";
                    if (h.Content.TryPickContentBlockParams(out var blocks))
                        contentTypes = string.Join("+", blocks.Select(b =>
                            b.TryPickText(out _) ? "text" :
                            b.TryPickToolUse(out _) ? "tool_use" :
                            b.TryPickToolResult(out _) ? "tool_result" : "unknown"));
                    _logger.LogDebug("messages[{Index}]: role={Role}, content=[{ContentTypes}]", m, h.Role, contentTypes);
                }
            }

            // Stream the response, collecting content blocks and yielding text deltas
            var result = await StreamAndCollectAsync(client, parameters, cancellationToken);

            totalInputTokens += result.InputTokens;
            totalOutputTokens += result.OutputTokens;

            if (result.Error is not null)
            {
                yield return new AgentStreamEvent { Error = result.Error };
                await SaveAndLogAsync(newMessages, displayTexts, totalInputTokens, totalOutputTokens, totalToolCalls, overallStopwatch);
                yield break;
            }

            // Yield any buffered text deltas
            foreach (var delta in result.TextDeltas)
            {
                yield return new AgentStreamEvent { TextDelta = delta };
            }

            // Add assistant response to conversation history
            var assistantMessage = new MessageParam
            {
                Role = Role.Assistant,
                Content = new List<ContentBlockParam>(result.ContentBlocks),
            };
            _conversationHistory.Add(assistantMessage);
            newMessages.Add(assistantMessage);

            // Extract display text from text content blocks
            var assistantText = string.Concat(result.TextDeltas);
            displayTexts.Add(string.IsNullOrEmpty(assistantText) ? null : assistantText);

            // Handle tool use
            if (result.StopReason == StopReason.ToolUse)
            {
                var toolResults = new List<ContentBlockParam>();

                foreach (var block in result.ContentBlocks)
                {
                    if (!block.TryPickToolUse(out var toolUse))
                        continue;

                    totalToolCalls++;
                    var confirmationTier = _toolExecutor.GetConfirmationTier(toolUse.Name);

                    if (confirmationTier is not null)
                    {
                        // Write tool — requires user confirmation
                        var inputElement = JsonSerializer.SerializeToElement(toolUse.Input);
                        var description = await _toolExecutor.BuildConfirmationDescriptionAsync(toolUse.Name, inputElement);

                        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        _pendingConfirmations[toolUse.ID] = tcs;

                        // Register cancellation to prevent leaked tasks
                        await using var ctr = cancellationToken.Register(() => tcs.TrySetCanceled());

                        yield return new AgentStreamEvent
                        {
                            ToolName = toolUse.Name,
                            ConfirmationRequest = new ToolConfirmationRequest(
                                toolUse.ID, toolUse.Name, description, confirmationTier.Value)
                        };

                        bool userAllowed;
                        try
                        {
                            userAllowed = await tcs.Task;
                        }
                        catch (OperationCanceledException)
                        {
                            _pendingConfirmations.TryRemove(toolUse.ID, out _);
                            await SaveAndLogAsync(newMessages, displayTexts, totalInputTokens, totalOutputTokens, totalToolCalls, overallStopwatch);
                            yield break;
                        }

                        _pendingConfirmations.TryRemove(toolUse.ID, out _);

                        if (!userAllowed)
                        {
                            _logger.LogInformation("Tool {ToolName} denied by user", toolUse.Name);
                            toolResults.Add(new ToolResultBlockParam(toolUse.ID)
                            {
                                Content = JsonSerializer.Serialize(new { status = "denied", message = "The user denied this action." }),
                            });
                            continue;
                        }

                        // Allowed — set audit source and execute
                        _auditSourceProvider.SetSource(AuditSource.AiAssistant);
                        try
                        {
                            _logger.LogInformation("Executing write tool {ToolName} (approved by user)", toolUse.Name);
                            var toolResult = await _toolExecutor.ExecuteAsync(toolUse.Name, toolUse.Input, _userId);

                            toolResults.Add(new ToolResultBlockParam(toolUse.ID)
                            {
                                Content = toolResult,
                            });
                        }
                        finally
                        {
                            _auditSourceProvider.SetSource(AuditSource.Web);
                        }
                    }
                    else
                    {
                        // Read tool — execute immediately
                        yield return new AgentStreamEvent { ToolName = toolUse.Name };

                        _logger.LogInformation("Executing tool {ToolName}", toolUse.Name);
                        var toolResult = await _toolExecutor.ExecuteAsync(toolUse.Name, toolUse.Input);

                        toolResults.Add(new ToolResultBlockParam(toolUse.ID)
                        {
                            Content = toolResult,
                        });
                    }
                }

                var toolResultMessage = new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>(toolResults),
                };
                _conversationHistory.Add(toolResultMessage);
                newMessages.Add(toolResultMessage);
                displayTexts.Add(null); // Tool results don't have display text

                continue;
            }

            // end_turn or max_tokens — done
            yield return new AgentStreamEvent { IsComplete = true };
            await SaveAndLogAsync(newMessages, displayTexts, totalInputTokens, totalOutputTokens, totalToolCalls, overallStopwatch);
            yield break;
        }

        yield return new AgentStreamEvent { Error = "Maximum tool call iterations reached. Please try a simpler question." };
        await SaveAndLogAsync(newMessages, displayTexts, totalInputTokens, totalOutputTokens, totalToolCalls, overallStopwatch);
    }

    public async Task<List<ChatDisplayMessage>?> LoadHistoryAsync()
    {
        if (_historyLoaded || _userId is null)
            return null;

        _historyLoaded = true;

        var snapshot = await _conversationStore.LoadActiveSessionAsync(_userId);
        if (snapshot is null)
            return null;

        _conversationHistory.Clear();
        foreach (var item in snapshot.History)
        {
            if (item is MessageParam mp)
                _conversationHistory.Add(mp);
        }
        return snapshot.DisplayMessages;
    }

    public async Task ClearHistoryAsync()
    {
        _conversationHistory.Clear();
        if (_userId is not null)
        {
            await _conversationStore.ClearHistoryAsync(_userId);
        }
    }

    private async Task SaveAndLogAsync(
        List<MessageParam> newMessages,
        List<string?> displayTexts,
        int inputTokens,
        int outputTokens,
        int toolCallCount,
        Stopwatch stopwatch)
    {
        if (_userId is null)
            return;

        try
        {
            await _conversationStore.SaveMessagesAsync(_userId, newMessages.Cast<object>().ToList(), displayTexts);
            await _usageTracker.LogAsync(_userId, inputTokens, outputTokens, toolCallCount, (int)stopwatch.ElapsedMilliseconds, _options.Model);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save conversation/usage data");
        }
    }

    public void SetUserContext(string userName, string userRole)
    {
        _userName = userName;
        _userRole = userRole;
    }

    public void SetUserId(string userId)
    {
        _userId = userId;
    }

    public void RespondToConfirmation(string toolCallId, bool allowed)
    {
        if (_pendingConfirmations.TryRemove(toolCallId, out var tcs))
        {
            tcs.TrySetResult(allowed);
        }
    }

    public void SetPageContext(string? entityType, string? entityId)
    {
        _pageEntityType = entityType;
        _pageEntityId = entityId;
    }

    /// <summary>
    /// Streams the API call, collects content blocks, and returns the aggregated result.
    /// Text deltas are buffered so they can be yielded by the caller (avoiding yield-in-try-catch).
    /// </summary>
    private async Task<StreamResult> StreamAndCollectAsync(
        AnthropicClient client,
        MessageCreateParams parameters,
        CancellationToken cancellationToken)
    {
        var textDeltas = new List<string>();
        var contentBlocks = new List<ContentBlockParam>();
        StopReason? stopReason = null;
        int inputTokens = 0;
        int outputTokens = 0;

        var currentTextBuilder = new System.Text.StringBuilder();
        string? currentToolId = null;
        string? currentToolName = null;
        var currentToolInputBuilder = new System.Text.StringBuilder();
        bool inTextBlock = false;
        bool inToolUseBlock = false;

        try
        {
            var stream = client.Messages.CreateStreaming(parameters);

            await foreach (var rawEvent in stream.WithCancellation(cancellationToken))
            {
                if (rawEvent.TryPickContentBlockStart(out var startEvent))
                {
                    FlushCurrentBlock(ref inTextBlock, ref inToolUseBlock, contentBlocks,
                        currentTextBuilder, currentToolId, currentToolName, currentToolInputBuilder);

                    if (startEvent.ContentBlock.TryPickText(out _))
                    {
                        inTextBlock = true;
                        currentTextBuilder.Clear();
                    }
                    else if (startEvent.ContentBlock.TryPickToolUse(out var toolUseStart))
                    {
                        inToolUseBlock = true;
                        currentToolId = toolUseStart.ID;
                        currentToolName = toolUseStart.Name;
                        currentToolInputBuilder.Clear();
                    }
                }
                else if (rawEvent.TryPickContentBlockDelta(out var deltaEvent))
                {
                    if (deltaEvent.Delta.TryPickText(out var textDelta))
                    {
                        currentTextBuilder.Append(textDelta.Text);
                        textDeltas.Add(textDelta.Text);
                    }
                    else if (deltaEvent.Delta.TryPickInputJson(out var inputJsonDelta))
                    {
                        currentToolInputBuilder.Append(inputJsonDelta.PartialJson);
                    }
                }
                else if (rawEvent.TryPickContentBlockStop(out _))
                {
                    FlushCurrentBlock(ref inTextBlock, ref inToolUseBlock, contentBlocks,
                        currentTextBuilder, currentToolId, currentToolName, currentToolInputBuilder);
                }
                else if (rawEvent.TryPickStart(out var startMessage))
                {
                    if (startMessage.Message?.Usage is { } usage)
                    {
                        inputTokens = (int)usage.InputTokens;
                    }
                }
                else if (rawEvent.TryPickDelta(out var messageDelta))
                {
                    if (messageDelta.Delta.StopReason is { } sr)
                    {
                        stopReason = sr;
                    }
                    if (messageDelta.Usage is { } deltaUsage)
                    {
                        outputTokens = (int)deltaUsage.OutputTokens;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Anthropic API");
            return new StreamResult { Error = $"Error communicating with AI service: {ex.Message}" };
        }

        // Flush any remaining block
        FlushCurrentBlock(ref inTextBlock, ref inToolUseBlock, contentBlocks,
            currentTextBuilder, currentToolId, currentToolName, currentToolInputBuilder);

        return new StreamResult
        {
            TextDeltas = textDeltas,
            ContentBlocks = contentBlocks,
            StopReason = stopReason,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
        };
    }

    private static void FlushCurrentBlock(
        ref bool inTextBlock,
        ref bool inToolUseBlock,
        List<ContentBlockParam> contentBlocks,
        System.Text.StringBuilder textBuilder,
        string? toolId,
        string? toolName,
        System.Text.StringBuilder toolInputBuilder)
    {
        if (inTextBlock)
        {
            if (textBuilder.Length > 0)
                contentBlocks.Add(new TextBlockParam { Text = textBuilder.ToString() });
            textBuilder.Clear();
            inTextBlock = false;
        }
        else if (inToolUseBlock && toolId is not null && toolName is not null)
        {
            var inputDict = toolInputBuilder.Length > 0
                ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolInputBuilder.ToString())
                    ?? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement>();

            contentBlocks.Add(new ToolUseBlockParam
            {
                ID = toolId,
                Name = toolName,
                Input = inputDict,
            });
            toolInputBuilder.Clear();
            inToolUseBlock = false;
        }
    }

    private sealed class StreamResult
    {
        public List<string> TextDeltas { get; init; } = [];
        public List<ContentBlockParam> ContentBlocks { get; init; } = [];
        public StopReason? StopReason { get; init; }
        public string? Error { get; init; }
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
    }
}
