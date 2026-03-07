# AI Assistant Appointment Filtering Fix

**Date:** 2026-03-07
**Issues:** #183, #184

## Problem

Two related issues with the AI assistant's appointment handling:

1. **#183** — The `list_appointments` tool description didn't tell the AI model that results are scoped to the current practitioner. When asked "what's on my schedule?", the AI thought it couldn't answer without a client ID.

2. **#184** — `HandleListAppointments` always passed `_currentUserId` as the nutritionist filter, so Admin users only saw their own appointments instead of all practitioners'.

## Root Cause

- `AiToolExecutor.cs` had no concept of user role — it only knew the user ID
- The tool description said "filter by date range, client, and status" with no mention of practitioner scoping
- `HandleGetDashboard` had the same issue: admin dashboard showed only the admin's own appointments

## Changes

### `AiToolExecutor.cs`
- Added `_currentUserRole` field and `IsAdmin` helper property
- Updated `ExecuteAsync` signature to accept `userRole` parameter
- Updated `list_appointments` tool description to explain practitioner scoping and "my schedule" usage
- Added `nutritionist_id` parameter to `list_appointments` for admin filtering by practitioner
- `HandleListAppointments`: role-aware logic — admin sees all by default, non-admin scoped to own
- `HandleGetDashboard`: admin sees practice-wide counts, non-admin sees own

### `AiAgentService.cs`
- Both `ExecuteAsync` call sites now pass `_userRole` alongside `_userId`

## Pattern Established

See [ADR-0002](../infrastructure/adr-0002-ai-tool-role-aware-filtering.md) for the role-aware filtering pattern.
