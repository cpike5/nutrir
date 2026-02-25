using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Configuration;

namespace Nutrir.Infrastructure.Services;

public class AiAgentService : IAiAgentService
{
    private readonly AnthropicOptions _options;
    private readonly AiToolExecutor _toolExecutor;
    private readonly ILogger<AiAgentService> _logger;
    private readonly List<MessageParam> _conversationHistory = [];

    private string _userName = "User";
    private string _userRole = "Unknown";

    private const int MaxToolLoopIterations = 10;

    private string BuildSystemPrompt() => $"""
        Today's date is {DateTime.Now:yyyy-MM-dd (dddd)}. You are speaking with {_userName} ({_userRole}).

        You are Nutrir Assistant, an AI helper for a nutrition practice management application used by dietitians and nutritionists.

        ## Capabilities
        - You can ONLY READ data. You cannot create, update, or delete anything.
        - When asked to make changes (create appointments, update clients, delete meal plans, etc.), politely explain that write operations are coming in a future update and suggest what they can do in the UI instead.
        - Use tools to look up real data before answering. Never guess or make up data.
        - If a search returns no results, say so clearly.

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
        - IDs are integers for most entities, GUIDs for users
        - "Today" means the current server date

        ## Response Guidelines
        - Be concise and professional
        - Format data in readable markdown tables or lists when showing multiple items
        - Include entity IDs for reference (e.g., "Client #3 - Maria Santos")
        - If results are empty, say so clearly ("No appointments found for today")
        - When showing appointments, include date, time, client name, type, and status
        - When showing clients, include name, email, and consent status
        - Round nutritional values to whole numbers
        - For multi-step lookups (e.g., "Tell me about Maria Santos"), use the search tool first, then get details with the specific ID
        """;

    public AiAgentService(
        IOptions<AnthropicOptions> options,
        AiToolExecutor toolExecutor,
        ILogger<AiAgentService> logger)
    {
        _options = options.Value;
        _toolExecutor = toolExecutor;
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

        _conversationHistory.Add(new MessageParam
        {
            Role = Role.User,
            Content = userMessage,
        });

        var client = new AnthropicClient { ApiKey = _options.ApiKey };
        var tools = AiToolExecutor.GetToolDefinitions();

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

            // Stream the response, collecting content blocks and yielding text deltas
            var result = await StreamAndCollectAsync(client, parameters, cancellationToken);

            if (result.Error is not null)
            {
                yield return new AgentStreamEvent { Error = result.Error };
                yield break;
            }

            // Yield any buffered text deltas
            foreach (var delta in result.TextDeltas)
            {
                yield return new AgentStreamEvent { TextDelta = delta };
            }

            // Add assistant response to conversation history
            _conversationHistory.Add(new MessageParam
            {
                Role = Role.Assistant,
                Content = new List<ContentBlockParam>(result.ContentBlocks),
            });

            // Handle tool use
            if (result.StopReason == StopReason.ToolUse)
            {
                var toolResults = new List<ContentBlockParam>();

                foreach (var block in result.ContentBlocks)
                {
                    if (!block.TryPickToolUse(out var toolUse))
                        continue;

                    yield return new AgentStreamEvent { ToolName = toolUse.Name };

                    _logger.LogInformation("Executing tool {ToolName}", toolUse.Name);
                    var toolResult = await _toolExecutor.ExecuteAsync(toolUse.Name, toolUse.Input);

                    toolResults.Add(new ToolResultBlockParam(toolUse.ID)
                    {
                        Content = toolResult,
                    });
                }

                _conversationHistory.Add(new MessageParam
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>(toolResults),
                });

                continue;
            }

            // end_turn or max_tokens — done
            yield return new AgentStreamEvent { IsComplete = true };
            yield break;
        }

        yield return new AgentStreamEvent { Error = "Maximum tool call iterations reached. Please try a simpler question." };
    }

    public void ClearHistory()
    {
        _conversationHistory.Clear();
    }

    public void SetUserContext(string userName, string userRole)
    {
        _userName = userName;
        _userRole = userRole;
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
                else if (rawEvent.TryPickDelta(out var messageDelta))
                {
                    if (messageDelta.Delta.StopReason is { } sr)
                    {
                        stopReason = sr;
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
    }
}
