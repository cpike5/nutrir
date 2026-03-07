# ADR-0002: Role-Aware Filtering in AI Tool Handlers

**Status:** Accepted
**Date:** 2026-03-07
**Domain:** Infrastructure

## Context

AI tool handlers in `AiToolExecutor` always passed `_currentUserId` as a filter when querying data (e.g., appointments). This meant Admin users — who should have practice-wide visibility — only saw their own data when using the AI assistant.

The underlying services already supported unfiltered queries (e.g., `AppointmentService.GetListAsync` accepts `null` for `nutritionistId`), but the tool executor never took advantage of this for admin users.

## Decision

Pass the user's role into `AiToolExecutor.ExecuteAsync` alongside the user ID. Each handler that needs role-aware scoping uses an `IsAdmin` helper to decide whether to filter by the current user or return all results:

- **Admin users** see all practitioners' data by default, with optional filters to narrow scope
- **Non-admin users** (Nutritionist) see only their own data

This pattern applies to any tool handler that queries data owned by specific practitioners.

## Affected Tools

- `list_appointments` — Admin sees all practitioners; added `nutritionist_id` parameter for explicit filtering
- `get_dashboard` — Admin sees practice-wide appointment counts; non-admin sees own

## Consequences

- Future tool handlers that need role-aware scoping follow the same `IsAdmin` pattern
- Tool descriptions must communicate scoping behavior to the AI model so it can reason about what data is available
- The `ExecuteAsync` signature now carries `userRole`, making role context available to all handlers without additional plumbing
