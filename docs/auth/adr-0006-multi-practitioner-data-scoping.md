# ADR-0006: Multi-Practitioner Data Scoping

**Status:** Proposed
**Date:** 2026-03-23
**Domain:** Auth / Cross-Cutting

## Context

All service methods that return lists or aggregates query globally — there is no practitioner filter applied at the service layer. For a single-practitioner clinic this is acceptable, but it is a hard blocker for multi-practitioner deployments.

**Reported impact:** Marco (March 2026 product roundtable) operates a 4-person clinic. His dashboard shows all four practitioners' appointments mixed together, making the "Today's Appointments" widget and dashboard metrics meaningless for his daily workflow.

**Existing FK fields** that make filtering viable today (no schema changes required for the scoping logic):

| Entity | FK field | Semantic meaning |
|--------|----------|-----------------|
| `Client` | `PrimaryNutritionistId` | Client ownership / assignment |
| `Appointment` | `NutritionistId` | Appointment assignment |
| `MealPlan` | `CreatedByUserId` | Plan creator |
| `ProgressGoal` | `CreatedByUserId` | Goal creator |
| `SessionNote` | `CreatedByUserId` | Note author |

**Prior art — ADR-0002** established the role-aware filtering pattern for AI tool handlers: Admin users see all practitioners' data, non-admin users see only their own. That pattern lives inside `AiToolExecutor` and does not propagate to the service layer. This ADR generalises the same principle to all service methods across the application.

## Decision

### 1. Service method signatures

Add an optional `string? practitionerId = null` parameter to all list and aggregate service methods that touch practitioner-owned data:

- `null` — practice-wide (no filter applied)
- non-null — filter results to the specified practitioner

All new parameters default to `null` so existing callers continue to compile and behave identically. No breaking changes.

### 2. `IPractitionerScopeProvider` — scoped service

Introduce a scoped service that resolves the effective practitioner ID for the current request:

```csharp
public interface IPractitionerScopeProvider
{
    /// <summary>
    /// Returns the practitioner ID to filter by, or null for practice-wide access.
    /// </summary>
    string? GetEffectivePractitionerId();

    /// <summary>
    /// Whether the current user is allowed to toggle between My View and Practice View.
    /// </summary>
    bool CanToggle { get; }

    /// <summary>
    /// Current view mode. Only meaningful when CanToggle is true.
    /// </summary>
    PractitionerViewMode ViewMode { get; set; }
}

public enum PractitionerViewMode { MyView, PracticeView }
```

The provider reads from `AuthenticationStateProvider` to determine the current user's ID and role, then applies the role defaults described in the scoping matrix below. View mode is held in memory for the duration of the Blazor Server circuit (session-scoped, not persisted to the database).

### 3. Role behavior

| Role | Default view | Can toggle? | Rationale |
|------|-------------|------------|-----------|
| Admin | Practice-wide (`null`) | Yes — can switch to "My View" | Admins routinely monitor the whole clinic |
| Nutritionist | My view (`currentUserId`) | No | Practitioners should default to their own workload |
| Assistant | Practice-wide (`null`) | No | Read-only practice visibility; no personal caseload |

### 4. CascadingParameter from MainLayout

`IPractitionerScopeProvider` is injected into `MainLayout` and exposed as a `CascadingValue<IPractitionerScopeProvider>` so all descendant components can access the current scope without prop-drilling. Pages that call services consume the cascading value to pass the effective practitioner ID.

### 5. "My View / Practice View" toggle

A toggle button is added to the sidebar or TopBar, visible only when `CanToggle == true` (i.e., Admin role). It reads and sets `ViewMode` on the provider. Blazor's `StateHasChanged` propagates the change through the cascading parameter to all subscribed components, which re-query their services with the updated practitioner ID.

The toggle is **not** persisted to the database. Admins switch context frequently mid-session; a page-reload resets to the role default (practice-wide for Admin), which is acceptable.

### 6. Affected services and methods

| Service | Methods receiving `string? practitionerId` |
|---------|--------------------------------------------|
| `IDashboardService` | `GetMetricsAsync`, `GetTodaysAppointmentsAsync`, `GetThisWeekAppointmentCountAsync`, `GetActiveMealPlanCountAsync`, `GetRecentClientsAsync`, `GetClientsMissingConsentAsync`, `GetRecentMealPlansAsync` |
| `IReportService` | `GetPracticeSummaryAsync` |
| `IClientService` | list / search methods |
| `IMealPlanService` | list methods |
| `IProgressService` | list methods |
| `ISessionNoteService` | list methods |

## Consequences

**Positive:**
- Multi-practitioner clinics get meaningful, personalised views with no data leakage between practitioners.
- The pattern is consistent with ADR-0002; the AI assistant and the UI now speak the same scoping language.
- All changes are backward-compatible — existing single-practitioner deployments are unaffected (default `null` = all).
- Admin retains unrestricted access regardless of toggle state.

**Negative / trade-offs:**
- Every affected service method gains an extra parameter. Callers that do not yet pass the scope will silently return practice-wide data — this is safe but means the feature is opt-in for each integration point.
- View mode is session-scoped, not user-preference-persisted. Admins must re-toggle after a page reload. This is a deliberate simplicity trade-off for v1; persistence can be added later if requested.
- The `CascadingParameter` pattern adds a layer of implicit state. Component authors must remember to consume it rather than hardcoding `null`.

**Implementation order (recommended):**
1. `IPractitionerScopeProvider` scoped service (no UI dependency)
2. `DashboardService` — highest visibility, validates the pattern end-to-end
3. `ReportService`, `ClientService`, `MealPlanService`, `ProgressService`, `SessionNoteService` — in parallel
4. View toggle component + `CascadingParameter` wiring in `MainLayout`
5. Page-level integration (Dashboard, Client list, Appointments, Meal Plans, Reports)

## Related

- [ADR-0002: Role-Aware Filtering in AI Tool Handlers](../infrastructure/adr-0002-ai-tool-role-aware-filtering.md)
- GitHub issue [#237](https://github.com/cpike/nutrir/issues/237): Multi-practitioner data scoping
- GitHub issue [#20](https://github.com/cpike/nutrir/issues/20): Per-practitioner utilization report (unblocked by this ADR)
