# AI Assistant — Phase 1 Implementation

## Overview

The AI assistant is a collapsible right-side chat panel in the Blazor UI that uses the Anthropic .NET SDK (v12.4.0) to answer read-only questions about practice data. Phase 1 is intentionally scoped to read-only operations — no mutations, no confirmation dialogs, no write tools.

The panel is toggled from the TopBar and communicates with the Anthropic API via an in-process scoped service. No SignalR hub is required because the Blazor InteractiveServer circuit already provides the real-time channel between browser and server.

## Architecture

```
TopBar [toggle button]
    ↕ (AiPanelState — Scoped event service)
AiAssistantPanel.razor
    ↓ @inject IAiAgentService
AiAgentService (Scoped — holds conversation history)
    ↓ Anthropic SDK CreateStreaming → IAsyncEnumerable
    ↓ on tool_use → AiToolExecutor.ExecuteAsync(name, input)
AiToolExecutor (Scoped)
    ↓ Dictionary<string, Func<JsonElement, Task<string>>>
    ↓ calls IClientService, IAppointmentService, etc.
Existing Service Layer (unchanged)
```

### Component Responsibilities

| Component | Scope | Responsibility |
|-----------|-------|----------------|
| `AiPanelState` | Scoped | Holds open/closed toggle state; fires `OnChange` event to notify subscribers |
| `AiAssistantPanel.razor` | InteractiveServer | Renders chat history, streams tokens to UI, handles user input |
| `AiToggleButton.razor` | InteractiveServer | Button in TopBar that calls `AiPanelState.Toggle()` |
| `IAiAgentService` / `AiAgentService` | Scoped | Holds conversation history; sends requests to Anthropic API; yields `AgentStreamEvent` records |
| `AiToolExecutor` | Scoped | Dispatches tool calls by name to the correct service method; returns JSON strings |

The assistant never accesses the database directly. All data retrieval goes through the existing service layer, preserving business rules and audit logging.

### Why No SignalR Hub

The Phase B spec included a SignalR hub (`AgentHub`) as the streaming transport. Phase 1 drops this because Blazor InteractiveServer components already run server-side and stream state changes to the browser over the existing `_blazor` websocket. The `IAsyncEnumerable<AgentStreamEvent>` returned by `AiAgentService` is consumed directly in the component with `await foreach`, and each token update triggers a `StateHasChanged()` call.

## Configuration

### API Key

Development (user secrets):

```bash
dotnet user-secrets set "Anthropic:ApiKey" "sk-ant-..." --project src/Nutrir.Web
```

Docker (environment variable):

```yaml
# docker-compose.yml
environment:
  Anthropic__ApiKey: ${ANTHROPIC_API_KEY:-}
```

### appsettings.json

```json
{
  "Anthropic": {
    "ApiKey": "",
    "Model": "claude-sonnet-4-5-20250514",
    "MaxTokens": 4096
  }
}
```

### Options Class

`AnthropicOptions` (registered via `services.Configure<AnthropicOptions>`) exposes:

| Property | Default | Description |
|----------|---------|-------------|
| `ApiKey` | (required) | Anthropic API key |
| `Model` | `claude-sonnet-4-5-20250514` | Model ID sent with every request |
| `MaxTokens` | `4096` | Max tokens per API response |

## Key Files

### New Files

| File | Description |
|------|-------------|
| `src/Nutrir.Core/Interfaces/IAiAgentService.cs` | Service interface and `AgentStreamEvent` record |
| `src/Nutrir.Infrastructure/Configuration/AnthropicOptions.cs` | Strongly-typed options class |
| `src/Nutrir.Infrastructure/Services/AiToolExecutor.cs` | 14 read-only tool handlers and tool definitions |
| `src/Nutrir.Infrastructure/Services/AiAgentService.cs` | Streaming agent loop |
| `src/Nutrir.Web/Components/Layout/AiPanelState.cs` | Scoped toggle state service |
| `src/Nutrir.Web/Components/Layout/AiAssistantPanel.razor` | Chat panel UI component |
| `src/Nutrir.Web/Components/Layout/AiToggleButton.razor` | TopBar toggle button component |
| `src/Nutrir.Web/wwwroot/js/ai-panel.js` | JS interop helpers (localStorage persistence, auto-scroll) |

### Modified Files

| File | Change |
|------|--------|
| `src/Nutrir.Infrastructure/Nutrir.Infrastructure.csproj` | Added `Anthropic` NuGet package (v12.4.0) |
| `src/Nutrir.Infrastructure/DependencyInjection.cs` | Registered `AnthropicOptions`, `AiAgentService`, `AiToolExecutor` |
| `src/Nutrir.Web/Program.cs` | Registered `AiPanelState` as scoped |
| `src/Nutrir.Web/Components/Layout/MainLayout.razor` | Added `<AiAssistantPanel />` |
| `src/Nutrir.Web/Components/Layout/TopBar.razor` | Added `<AiToggleButton />` |
| `src/Nutrir.Web/Components/App.razor` | Added `<script src="js/ai-panel.js">` reference |
| `src/Nutrir.Web/appsettings.json` | Added `Anthropic` configuration section |
| `docker-compose.yml` | Added `Anthropic__ApiKey` environment variable passthrough |

## Tool Reference

Phase 1 exposes 14 read-only tools. All tools return a JSON string that is appended to the conversation as a `tool_result` message.

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

Tool definitions are declared as static `Tool` objects in `AiToolExecutor` and passed to the Anthropic SDK on every request. Adding a new tool requires adding a definition and registering a handler in the `Dictionary<string, Func<JsonElement, Task<string>>>`.

## Streaming Flow

The agent loop runs inside `AiAgentService.SendMessageAsync(string userMessage)` and yields `AgentStreamEvent` records to the component:

1. User message is appended to the in-memory conversation history.
2. `client.Messages.CreateStreaming(params)` is called with the tool list and full conversation history.
3. Text delta events are yielded as `AgentStreamEvent { Type = Text, Delta = "..." }` as they arrive.
4. When the stream closes with `StopReason.ToolUse`:
   - Tool use blocks are extracted from the accumulated response.
   - Each tool is dispatched to `AiToolExecutor.ExecuteAsync(name, input)`.
   - The assistant message and all tool results are appended to conversation history.
   - The loop calls the API again with the updated history.
5. When the stream closes with `StopReason.EndTurn`:
   - `AgentStreamEvent { Type = Complete }` is yielded.
   - The loop exits.
6. A maximum of 10 tool-loop iterations is enforced to prevent runaway chains. If the limit is reached, a warning event is yielded and the loop exits.

The component's `await foreach` loop appends text deltas to the current assistant message string and calls `StateHasChanged()`, which causes Blazor to push the incremental update to the browser over the existing websocket.

## Sample Interactions

The following examples illustrate how the agent resolves common questions.

### "How many clients do we have?"

1. Agent calls `list_clients` with no parameters.
2. Receives the full client list in JSON.
3. Counts the results and replies with the number.

### "What appointments are scheduled for today?"

1. Agent calls `get_dashboard`.
2. Dashboard data includes today's appointment count and list.
3. Agent formats and presents the appointments.

### "Show me James Whitfield's meal plan"

1. Agent calls `search` with `query = "James Whitfield"` to resolve the client ID.
2. Agent calls `list_meal_plans` with `client_id` from the search result.
3. Agent presents the active meal plan.

### "What are David Kim's goals?"

1. Agent calls `search` with `query = "David Kim"` to resolve the client ID.
2. Agent calls `list_goals` with `client_id` from the search result.
3. Agent lists the goals with their status and target values.

## AgentStreamEvent Record

```csharp
public record AgentStreamEvent(
    AgentStreamEventType Type,
    string? Delta = null,
    string? ErrorMessage = null
);

public enum AgentStreamEventType
{
    Text,
    Complete,
    Error
}
```

The component switches on `Type` to append text, mark the message complete, or display an error block.

## Limitations

The following are known Phase 1 limitations, all intentional scope constraints:

| Limitation | Detail |
|------------|--------|
| Read-only | No create, update, or delete tools. The assistant cannot modify any data. |
| No persistence | Conversation history is held in the scoped `AiAgentService` instance. It is lost when the Blazor circuit disconnects (navigation away, browser close, idle timeout). |
| No rate limiting | API calls are not rate-limited per user or per session. A busy user or a runaway loop can exhaust the API quota. |
| No user isolation | All users in a practice share the same API key. There is no per-user usage tracking. |
| Minimal markdown | The panel renders plain text and line breaks only. Bold, code blocks, and tables from the model are displayed as raw markdown syntax. |
| No entity links | Entity IDs in responses are plain text. There are no clickable chips that navigate to detail pages. |
| English only | The system prompt is English. The model responds in English regardless of browser locale. |

## Phase 2 Roadmap

The following items are deferred to Phase 2. See [AI Assistant Spec](ai-assistant-spec.md) for the full Phase B design.

- Write operations (create, update, delete, cancel) with inline confirmation dialogs
- Clickable entity chips that navigate to the detail page within the main content area
- Conversation persistence across circuit disconnects and browser sessions
- Per-user rate limiting and usage tracking
- Rich message rendering (markdown parser for bold, code, tables)
- Contextual awareness — inject the current page's entity ID into the system prompt
