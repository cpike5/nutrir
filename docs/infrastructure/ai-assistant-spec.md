# Nutrir AI Assistant Panel — Design Specification

## Overview

The AI assistant is a collapsible right-side panel in the Blazor UI that accepts natural language requests and executes them against the Nutrir service layer. Users can ask the assistant to create clients, schedule appointments, look up progress records, and perform other platform operations without navigating the UI manually.

This document describes the Phase B future-state design. It is a specification, not an implementation guide.

## Architecture

```
[Blazor Panel] <--SignalR--> [AgentHub] --> [AnthropicAgentService]
                                                      |
                                               [Tool Executor]
                                                      |
                                            [Existing Services]
                                      (IClientService, IAppointmentService,
                                       IMealPlanService, IGoalService,
                                       IProgressService, IUserService, ...)
```

- **Blazor Panel** — InteractiveServer component, renders chat history and streams incoming tokens
- **AgentHub** — SignalR hub that bridges the Blazor panel to the backend agent service
- **AnthropicAgentService** — Sends the system prompt and tool definitions to the Anthropic API, processes tool call responses
- **Tool Executor** — Routes tool calls to the appropriate application service and returns structured results
- **Existing Services** — Unchanged; the assistant reuses the same service layer as the web UI and CLI

The assistant never accesses the database directly. All operations go through the service layer, preserving business rules, validation, and audit logging.

## User Flow

1. User clicks the AI assistant icon in the app header or sidebar to open the panel
2. User types a natural language request (e.g., "Create a new client named John Smith with celiac disease")
3. The authenticated user's context (userId, role) is captured server-side and injected into the agent invocation
4. The system prompt, tool definitions, and conversation history are sent to the Anthropic Agent SDK
5. The agent determines required tool calls and executes them sequentially against the service layer via the Tool Executor
6. Results stream back to the panel via SignalR
7. The panel renders the response with entity links (e.g., a chip linking to the new client's detail page)
8. For mutation operations, a confirmation dialog is shown before the tool call executes (see Permission Model)

## Tool Definitions

Each tool maps 1:1 to a CLI command and has:
- A unique name (snake_case)
- A plain-English description used in the system prompt
- A JSON Schema for parameters (required vs optional marked explicitly)
- A return schema (the same JSON envelope as the CLI)

### Clients

| Tool Name | Maps To |
|-----------|---------|
| `list_clients` | `clients list` |
| `get_client` | `clients get` |
| `create_client` | `clients create` |
| `update_client` | `clients update` |
| `delete_client` | `clients delete` |

### Appointments

| Tool Name | Maps To |
|-----------|---------|
| `list_appointments` | `appointments list` |
| `get_appointment` | `appointments get` |
| `create_appointment` | `appointments create` |
| `cancel_appointment` | `appointments cancel` |
| `delete_appointment` | `appointments delete` |

### Meal Plans

| Tool Name | Maps To |
|-----------|---------|
| `list_meal_plans` | `meal-plans list` |
| `get_meal_plan` | `meal-plans get` |
| `create_meal_plan` | `meal-plans create` |
| `activate_meal_plan` | `meal-plans activate` |
| `archive_meal_plan` | `meal-plans archive` |
| `duplicate_meal_plan` | `meal-plans duplicate` |
| `delete_meal_plan` | `meal-plans delete` |

### Goals

| Tool Name | Maps To |
|-----------|---------|
| `list_goals` | `goals list` |
| `get_goal` | `goals get` |
| `create_goal` | `goals create` |
| `update_goal` | `goals update` |
| `achieve_goal` | `goals achieve` |
| `abandon_goal` | `goals abandon` |
| `delete_goal` | `goals delete` |

### Progress

| Tool Name | Maps To |
|-----------|---------|
| `list_progress` | `progress list` |
| `get_progress_entry` | `progress get` |
| `create_progress_entry` | `progress create` |
| `delete_progress_entry` | `progress delete` |

### Users

| Tool Name | Maps To |
|-----------|---------|
| `list_users` | `users list` |
| `get_user` | `users get` |
| `create_user` | `users create` |
| `update_user` | `users update` |
| `change_user_role` | `users change-role` |
| `deactivate_user` | `users deactivate` |
| `reactivate_user` | `users reactivate` |
| `reset_user_password` | `users reset-password` |

### Utility

| Tool Name | Maps To |
|-----------|---------|
| `search` | `search` |
| `get_dashboard` | `dashboard` |
| `list_audit` | `audit list` |

## Permission Model

The assistant uses a three-tier confirmation model:

| Tier | Operations | Behavior |
|------|-----------|----------|
| No confirmation | All read operations: `list_*`, `get_*`, `search`, `get_dashboard`, `list_audit` | Executes immediately, streams result |
| Confirmation required | Create, update, delete on clients, appointments, meal plans, goals, progress entries | Shows inline confirmation dialog before executing |
| Elevated confirmation | All user management operations: `create_user`, `change_user_role`, `deactivate_user`, `reactivate_user`, `reset_user_password` | Shows a more prominent confirmation dialog with explicit action description |

### Confirmation Dialog

The UI renders an inline confirmation chip in the chat thread:

> The assistant wants to **create a client** named John Smith. Allow?
>
> [Allow] [Deny]

If the user denies, the assistant receives a `tool_denied` result and should acknowledge and stop the operation.

## System Prompt

The system prompt is assembled at request time and includes:

- **Practice context**: The assistant is helping a nutrition practice manage clients, appointments, meal plans, goals, and progress. It operates on behalf of the authenticated user.
- **Tool catalog**: A concise summary of all available tools and what they do.
- **Enum reference**: All valid values for types, statuses, and locations across every domain (appointment types, goal types, metric types, etc.).
- **Multi-step workflow guidance**: Instructions for chaining operations — e.g., look up a client by name before referencing their ID, confirm IDs from list results before creating dependent records.
- **Error handling instructions**: If a tool returns `success: false`, surface the error message to the user and do not proceed with dependent steps.
- **Scope constraints**: The assistant should not perform operations outside the tool set. It should not offer to write code, access external systems, or speculate about data it has not retrieved.
- **Confirmation acknowledgement**: When a confirmation is denied, acknowledge clearly and stop. Do not retry the same operation.

The prompt is version-controlled and tuned separately from application code.

## UI Design

### Panel Layout

- Fixed right-side drawer, 400px wide
- Collapsible — toggled via a button in the app header (icon: sparkle or chat bubble)
- Does not shift the main content area — overlays on top (similar to a notification drawer)
- Persists open/closed state in local storage across navigation

### Chat Interface

- Scrollable message history with user and assistant bubbles
- User messages: right-aligned, primary color background
- Assistant messages: left-aligned, neutral background
- Streaming text renders incrementally as tokens arrive via SignalR

### Entity Links

When the assistant creates or references an entity, it renders a clickable chip inline:

```
Client created: [John Smith →]
```

Clicking the chip navigates to the entity detail page within the main content area.

### Confirmation Dialogs

Rendered inline in the chat thread (not modal). The dialog is dismissed after the user clicks Allow or Deny.

### Error States

- Tool errors surface as a styled error block in the chat with the error message
- Network/SignalR disconnects show a persistent banner with a "Reconnect" button
- Rate limit exceeded shows a countdown until the next request is allowed

## Technical Stack

| Component | Technology |
|-----------|-----------|
| UI panel | Blazor `InteractiveServer` render mode |
| Streaming transport | SignalR (existing `_blazor` websocket) |
| LLM provider | Anthropic API (Claude Sonnet — fast, cost-effective for tool use) |
| .NET integration | Anthropic Agent SDK for .NET, or direct REST API with streaming |
| Service layer | Existing `IClientService`, `IAppointmentService`, etc. — no changes |
| Audit | Existing audit infrastructure; source tagged as `ai-assistant` |

## Rate Limiting

Per-user limits enforced server-side in `AgentHub`:

| Limit | Value |
|-------|-------|
| Requests per minute | 30 |
| Requests per day | 500 |
| Max tokens per request | TBD (based on Anthropic plan tier) |

Limits are tracked in-memory per session for v1. Persistent tracking (e.g., Redis or database) is a v2 consideration.

## Future Enhancements

- **Conversation history persistence** — Store chat history per user in the database so context survives page reloads and browser sessions
- **Contextual awareness** — Inject the current page context (e.g., currently viewed client ID) into the system prompt so the user can say "create a goal for this client" without specifying an ID
- **Multi-step workflow templates** — Pre-built prompts for common workflows (new client onboarding, end-of-session notes, weekly review) accessible via slash commands
- **Audit source column** — Dedicated `Source` enum on audit entries (replacing the free-text tag) to enable reliable filtering between `Web`, `Cli`, and `AiAssistant` actions
- **Structured output mode** — For batch operations, the assistant streams a structured progress indicator rather than prose

## Phase 1 Implementation

Phase 1 has been implemented as a read-only proof of concept. It proves the agent loop works with streaming and the existing service layer, and delivers immediate value as a natural language query interface for practice data.

See [AI Assistant Phase 1](ai-assistant-phase1.md) for full implementation details including key files, configuration, streaming flow, and known limitations.

Phase 1 scope:

- 14 read-only tools covering clients, appointments, meal plans, goals, progress, users, search, and dashboard
- Streaming text output via `IAsyncEnumerable<AgentStreamEvent>` consumed directly in the Blazor component
- Tool-use loop with a 10-iteration safety limit
- Collapsible panel UI toggled from the TopBar, with open/closed state persisted in localStorage

Out of scope for Phase 1 (deferred to Phase 2 / Phase B):

- Write operations (create, update, delete, cancel)
- Confirmation dialogs
- SignalR hub — Phase 1 uses the Blazor InteractiveServer circuit directly as the streaming channel
- Entity links, conversation persistence, rate limiting, and rich markdown rendering
