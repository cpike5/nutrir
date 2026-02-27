# Nutrir AI Assistant — Specification

## Overview

The AI assistant is a collapsible right-side panel in the Blazor UI that accepts natural language requests and executes them against the Nutrir service layer. Users can ask the assistant to create clients, schedule appointments, look up progress records, and perform other platform operations without navigating the UI manually.

Compliance hardening items (retention purge, audit logging for AI data operations) are tracked separately in the [AI Conversation Data Policy](../compliance/ai-conversation-data-policy.md).

## Architecture

```
AiAssistantPanel.razor (InteractiveServer)
    ↓ @inject IAiAgentService
AiAgentService (Scoped — holds conversation history)
    ↓ Anthropic SDK CreateStreaming → IAsyncEnumerable<AgentStreamEvent>
    ↓ on tool_use → AiToolExecutor.ExecuteAsync(name, input)
AiToolExecutor (Scoped)
    ↓ Dictionary<string, Func<JsonElement, Task<string>>>
    ↓ calls IClientService, IAppointmentService, etc.
Existing Service Layer (unchanged)
```

### Component Responsibilities

| Component | Scope | Responsibility |
|-----------|-------|----------------|
| `AiPanelState` | Scoped | Holds open/closed/wide toggle state; fires `OnChange` event to notify subscribers |
| `AiAssistantPanel.razor` | InteractiveServer | Renders chat history, streams tokens to UI, handles user input, renders confirmation cards, entity link chips |
| `AiToggleButton.razor` | InteractiveServer | Button in TopBar that calls `AiPanelState.Toggle()` |
| `IAiAgentService` / `AiAgentService` | Scoped | Holds conversation history; sends requests to Anthropic API; yields `AgentStreamEvent` records; manages confirmation pause-and-resume; page context awareness |
| `AiToolExecutor` | Scoped | Dispatches tool calls by name to the correct service method; returns JSON strings; builds confirmation descriptions with entity name resolution |
| `IAiConversationStore` / `AiConversationStore` | Scoped | DB-backed conversation persistence with session expiry and message cap |
| `IAiRateLimiter` / `AiRateLimiter` | Singleton | Per-user rate limiting with minute and day windows |
| `IAiUsageTracker` / `AiUsageTracker` | Scoped | Logs token usage, tool calls, and request duration per API call |
| `IAiMarkdownRenderer` / `AiMarkdownRenderer` | Singleton | Renders markdown to HTML with entity link chips, tables, status badges, headers, blockquotes, code blocks |
| `IAiSuggestionService` / `AiSuggestionService` | Singleton | Provides conversation starters and context-aware typeahead suggestions |

The assistant never accesses the database directly. All data operations go through the existing service layer, preserving business rules, validation, and audit logging. No separate SignalR hub is needed — the Blazor Server circuit already provides the real-time channel.

## User Flow

1. User clicks the AI assistant icon in the TopBar to open the panel
2. User types a natural language request (e.g., "Create a new client named John Smith with celiac disease")
3. The authenticated user's context (userId, role, name) is captured server-side and injected into the system prompt
4. The system prompt, tool definitions, and conversation history are sent to the Anthropic API
5. The agent determines required tool calls and executes them sequentially against the service layer via the Tool Executor
6. Results stream back to the panel via `IAsyncEnumerable<AgentStreamEvent>` over the Blazor Server circuit
7. The panel renders the response with rich markdown and entity link chips (e.g., a chip linking to the new client's detail page)
8. For mutation operations, a confirmation dialog is shown inline before the tool call executes (see Permission Model)

## Tool Reference

### Read Tools (14)

Read tools execute immediately with no confirmation.

| Tool | Service Method | Key Parameters |
|------|---------------|----------------|
| `list_clients` | `IClientService.GetListAsync` | `search_term?` |
| `get_client` | `IClientService.GetByIdAsync` | `id` |
| `list_appointments` | `IAppointmentService.GetListAsync` | `from_date?`, `to_date?`, `client_id?`, `status?` |
| `get_appointment` | `IAppointmentService.GetByIdAsync` | `id` |
| `list_meal_plans` | `IMealPlanService.GetListAsync` | `client_id?`, `status?` |
| `get_meal_plan` | `IMealPlanService.GetByIdAsync` | `id` |
| `list_goals` | `IProgressService.GetGoalsByClientAsync` | `client_id` |
| `get_goal` | `IProgressService.GetGoalByIdAsync` | `id` |
| `list_progress` | `IProgressService.GetEntriesByClientAsync` | `client_id` |
| `get_progress_entry` | `IProgressService.GetEntryByIdAsync` | `id` |
| `list_users` | `IUserManagementService.GetUsersAsync` | `search?`, `role?`, `is_active?` |
| `get_user` | `IUserManagementService.GetUserByIdAsync` | `user_id` |
| `search` | `ISearchService.SearchAsync` | `query` |
| `get_dashboard` | Multiple `IDashboardService` methods | (none) |

### Standard Write Tools (19)

Standard tools cover create, update, delete, and status-change operations on client and clinical data. A bordered confirmation card with Allow and Deny buttons is shown before execution.

| Tool | Operation | Key Parameters |
|------|-----------|----------------|
| `create_client` | Create a new client record | `first_name`, `last_name`, `email?`, `phone?`, `date_of_birth?`, `primary_nutritionist_id`, `consent_given`, `notes?` |
| `update_client` | Update an existing client's details | `id`, `first_name?`, `last_name?`, `email?`, `phone?`, `primary_nutritionist_id?`, `notes?` |
| `delete_client` | Soft-delete a client record | `id` |
| `create_appointment` | Schedule a new appointment | `client_id`, `start_time`, `end_time`, `type`, `location_type`, `notes?` |
| `update_appointment` | Update appointment details | `id`, `start_time?`, `end_time?`, `type?`, `location_type?`, `notes?` |
| `cancel_appointment` | Cancel a scheduled appointment | `id`, `reason?` |
| `delete_appointment` | Delete an appointment record | `id` |
| `create_meal_plan` | Create a new meal plan | `client_id`, `title`, `description?` |
| `update_meal_plan` | Update meal plan metadata | `id`, `title?`, `description?` |
| `activate_meal_plan` | Set a meal plan to active status | `id` |
| `archive_meal_plan` | Archive an active meal plan | `id` |
| `duplicate_meal_plan` | Duplicate an existing meal plan | `id`, `new_title?` |
| `delete_meal_plan` | Delete a meal plan | `id` |
| `create_goal` | Create a new progress goal | `client_id`, `title`, `type`, `target_value?`, `target_date?`, `notes?` |
| `update_goal` | Update goal details | `id`, `title?`, `target_value?`, `target_date?`, `notes?` |
| `achieve_goal` | Mark a goal as achieved | `id` |
| `abandon_goal` | Mark a goal as abandoned | `id`, `reason?` |
| `delete_goal` | Delete a goal record | `id` |
| `create_progress_entry` | Record a progress measurement | `client_id`, `entry_date`, `measurements`, `notes?` |
| `delete_progress_entry` | Delete a progress entry | `id` |

### Elevated Write Tools (5)

Elevated tools cover user account management operations. A warning-styled confirmation card with a caution indicator is shown before execution to signal the increased risk.

| Tool | Operation | Key Parameters |
|------|-----------|----------------|
| `create_user` | Create a new practitioner account | `email`, `first_name`, `last_name`, `role` |
| `change_user_role` | Change a user's role | `user_id`, `new_role` |
| `deactivate_user` | Deactivate a user account | `user_id` |
| `reactivate_user` | Reactivate a deactivated account | `user_id` |
| `reset_user_password` | Send a password reset email | `user_id` |

### Deferred Tools

The following tools have not been implemented. They remain in the backlog for a future phase.

| Tool | Reason Deferred |
|------|----------------|
| `update_user` | Low value via chat interface; profile updates are better handled in the user settings UI |
| `list_audit` | Audit log querying via the assistant has not been prioritized; admin audit UI covers this need |
| `save_meal_plan_content` | Requires constructing deeply nested meal/day/item DTOs — impractical as a flat tool input schema |
| `update_progress_entry` | Service layer does not accept partial measurement patches |
| `force_mfa` | Edge case with limited demand; carries risk of locking users out if misused |

## Permission Model

The assistant uses a two-tier confirmation model. The tier determines the visual treatment of the confirmation card; the pause-and-resume mechanics are identical for both tiers.

| Tier | Operations | UI Treatment |
|------|-----------|--------------|
| No confirmation | All read tools: `list_*`, `get_*`, `search`, `get_dashboard` | Executes immediately, streams result |
| Standard | All client, appointment, meal plan, goal, and progress write tools | Bordered card with neutral styling, Allow and Deny buttons |
| Elevated | All user management write tools | Warning-styled card with caution icon, explicit action description, Allow and Deny buttons |

### Confirmation Flow

The confirmation flow uses a `TaskCompletionSource<bool>` per pending tool call, stored in a `ConcurrentDictionary<string, TaskCompletionSource<bool>>` keyed by tool call ID:

1. The agent loop in `AiAgentService` encounters a write tool call.
2. It yields an `AgentStreamEvent` with a `ConfirmationRequest` carrying the tool call ID, tool name, human-readable description, and tier.
3. The loop then awaits the `TaskCompletionSource`, which blocks the `IAsyncEnumerable` iteration.
4. `AiAssistantPanel.razor` receives the event and renders the confirmation card inline in the chat thread.
5. When the user clicks Allow or Deny, the panel calls `IAiAgentService.RespondToConfirmation(string toolCallId, bool allowed)`.
6. `RespondToConfirmation` looks up the `TaskCompletionSource` by tool call ID and calls `TrySetResult(allowed)`, unblocking the loop.
7. If approved, the audit source is set to `AiAssistant` and the tool executes. If denied, a denial result is returned to the model.

Closing the panel or cancelling the stream while a confirmation is pending triggers the cancellation token, which resolves the `TaskCompletionSource` via a registered callback.

### Confirmation Descriptions

Confirmation cards display human-readable descriptions built by `AiToolExecutor.BuildConfirmationDescriptionAsync`. These resolve entity names from IDs to show context like "delete client **Maria Santos** (#5)" rather than just "delete client #5". For update operations, changed fields are appended (e.g., "update client ... — changing: email, phone").

## Streaming

### AgentStreamEvent

```csharp
public enum ConfirmationTier { Standard, Elevated }

public record ToolConfirmationRequest(
    string ToolCallId,
    string ToolName,
    string Description,
    ConfirmationTier Tier
);

public record AgentStreamEvent
{
    public string? TextDelta { get; init; }
    public string? ToolName { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
    public ToolConfirmationRequest? ConfirmationRequest { get; init; }
}
```

The component checks which properties are set to determine event type:
- `TextDelta` non-null → append text to current assistant message
- `ToolName` non-null (no `ConfirmationRequest`) → show tool-in-progress spinner
- `ConfirmationRequest` non-null → render confirmation card, stream pauses
- `IsComplete` true → mark message complete
- `Error` non-null → display error block

### Stream Cancellation

The panel creates a `CancellationTokenSource` per `SendMessage()` call and passes the token through to `AiAgentService.SendMessageAsync()`. The token is threaded through to the agent loop and all tool calls. `Close()`, `ClearConversation()`, and `DisposeAsync()` all cancel the active token. `OperationCanceledException` is caught silently at the panel level.

### Agent Loop

The agent loop runs inside `AiAgentService.SendMessageAsync` and yields `AgentStreamEvent` records:

1. User message is appended to the in-memory conversation history.
2. `client.Messages.CreateStreaming(params)` is called with the tool list and full conversation history.
3. Text delta events are yielded as they arrive.
4. When the stream closes with `StopReason.ToolUse`, tool calls are dispatched (with confirmation gates for write tools), results are appended to history, and the loop continues.
5. When the stream closes with `StopReason.EndTurn`, a complete event is yielded and the loop exits.
6. A maximum of 10 tool-loop iterations is enforced to prevent runaway chains.

## Audit Source Tagging

All audit log entries include an `AuditSource` value (`Web`, `Cli`, `AiAssistant`). An ambient scoped `IAuditSourceProvider` carries the current source. `AiAgentService` sets it to `AiAssistant` immediately before executing an approved write tool, then resets to `Web` in a `finally` block. `AuditLogService` reads from the provider when creating entries — no method signatures change.

## Rich Markdown Rendering

`AiMarkdownRenderer` converts assistant response text to HTML with support for:

- **Entity link chips**: `[[type:id:display]]` syntax rendered as styled `<a>` chips that navigate via Blazor enhanced navigation. Supported types: `client`, `appointment`, `meal_plan`, `user`. Goals and progress entries are excluded (no standalone detail pages). Regex is applied after HTML encoding for XSS safety.
- **Tables**: Markdown tables converted to styled HTML tables with automatic status badges (green for Confirmed/Active/Completed, amber for Scheduled/Pending/Draft, red for Cancelled/No-show/Expired).
- **Code blocks**: Fenced and inline code.
- **Text formatting**: Bold, italic, headers (h1-h3), blockquotes, horizontal rules.
- **Lists**: Unordered and ordered lists with proper wrapping.

## Contextual Page Awareness

`IAiAgentService.SetPageContext(string uri)` parses the current URL and resolves entity context:

| URL Pattern | Resolved Context |
|-------------|-----------------|
| `/clients/{id}` | `entityType = "client"`, `entityId = id` |
| `/clients/{id}/progress` | `entityType = "client"`, `entityId = id` |
| `/appointments/{id}` | `entityType = "appointment"`, `entityId = id` |
| `/meal-plans/{id}` | `entityType = "meal_plan"`, `entityId = id` |
| `/admin/users/{guid}` | `entityType = "user"`, `entityId = guid` |

When context is set, the system prompt includes a "Current Page Context" section so the user can say "this client" instead of specifying an ID. Sub-paths correctly resolve to the parent entity. Pages with no recognized entity pattern clear the context.

The panel subscribes to `NavigationManager.LocationChanged` and calls `SetPageContext` on each navigation. The `AiSuggestionService` also uses the current entity type to provide context-aware typeahead suggestions.

## Conversation Persistence

### Entity Model

- **`AiConversation`** — One row per session. Tracks `UserId`, `CreatedAt`, `LastMessageAt`.
- **`AiConversationMessage`** — One row per `MessageParam`. Stores serialized API content (`ContentJson`) and extracted plain text (`DisplayText`) for UI reconstruction.

### Session Semantics

Sessions use an **8-hour idle expiry**. If `LastMessageAt` is older than 8 hours, a new session starts automatically. Conversations persist across page reloads and circuit reconnects within the same working day.

### Serialization Strategy

Messages are serialized using a custom `StoredContentBlock` DTO format (not direct SDK type serialization) for stable round-trip behavior across SDK version changes. Content types handled: text blocks, tool use blocks, tool result blocks, and plain strings.

### Message Cap

Maximum 100 messages per conversation (approximately 50 user-assistant exchanges). When exceeded, the oldest messages are trimmed via `ExecuteDeleteAsync`.

### History Restoration

On first render, `AiAssistantPanel` calls `LoadHistoryAsync()` which populates the in-memory conversation history from the stored session and returns display messages for UI reconstruction.

## Rate Limiting

Per-user limits enforced server-side in `AiRateLimiter` (Singleton):

| Limit | Default |
|-------|---------|
| Requests per minute | 30 |
| Requests per day | 500 |

Limits are tracked in-memory via a `ConcurrentDictionary<string, UserRateState>` with sliding windows that reset when their duration elapses. Stale entries (inactive > 1 hour) are cleaned up every 5 minutes.

### Configuration

```json
{
  "AiRateLimits": {
    "RequestsPerMinute": 30,
    "RequestsPerDay": 500
  }
}
```

## Usage Tracking

### Entity: `AiUsageLog`

One row per API call (per iteration of the tool loop): `UserId`, `RequestedAt`, `InputTokens`, `OutputTokens`, `ToolCallCount`, `DurationMs`, `Model`.

### Admin Page: `/admin/ai-usage`

- **Access**: Admin role only
- **Summary view**: Table showing each user's total requests, input/output tokens, and last active date.
- **Drill-down view**: Click a user to see daily breakdown with requests, tokens, tool calls, and average duration.
- **Date range filter**: Last 7, 30, or 90 days (default: 30).

## System Prompt

The system prompt is assembled at request time by `AiAgentService.BuildSystemPrompt()` and includes:

- **Date and user context**: Current date, authenticated user's name and role.
- **Practice context**: The assistant is helping a nutrition practice manage clients, appointments, meal plans, goals, and progress.
- **Capabilities summary**: Read and write operations across all domains, with consent requirement for client creation.
- **Data model reference**: Entity descriptions, enum values for types/statuses/locations, date format conventions.
- **Multi-step workflow guidance**: Instructions for chaining operations — look up by name before referencing IDs, confirm ambiguous matches.
- **Tool usage tips**: Prefer `get_dashboard` for daily overview, use full UTC timestamps for date filters, gather all required fields before write calls.
- **Response guidelines**: Concise and professional, use markdown tables/lists, include entity IDs, round nutritional values.
- **Entity reference syntax**: Instructions for `[[type:id:display]]` link format, only for tool-confirmed entities.
- **Page context**: When set, a section telling the model what entity the user is currently viewing.
- **Scope constraints**: The assistant should not perform operations outside the tool set or speculate about data it has not retrieved.
- **Confirmation acknowledgement**: When denied, acknowledge and stop.

## Configuration

### API Key

Development (user secrets):

```bash
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/Nutrir.Web
```

Docker (environment variable):

```yaml
environment:
  Anthropic__ApiKey: ${ANTHROPIC_API_KEY:-}
```

### appsettings.json

```json
{
  "Anthropic": {
    "ApiKey": "",
    "Model": "claude-haiku-4-5",
    "MaxTokens": 4096
  },
  "AiRateLimits": {
    "RequestsPerMinute": 30,
    "RequestsPerDay": 500
  }
}
```

### Options Classes

`AnthropicOptions`:

| Property | Default | Description |
|----------|---------|-------------|
| `ApiKey` | (required) | Anthropic API key |
| `Model` | `claude-haiku-4-5` | Model ID sent with every request |
| `MaxTokens` | `4096` | Max tokens per API response |

`AiRateLimitOptions`:

| Property | Default | Description |
|----------|---------|-------------|
| `RequestsPerMinute` | `30` | Maximum requests per user per minute |
| `RequestsPerDay` | `500` | Maximum requests per user per day |

## Technical Stack

| Component | Technology |
|-----------|-----------|
| UI panel | Blazor `InteractiveServer` render mode |
| Streaming transport | Blazor Server circuit (existing `_blazor` websocket) — no separate SignalR hub |
| LLM provider | Anthropic API (Claude Haiku 4.5 — fast, cost-effective for tool use) |
| .NET integration | Anthropic C# SDK with streaming via `CreateStreaming` |
| Service layer | Existing `IClientService`, `IAppointmentService`, etc. — no changes |
| Audit | Existing audit infrastructure; source tagged via ambient `IAuditSourceProvider` |
| Persistence | PostgreSQL via EF Core (`AiConversations`, `AiConversationMessages`, `AiUsageLogs` tables) |

## Key Files

### Core Interfaces

| File | Description |
|------|-------------|
| `src/Nutrir.Core/Interfaces/IAiAgentService.cs` | Service interface, `AgentStreamEvent` record, `ToolConfirmationRequest`, `ConfirmationTier` enum |
| `src/Nutrir.Core/Interfaces/IAiConversationStore.cs` | Conversation persistence interface with `ConversationSnapshot` record |
| `src/Nutrir.Core/Interfaces/IAiRateLimiter.cs` | Rate limiting interface |
| `src/Nutrir.Core/Interfaces/IAiUsageTracker.cs` | Usage tracking interface |
| `src/Nutrir.Core/Interfaces/IAiMarkdownRenderer.cs` | Markdown rendering interface |
| `src/Nutrir.Core/Interfaces/IAiSuggestionService.cs` | Suggestion provider interface |

### Infrastructure Services

| File | Description |
|------|-------------|
| `src/Nutrir.Infrastructure/Services/AiAgentService.cs` | Main agent orchestrator: conversation loop, tool execution, confirmation pausing, streaming, system prompt |
| `src/Nutrir.Infrastructure/Services/AiToolExecutor.cs` | 14 read + 24 write tool handlers, tool definitions, confirmation tier map, description builder with entity name resolution |
| `src/Nutrir.Infrastructure/Services/AiConversationStore.cs` | DB persistence with 8-hour session expiry, 100-message cap, custom serialization |
| `src/Nutrir.Infrastructure/Services/AiRateLimiter.cs` | In-memory per-user rate limiting with sliding windows |
| `src/Nutrir.Infrastructure/Services/AiUsageTracker.cs` | Per-request token/duration/tool-call logging |
| `src/Nutrir.Infrastructure/Services/AiMarkdownRenderer.cs` | Markdown → HTML with entity chips, tables, status badges |
| `src/Nutrir.Infrastructure/Services/AiSuggestionService.cs` | Conversation starters and context-aware typeahead suggestions |
| `src/Nutrir.Infrastructure/Configuration/AnthropicOptions.cs` | Strongly-typed options class |
| `src/Nutrir.Infrastructure/Configuration/AiRateLimitOptions.cs` | Rate limit configuration |

### UI Components

| File | Description |
|------|-------------|
| `src/Nutrir.Web/Components/Layout/AiAssistantPanel.razor` | Full chat panel: streaming, message history, confirmation cards, entity links, suggestions |
| `src/Nutrir.Web/Components/Layout/AiToggleButton.razor` | TopBar toggle button |
| `src/Nutrir.Web/Components/Layout/AiPanelState.cs` | Scoped panel state service with event notifications |
| `src/Nutrir.Web/Components/Pages/Admin/AiUsage.razor` | Admin usage dashboard |
| `src/Nutrir.Web/wwwroot/js/ai-panel.js` | JS interop helpers (localStorage persistence, auto-scroll, focus management) |

### Data Entities

| File | Description |
|------|-------------|
| `src/Nutrir.Core/Entities/AiConversation.cs` | Conversation session entity |
| `src/Nutrir.Core/Entities/AiConversationMessage.cs` | Message entity with `ContentJson` and `DisplayText` |
| `src/Nutrir.Core/Entities/AiUsageLog.cs` | Per-request usage metrics entity |

## Future Enhancements

- **Multi-step workflow templates** — Pre-built prompts for common workflows (new client onboarding, end-of-session notes, weekly review) accessible via slash commands
- **Structured output mode** — For batch operations, the assistant streams a structured progress indicator rather than prose
