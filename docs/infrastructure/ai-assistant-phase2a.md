# AI Assistant — Phase 2a Implementation

## Overview

Phase 2a extends the read-only AI assistant built in Phase 1 with write capabilities: 21 write tools covering all core domain mutations, an inline confirmation system that pauses the agent stream until the user approves or denies each action, and audit source tagging so write operations performed via the AI assistant are distinguishable from web UI and CLI writes in the audit log.

Phase 2a ships as a single cohesive unit because the three components are tightly coupled: write tools are not safe without confirmations, and audit source tagging is only meaningful when mutations are present.

## Architecture Changes

### Confirmation Flow

Phase 1's streaming loop ran to completion without interruption. Phase 2a introduces a pause-and-resume mechanism that suspends the `IAsyncEnumerable` mid-iteration while waiting for user input.

The mechanism uses a `TaskCompletionSource<bool>` per pending confirmation:

1. The agent loop in `AiAgentService` calls a write tool.
2. Before executing, the agent yields a `AgentStreamEvent { Type = ConfirmationRequired, ... }` carrying the tool name, a human-readable description of the action, and the permission tier (`Standard` or `Elevated`).
3. The loop then awaits `_confirmationTcs.Task` — a `TaskCompletionSource<bool>` held on the service instance — which blocks the `IAsyncEnumerable` iteration.
4. `AiAssistantPanel.razor` receives the `ConfirmationRequired` event and renders the confirmation card inline in the chat thread.
5. When the user clicks Allow or Deny, the panel calls `IAiAgentService.ResolveConfirmation(bool approved)`.
6. `ResolveConfirmation` calls `_confirmationTcs.SetResult(approved)`, which unblocks the awaiting loop.
7. If approved, the tool executes and returns its result. If denied, the loop returns a `tool_result` with `{ "success": false, "error": "Action denied by user." }` so the model can acknowledge and stop.

This approach keeps all agent state inside the scoped `AiAgentService` — no additional state management is needed in the component beyond rendering the confirmation card and calling the resolution method.

```
AiAgentService.SendMessageAsync
    → stream yields ConfirmationRequired event
    → await _confirmationTcs.Task          ← blocks here
AiAssistantPanel.razor
    → renders confirmation card
    → user clicks Allow
    → calls IAiAgentService.ResolveConfirmation(true)
        → _confirmationTcs.SetResult(true) ← unblocks
    → tool executes, loop continues
```

### AuditSourceProvider

Phase 1 passed audit source information as method parameters threaded through the service layer. Phase 2a replaces this with an ambient scoped provider that any service can read without requiring parameter changes.

`IAuditSourceProvider` is a scoped interface with a single property:

```csharp
public interface IAuditSourceProvider
{
    AuditSource Source { get; set; }
}
```

The default implementation initializes `Source` to `AuditSource.Web`. `AiAgentService` sets it to `AuditSource.AiAssistant` immediately before executing an approved write tool, then resets it to `AuditSource.Web` immediately after the tool returns (in a `finally` block). `AuditLogService` reads from the provider when creating audit entries rather than accepting source as a parameter.

This means no existing service signatures change, and the audit source is correctly set for the duration of the tool call regardless of call depth.

## Write Tool Reference

Phase 2a adds 21 write tools. All write tools require user confirmation before execution. Tools are grouped into Standard (bordered confirmation card) and Elevated (warning-styled card with caution indicator) tiers.

### Standard Tools

Standard tools cover create, update, delete, and status-change operations on client and clinical data. A bordered confirmation card with Allow and Deny buttons is shown before execution.

| Tool | Operation | Key Parameters |
|------|-----------|----------------|
| `create_client` | Create a new client record | `first_name`, `last_name`, `email?`, `phone?`, `date_of_birth?`, `health_conditions?` |
| `update_client` | Update an existing client's details | `id`, `first_name?`, `last_name?`, `email?`, `phone?`, `health_conditions?` |
| `delete_client` | Permanently delete a client record | `id` |
| `create_appointment` | Schedule a new appointment | `client_id`, `start_time`, `end_time`, `type`, `location_type`, `notes?` |
| `update_appointment` | Update appointment details | `id`, `start_time?`, `end_time?`, `type?`, `location_type?`, `notes?` |
| `cancel_appointment` | Cancel a scheduled appointment | `id`, `reason?` |
| `delete_appointment` | Delete an appointment record | `id` |
| `create_meal_plan` | Create a new meal plan | `client_id`, `name`, `description?` |
| `update_meal_plan` | Update meal plan metadata | `id`, `name?`, `description?` |
| `activate_meal_plan` | Set a meal plan to active status | `id` |
| `archive_meal_plan` | Archive an active meal plan | `id` |
| `duplicate_meal_plan` | Duplicate an existing meal plan | `id`, `new_name?` |
| `delete_meal_plan` | Delete a meal plan | `id` |
| `create_goal` | Create a new progress goal | `client_id`, `type`, `target_value`, `target_date?`, `notes?` |
| `update_goal` | Update goal details | `id`, `target_value?`, `target_date?`, `notes?` |
| `achieve_goal` | Mark a goal as achieved | `id` |
| `abandon_goal` | Mark a goal as abandoned | `id`, `reason?` |
| `delete_goal` | Delete a goal record | `id` |
| `create_progress_entry` | Record a progress measurement | `client_id`, `date`, `weight_kg?`, `measurements?`, `notes?` |
| `delete_progress_entry` | Delete a progress entry | `id` |

### Elevated Tools

Elevated tools cover user account management operations. A warning-styled confirmation card with a caution indicator is shown before execution to signal the increased risk of these actions.

| Tool | Operation | Key Parameters |
|------|-----------|----------------|
| `create_user` | Create a new practitioner account | `email`, `first_name`, `last_name`, `role` |
| `change_user_role` | Change a user's role | `user_id`, `new_role` |
| `deactivate_user` | Deactivate a user account | `user_id` |
| `reactivate_user` | Reactivate a deactivated account | `user_id` |
| `reset_user_password` | Send a password reset email | `user_id` |

## Permission Model

The confirmation system uses a two-tier model. The tier determines the visual treatment of the confirmation card, not the mechanics of the pause-and-resume flow (which is identical for both tiers).

| Tier | Applies To | UI Treatment |
|------|-----------|--------------|
| Standard | All client, appointment, meal plan, goal, and progress write tools | Bordered card with neutral styling, Allow and Deny buttons |
| Elevated | All user management write tools: `create_user`, `change_user_role`, `deactivate_user`, `reactivate_user`, `reset_user_password` | Warning-styled card with caution icon, explicit action description, Allow and Deny buttons |

Read tools (`list_*`, `get_*`, `search`, `get_dashboard`) continue to execute immediately with no confirmation, unchanged from Phase 1.

### Confirmation Card Content

The confirmation card displays:

- The action being requested in plain English (e.g., "Create a new client named Jane Doe")
- For Elevated tools: an explicit caution note about the nature of the action
- Allow and Deny buttons

When the user closes the AI panel while a confirmation is pending, the pending `TaskCompletionSource` is resolved with `false` (denied) to prevent the agent loop from hanging indefinitely.

## Deferred Tools

The following tools from the original spec were deferred from Phase 2a. They remain in the backlog for a future phase.

| Tool | Reason Deferred |
|------|----------------|
| `save_meal_plan_content` | Requires constructing deeply nested meal/day/item DTOs — impractical to express as a flat tool input schema without significant UX work |
| `update_progress_entry` | Requires rebuilding the full measurements list from a partial update — the service layer does not accept partial measurement patches |
| `update_user_profile` | Low value via chat interface; profile updates are better handled in the user settings UI |
| `force_mfa` | Edge case with limited demand; carries risk of locking users out if misused |

## Audit Source Tracking

### AuditSource Enum

```csharp
public enum AuditSource
{
    Web,
    Cli,
    AiAssistant
}
```

All audit log entries include an `AuditSource` value. Entries created through the web UI are tagged `Web`, CLI commands tag `Cli`, and AI assistant write operations tag `AiAssistant`.

### IAuditSourceProvider

```csharp
public interface IAuditSourceProvider
{
    AuditSource Source { get; set; }
}

public class AuditSourceProvider : IAuditSourceProvider
{
    public AuditSource Source { get; set; } = AuditSource.Web;
}
```

`AuditSourceProvider` is registered as a scoped service. Its lifetime matches the Blazor circuit and CLI command lifetime, ensuring each user session has its own instance and there is no cross-session leakage.

### How AiAgentService Uses It

`AiAgentService` injects `IAuditSourceProvider` and wraps each approved write tool execution:

```csharp
_auditSourceProvider.Source = AuditSource.AiAssistant;
try
{
    result = await _toolExecutor.ExecuteAsync(toolName, input);
}
finally
{
    _auditSourceProvider.Source = AuditSource.Web;
}
```

The `finally` block ensures the source is always reset even if the tool throws an exception.

### How AuditLogService Uses It

`AuditLogService` injects `IAuditSourceProvider` and reads its value when creating entries:

```csharp
var entry = new AuditLogEntry
{
    // ...
    Source = _auditSourceProvider.Source,
    // ...
};
```

No existing method signatures on `IAuditLogService` change — the source is read from the ambient provider rather than passed as a parameter.

### AgentStreamEvent Changes

Phase 2a adds two new event types to `AgentStreamEventType`:

```csharp
public enum AgentStreamEventType
{
    Text,
    Complete,
    Error,
    ConfirmationRequired,   // new — pause and show confirmation card
    ConfirmationResolved    // new — user responded; carry the result for logging
}
```

`ConfirmationRequired` events carry additional data on the `AgentStreamEvent` record:

```csharp
public record AgentStreamEvent(
    AgentStreamEventType Type,
    string? Delta = null,
    string? ErrorMessage = null,
    string? ConfirmationDescription = null,   // new
    ConfirmationTier? Tier = null             // new
);

public enum ConfirmationTier { Standard, Elevated }
```

## Key Files

### New Files

| File | Description |
|------|-------------|
| `src/Nutrir.Core/Enums/AuditSource.cs` | `AuditSource` enum: `Web`, `Cli`, `AiAssistant` |
| `src/Nutrir.Core/Interfaces/IAuditSourceProvider.cs` | Scoped ambient provider interface |
| `src/Nutrir.Infrastructure/Services/AuditSourceProvider.cs` | Default implementation, initializes to `AuditSource.Web` |
| `src/Nutrir.Web/Components/Layout/AiConfirmationCard.razor` | Inline confirmation card component — Standard and Elevated visual variants |

### Modified Files

| File | Change |
|------|--------|
| `src/Nutrir.Core/Interfaces/IAiAgentService.cs` | Added `ResolveConfirmation(bool approved)` method; added `ConfirmationRequired` and `ConfirmationResolved` event types; added `ConfirmationTier` enum and new `AgentStreamEvent` properties |
| `src/Nutrir.Infrastructure/Services/AiToolExecutor.cs` | Added 21 write tool definitions and handlers; added `IsWriteTool(string name)` and `GetConfirmationTier(string name)` helpers |
| `src/Nutrir.Infrastructure/Services/AiAgentService.cs` | Added `_confirmationTcs` field; added confirmation pause-and-resume logic; added `ResolveConfirmation` implementation; injects and uses `IAuditSourceProvider` around write tool calls |
| `src/Nutrir.Infrastructure/DependencyInjection.cs` | Registered `IAuditSourceProvider` / `AuditSourceProvider` as scoped |
| `src/Nutrir.Web/Components/Layout/AiAssistantPanel.razor` | Handles `ConfirmationRequired` events; renders `<AiConfirmationCard>`; calls `ResolveConfirmation` on Allow/Deny; resolves pending confirmation to `false` on panel close |
| `src/Nutrir.Infrastructure/Services/AuditLogService.cs` | Injects `IAuditSourceProvider`; reads `Source` from provider when creating audit entries |
| `src/Nutrir.Infrastructure/Data/Migrations/` | Migration adding `Source` column to `AuditLogs` table |

## Verification Steps

Use this checklist after deployment to confirm Phase 2a is functioning correctly.

| Step | Action | Expected Result |
|------|--------|----------------|
| Build | `dotnet build` | No errors or warnings |
| Migration | `dotnet ef database update` | `Source` column added to `AuditLogs` table without error |
| Docker | `docker compose up --build` | App starts, no startup exceptions in Seq logs |
| Read tools | Ask "How many clients do we have?" | Answer streams with no confirmation card |
| Write + confirm (Allow) | Ask "Create a client named Test User" | Confirmation card appears; click Allow; client is created; success message in chat |
| Write + confirm (Deny) | Ask "Delete client [id]" | Confirmation card appears; click Deny; assistant acknowledges denial and stops |
| Elevated tier | Ask "Create a new user account for..." | Warning-styled elevated confirmation card appears |
| Audit source | Check audit log after an allowed write | Entry shows `Source = AiAssistant` |
| Panel close | Open a write prompt, wait for confirmation, close panel | Pending confirmation resolves as denied; loop does not hang |
| Error handling | Ask to delete a non-existent record | Tool returns error JSON; assistant surfaces the error message |
