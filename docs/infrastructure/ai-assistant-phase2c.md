# AI Assistant Phase 2c — Production Hardening

## Overview

Phase 2c hardens the AI assistant for real-world use with three capabilities: conversation persistence, per-user rate limiting, and usage tracking with an admin dashboard.

## Conversation Persistence

### Entity Model

- **`AiConversation`** — One row per session. Tracks `UserId`, `CreatedAt`, `LastMessageAt`.
- **`AiConversationMessage`** — One row per `MessageParam`. Stores serialized API content (`ContentJson`) and extracted plain text (`DisplayText`) for UI reconstruction.

### Session Semantics

Sessions use an **8-hour idle expiry**. If `LastMessageAt` is older than 8 hours, a new session starts automatically. This means:
- Conversations persist across page reloads and circuit reconnects within the same working day.
- After overnight or extended breaks, users start fresh.

### Serialization Strategy

Messages are serialized using a custom `StoredContentBlock` DTO format rather than direct SDK type serialization. This provides stable round-trip behavior regardless of Anthropic C# SDK version changes.

Content types handled:
- **Text blocks** — `{ "Type": "text", "Text": "..." }`
- **Tool use blocks** — `{ "Type": "tool_use", "Id": "...", "Name": "...", "Input": "{...}" }`
- **Tool result blocks** — `{ "Type": "tool_result", "ToolUseId": "...", "Content": "..." }`
- **Plain strings** — JSON-serialized string (user text messages)

### Message Cap

Maximum 100 messages per conversation (approximately 50 user-assistant exchanges). When exceeded, the oldest messages are trimmed from the database via `ExecuteDeleteAsync`.

### Service: `IAiConversationStore` / `AiConversationStore` (Scoped)

- `LoadActiveSessionAsync(userId)` — Finds the most recent non-expired session, deserializes messages, returns both API history and display messages.
- `SaveMessagesAsync(userId, messages, displayTexts)` — Creates session if needed, appends messages, enforces cap.
- `ClearHistoryAsync(userId)` — Deletes all conversations for the user.

## Rate Limiting

### Service: `IAiRateLimiter` / `AiRateLimiter` (Singleton)

In-memory rate limiter using `ConcurrentDictionary<string, UserRateState>` with two sliding windows:
- **Per-minute**: 30 requests (configurable)
- **Per-day**: 500 requests (configurable)

Windows reset when their duration elapses. Stale entries (users inactive > 1 hour) are cleaned up on access (checked every 5 minutes).

### Configuration

```json
{
  "AiRateLimits": {
    "RequestsPerMinute": 30,
    "RequestsPerDay": 500
  }
}
```

Defined in `AiRateLimitOptions` and bound from `appsettings.json`.

## Usage Tracking

### Entity: `AiUsageLog`

One row per API call (per iteration of the tool loop):
- `UserId`, `RequestedAt`
- `InputTokens`, `OutputTokens` — captured from stream events
- `ToolCallCount` — number of tool invocations in the exchange
- `DurationMs` — wall-clock time for the full exchange
- `Model` — model identifier used

### Service: `IAiUsageTracker` / `AiUsageTracker` (Scoped)

- `LogAsync(...)` — Persists a usage log entry.
- `GetSummaryAsync(from, to)` — Aggregates usage by user with name/email resolution.
- `GetDailyUsageAsync(userId, from, to)` — Daily breakdown for a specific user.

### Admin Page: `/admin/ai-usage`

- **Access**: Admin role only (`[Authorize(Roles = "Admin")]`)
- **Summary view**: Table showing each user's total requests, input/output tokens, and last active date.
- **Drill-down view**: Click a user to see daily breakdown with requests, tokens, tool calls, and average duration.
- **Date range filter**: Last 7, 30, or 90 days (default: 30).

## Integration Points

### `AiAgentService` Changes

1. **Rate limit check** at the start of `SendMessageAsync` — returns error event if exceeded.
2. **Token capture** from stream events (`TryPickStart` for input tokens, `TryPickDelta` for output tokens).
3. **Usage logging** after each complete exchange via `SaveAndLogAsync`.
4. **History loading** via `LoadHistoryAsync()` — populates `_conversationHistory` from stored session.
5. **Async clear** via `ClearHistoryAsync()` — clears both in-memory and DB history.

### `AiAssistantPanel.razor` Changes

1. **History restoration** in `OnAfterRenderAsync` — loads display messages and reconstructs UI chat list.
2. **Async clear** — `ClearConversation` now calls `ClearHistoryAsync()`.

## Database Migration

Single migration `AddAiProductionHardening` creates:
- `AiConversations` table with `UserId` index
- `AiConversationMessages` table with `ConversationId` index
- `AiUsageLogs` table with `UserId` and `RequestedAt` indexes

All tables use cascade delete from `AspNetUsers`.
