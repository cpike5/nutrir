# AI Assistant — Phase 2b: UX Polish

## Overview

Phase 2b delivers three UX improvements to the AI assistant panel: stream cancellation fix, entity link chips, and contextual page awareness.

These features were identified in the Phase 2b roadmap entry in [AI Assistant Spec](ai-assistant-spec.md) as independent improvements that significantly enhance daily usability. Rich markdown rendering shipped as part of the Phase 2a polish work. This document covers the three remaining items delivered in Phase 2b.

## Stream Cancellation Fix

Previous behaviour: closing the panel or clearing the conversation while a stream was in progress left the `IAsyncEnumerable` iteration running in the background, holding a connection and potentially executing tools after the user had moved on.

Phase 2b fixes this by introducing a per-stream `CancellationTokenSource`:

- `AiAssistantPanel.razor` creates a new `CancellationTokenSource` at the start of each `SendMessage()` call and stores it as a field.
- The token is passed to `AiAgentService.SendMessageAsync()` and threaded through to the agent loop and all tool calls.
- `Close()`, `ClearConversation()`, and `DisposeAsync()` all call `Cancel()` on the active `CancellationTokenSource`.
- When cancellation is triggered while a confirmation dialog is pending, the pending `TaskCompletionSource` is resolved via the cancellation callback already wired in `AiAgentService` — the same callback used when the panel is closed with a confirmation in flight (introduced in Phase 2a).
- `OperationCanceledException` is caught silently at the panel level — no error message is shown to the user.

## Entity Link Chips

The AI assistant can now render inline navigation chips for entities it creates or references. Chips link directly to the entity detail page without requiring the user to navigate manually.

### Syntax

The model uses a double-bracket reference format in its responses:

```
[[type:id:display]]
```

Examples:

```
[[client:3:Jane Doe]]
[[appointment:15:Follow-up consultation]]
[[meal_plan:7:High Protein Plan]]
[[user:a1b2c3d4-...:Dr. Smith]]
```

### Supported Types and Routes

| Syntax | Rendered Route |
|--------|----------------|
| `[[client:3:Name]]` | `/clients/3` |
| `[[appointment:15:Desc]]` | `/appointments/15` |
| `[[meal_plan:7:Name]]` | `/meal-plans/7` |
| `[[user:guid:Name]]` | `/admin/users/guid` |

Goals and progress entries are excluded — they have no standalone detail pages.

### Rendering

- `AiAssistantPanel.razor` applies a regex pass over the assistant's response text after HTML encoding, replacing each `[[type:id:display]]` token with a styled `<a href>` chip.
- HTML encoding runs first to ensure XSS safety before any markup is injected.
- Links use standard `<a href>` tags. Blazor enhanced navigation intercepts them, so the panel stays open and the main content area navigates without a full page reload.
- Chips are styled via `AiAssistantPanel.razor.css` with an inline rounded appearance distinct from plain hyperlinks.

### System Prompt Instructions

The system prompt instructs the model to use the `[[type:id:display]]` format only for entities whose IDs have been confirmed by a tool result. The model must not fabricate references for entities it has not retrieved via tools.

## Contextual Page Awareness

The panel now injects the current page's entity context into the system prompt, enabling natural language references such as "create a goal for this client" or "what appointments does this client have?" without the user needing to supply an ID.

### Interface Change

`IAiAgentService` gains a new method:

```csharp
void SetPageContext(string? entityType, string? entityId);
```

`AiAgentService` stores the values in fields and includes them in the system prompt as a "Current Page Context" section when both values are present.

### URL Parsing

`AiAssistantPanel.razor` subscribes to `NavigationManager.LocationChanged` and parses the current URL on each navigation:

| URL Pattern | Resolved Context |
|-------------|-----------------|
| `/clients/{id}` | `entityType = "client"`, `entityId = id` |
| `/clients/{id}/progress` | `entityType = "client"`, `entityId = id` |
| `/appointments/{id}` | `entityType = "appointment"`, `entityId = id` |
| `/meal-plans/{id}` | `entityType = "meal_plan"`, `entityId = id` |
| `/admin/users/{guid}` | `entityType = "user"`, `entityId = guid` |

Sub-paths (e.g., `/clients/5/progress`) correctly resolve to the parent entity context by matching the leading path segment. Pages with no recognized entity pattern clear the context.

### System Prompt Injection

When a page context is set, `AiAgentService` appends a section to the system prompt before the request is sent:

```
## Current Page Context

The user is currently viewing: client with ID 5.
When the user refers to "this client", "the current client", or similar, use ID 5.
If you need the client's name or other details, use get_client(5).
```

No entity name lookup is performed proactively — the AI uses `get_*` tools if it needs the name, keeping the context injection lightweight and avoiding unnecessary API calls.

## Key Files Changed

| File | Changes |
|------|---------|
| `src/Nutrir.Core/Interfaces/IAiAgentService.cs` | Added `SetPageContext(string? entityType, string? entityId)` method |
| `src/Nutrir.Infrastructure/Services/AiAgentService.cs` | Page context fields; system prompt "Current Page Context" section; entity reference syntax instructions |
| `src/Nutrir.Web/Components/Layout/AiAssistantPanel.razor` | Per-stream `CancellationTokenSource`; entity link regex; `LocationChanged` subscription and URL parsing; `SetPageContext` calls |
| `src/Nutrir.Web/Components/Layout/AiAssistantPanel.razor.css` | Entity chip styles (inline rounded chip, hover state) |
