# Tier 1 Implementation Plan — Milestone 12

**Issues:** #224, #233, #234, #232
**Date:** 2026-03-10

---

## Pre-Implementation Discovery Summary

### Key Finding: #224 Is Already Implemented
`AppointmentService.CreateAsync()` already calls `CheckOverlapAsync()` (line 178), which detects conflicts with existing appointments including buffer time. `UpdateAsync()` also checks overlap when time changes (line 225). `CreateRecurringAsync()` catches `SchedulingConflictException` and skips conflicting slots.

The AI tool executor's generic `catch (Exception ex)` at line 809 already surfaces the `SchedulingConflictException.Message` back to the AI as `{ error: "..." }`.

**Recommendation:** Close #224 as already-done. Verify with a quick manual test and add unit tests if missing.

### #233: Availability Enforcement — The Real Gap
`AppointmentService` has **no reference** to `IAvailabilityService`. The overlap check prevents double-booking, but nothing prevents booking **outside working hours** (e.g., scheduling at 2 AM when schedule says 9 AM–5 PM). This is the actual enforcement gap.

### #233: Advisory Warning Already Exists in AI Context Builder
`AiToolExecutor.BuildCreateAppointmentContext()` (line 296) already checks working hours and adds warnings — but this is **advisory only** (shown in the confirmation prompt). The actual `CreateAsync` call still proceeds. The enforcement must be at the service layer.

### #234: Macro Totals — Partially Done in Both Views
- `MealPlanEdit.razor` has a `day-totals` bar (lines 81-94) showing per-day kcal/P/C/F with a calorie diff indicator
- `MealPlanDetail.razor` shows `day.TotalCalories`, `TotalProtein`, `TotalCarbs`, `TotalFat` from the DTO (pre-computed in `MealPlanService.MapToDetailDto()`)
- **What's missing:** Color-coded comparison against targets (green/amber/red) on both pages. Edit page only compares calories (±50 kcal threshold), detail page shows raw totals with no comparison. Neither page uses percentage-based thresholds per the issue spec.

### No Test Project Exists
The project has **zero test infrastructure** — no `*.Tests.csproj`, no test framework. Unit tests mentioned in acceptance criteria would require creating a test project first. Testing will be manual-only for this batch unless we add a test project as a prerequisite.

### #232: Session Notes Workflow — New Entity Required
- No `SessionNote` entity exists
- `ProgressEntry` has no FK to `Appointment`
- `UpdateStatusAsync()` currently just persists the status change with no hooks
- `CompleteAppointment()` in `AppointmentDetail.razor` (line 455) just calls `UpdateStatusAsync` and refreshes — no redirect or prompt

---

## Implementation Plan

### Work Package 1: Close #224 + Add Availability Enforcement (#233)
**Agent:** `dotnet-specialist` (worktree isolation)
**Branch:** `feature/233-availability-enforcement`
**Closes:** #224, #233

Since #224 is already implemented, this work package focuses entirely on #233 while acknowledging #224 in the PR.

#### Step 1: Add `IsSlotWithinScheduleAsync` to `IAvailabilityService`
**File:** `src/Nutrir.Core/Interfaces/IAvailabilityService.cs`
```csharp
Task<(bool IsWithin, string? Reason)> IsSlotWithinScheduleAsync(
    string practitionerId, DateTime startTimeUtc, int durationMinutes);
```

#### Step 2: Implement in `AvailabilityService`
**File:** `src/Nutrir.Infrastructure/Services/AvailabilityService.cs`
- Look up the `PractitionerSchedule` for the appointment's day of week
- If no schedule exists or `IsAvailable == false` → return `(false, "Practitioner is not available on {DayOfWeek}")`
- Convert the appointment start/end to `TimeOnly` and compare against `schedule.StartTime` / `schedule.EndTime`
- If outside bounds → return `(false, "Appointment falls outside working hours ({start}–{end})")`
- Check `PractitionerTimeBlocks` for the specific date (vacation days, etc.)
- If blocked → return `(false, "Time block conflict: {reason}")`
- Otherwise → return `(true, null)`

#### Step 3: Inject `IAvailabilityService` into `AppointmentService`
**File:** `src/Nutrir.Infrastructure/Services/AppointmentService.cs`
- Add `IAvailabilityService` to constructor
- In `CreateAsync()`, after `CheckOverlapAsync()` (line 178), add:
  ```csharp
  var (isWithin, reason) = await _availabilityService
      .IsSlotWithinScheduleAsync(userId, dto.StartTime, dto.DurationMinutes);
  if (!isWithin)
      throw new SchedulingConflictException("Outside working hours", reason!, ...);
  ```
- Same check in `UpdateAsync()` when time changes (alongside the existing overlap check)

#### Step 4: Update DI registration
**File:** Confirm `AvailabilityService` is already registered. If `AppointmentService` constructor signature changed, ensure DI still resolves.

#### Step 5: Verify AI tool error surfacing
The generic catch at `AiToolExecutor.cs:809` already serializes `ex.Message` as `{ error: "..." }`. The `SchedulingConflictException` message will propagate. **No changes needed** — just verify in testing.

#### Testing
- Unit test: attempt to book at 2 AM when schedule is 9–5 → should throw
- Unit test: book at 10 AM within schedule → should succeed
- Unit test: book on a day with `IsAvailable = false` → should throw
- Unit test: book during a time block → should throw
- Manual: verify AI assistant returns readable error for out-of-hours booking

---

### Work Package 2: Macro Totals on Detail View (#234)
**Agent:** `dotnet-specialist` (worktree isolation)
**Branch:** `feature/234-macro-totals-detail`
**Closes:** #234

#### Discovery
- `MealPlanEdit.razor` already has: `GetDayCalories()`, `GetDayProtein()`, `GetDayCarbs()`, `GetDayFat()`, `GetDayTotalsClass()`, `GetSlotCalories()`, `GetSlotProtein()` helper methods and a `day-totals` bar
- `MealPlanDetail.razor` (659 lines) shows `day.TotalCalories/TotalProtein/TotalCarbs/TotalFat` from the DTO but **without comparison to targets**
- The `MealPlanDayDto` already pre-computes totals via `MealPlanService.MapToDetailDto()` — no extra computation needed on detail page
- The edit page uses local `EditDay`/`EditSlot`/`EditItem` classes and re-aggregates from entities on render
- The edit page's calorie diff uses a flat ±50 kcal threshold, not percentage-based

#### Step 1: Add target comparison to `MealPlanDetail.razor`
**File:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanDetail.razor`
- The day totals are already displayed — add comparison indicators showing actual vs target
- Use percentage-based thresholds (not flat ±50 kcal):
- Add color-coded indicators:
  - **Green** (`.on-target`): within 5% of target
  - **Amber** (`.near-target`): within 10% of target
  - **Red** (`.off-target`): more than 10% off target
- Apply to all four macros (calories, protein, carbs, fat), not just calories

#### Step 2: Enhance edit page indicators
**File:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanEdit.razor`
- Extend the existing `target-diff` pattern to also show for protein, carbs, fat (currently only shows for calories)
- Use the same green/amber/red classification

#### Step 3: Add/update CSS
**File:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanDetail.razor.css` (or shared CSS)
- `.day-totals` bar styling (can reuse from edit page)
- `.on-target`, `.near-target`, `.off-target` color classes
- Ensure consistency between edit and detail pages

#### Step 4: Extract shared helper (optional)
If both pages need the same percentage-threshold logic, extract a static helper:
```csharp
// Could go in a shared location or just duplicate the 5-line method
static string GetMacroClass(decimal actual, decimal? target)
{
    if (!target.HasValue || target.Value == 0) return "";
    var pct = Math.Abs((actual - target.Value) / target.Value);
    return pct <= 0.05m ? "on-target" : pct <= 0.10m ? "near-target" : "off-target";
}
```

#### Testing
- Manual: create a meal plan with targets, add items, verify totals update on edit page
- Manual: view same plan on detail page, verify totals match
- Manual: verify green/amber/red indicators work correctly for each threshold
- Manual: verify days with no items show `0 / target`

---

### Work Package 3: Session Notes Workflow (#232)
**Agent:** `dotnet-specialist` (worktree isolation)
**Branch:** `feature/232-session-notes-workflow`
**Closes:** #232

This is the most complex work package — it requires a new entity, migration, service changes, and UI changes.

#### Step 1: Create `SessionNote` entity
**File:** `src/Nutrir.Core/Entities/SessionNote.cs`
```csharp
namespace Nutrir.Core.Entities;

public class SessionNote
{
    public int Id { get; set; }
    public int AppointmentId { get; set; }
    public int ClientId { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;

    public bool IsDraft { get; set; } = true;

    // Structured sections
    public string? Notes { get; set; }                    // Session Notes
    public int? AdherenceScore { get; set; }              // 0-100%
    public string? MeasurementsTaken { get; set; }
    public string? PlanAdjustments { get; set; }
    public string? FollowUpActions { get; set; }

    // Soft-delete
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

#### Step 2: Register in `AppDbContext`
**File:** `src/Nutrir.Infrastructure/Data/AppDbContext.cs`
- Add `DbSet<SessionNote> SessionNotes { get; set; }`
- In `OnModelCreating()`:
  - `HasQueryFilter(sn => !sn.IsDeleted)`
  - Index on `AppointmentId` (unique)
  - Index on `ClientId`

#### Step 3: Create EF migration
```bash
dotnet ef migrations add AddSessionNote \
  --project src/Nutrir.Infrastructure \
  --startup-project src/Nutrir.Web
```

#### Step 4: Create `ISessionNoteService` interface
**File:** `src/Nutrir.Core/Interfaces/ISessionNoteService.cs`
```csharp
public interface ISessionNoteService
{
    Task<SessionNoteDto?> GetByIdAsync(int id);
    Task<SessionNoteDto?> GetByAppointmentIdAsync(int appointmentId);
    Task<List<SessionNoteSummaryDto>> GetByClientAsync(int clientId);
    Task<SessionNoteDto> CreateDraftAsync(int appointmentId, int clientId, string userId);
    Task<bool> UpdateAsync(int id, UpdateSessionNoteDto dto, string userId);
    Task<bool> FinalizeAsync(int id, string userId);   // IsDraft = false
    Task<bool> SoftDeleteAsync(int id, string userId);
    Task<List<SessionNoteSummaryDto>> GetMissingNotesAsync(); // Completed appointments without notes
}
```

#### Step 5: Create DTOs
**File:** `src/Nutrir.Core/DTOs/SessionNoteDtos.cs`
- `SessionNoteDto` — full detail
- `SessionNoteSummaryDto` — for lists (id, appointmentId, clientName, date, isDraft, adherenceScore)
- `UpdateSessionNoteDto` — for editing (notes, adherenceScore, measurementsTaken, planAdjustments, followUpActions)

#### Step 6: Implement `SessionNoteService`
**File:** `src/Nutrir.Infrastructure/Services/SessionNoteService.cs`
- Standard CRUD following existing patterns (`IDbContextFactory`, audit logging, notification dispatch)
- `CreateDraftAsync()` — create with `IsDraft = true`, pre-populated with section headers in Notes
- `GetMissingNotesAsync()` — query completed appointments that have no linked session note

#### Step 7: Hook into `AppointmentService.UpdateStatusAsync()`
**File:** `src/Nutrir.Infrastructure/Services/AppointmentService.cs`
- Inject `ISessionNoteService`
- After status persisted, if `newStatus == AppointmentStatus.Completed`:
  ```csharp
  await _sessionNoteService.CreateDraftAsync(id, entity.ClientId, userId);
  ```
- Only create if no existing note for this appointment

#### Step 8: Update `CompleteAppointment()` in UI
**File:** `src/Nutrir.Web/Components/Pages/Appointments/AppointmentDetail.razor`
- After `UpdateStatusAsync()` succeeds, navigate to the session note edit page:
  ```csharp
  NavigationManager.NavigateTo($"/session-notes/{appointmentId}");
  ```

#### Step 9: Create Session Note Edit Page
**File:** `src/Nutrir.Web/Components/Pages/SessionNotes/SessionNoteEdit.razor`
- Route: `/session-notes/{AppointmentId:int}`
- Load the draft session note by appointment ID
- Display structured form with sections:
  - Session Notes (textarea)
  - Client-Reported Adherence (slider or input, 0–100%)
  - Measurements Taken (textarea)
  - Plan Adjustments (textarea)
  - Follow-up Action Items (textarea)
- Save button (keeps as draft) and Finalize button (marks complete)
- Link back to appointment detail

#### Step 10: Add "Notes Missing" indicator
**File:** `src/Nutrir.Web/Components/Pages/Appointments/AppointmentList.razor` or `AppointmentDetail.razor`
- Show a visual indicator (badge or icon) on completed appointments that have no associated session note
- Could be a simple query: if status == Completed and no SessionNote exists, show warning badge

#### Step 11: Register DI
**File:** `src/Nutrir.Web/Program.cs`
- `services.AddScoped<ISessionNoteService, SessionNoteService>()`

#### Testing
- Manual: mark appointment as Complete → verify redirect to session note page
- Manual: verify draft note pre-populated with section template
- Manual: fill in and finalize note, verify it's linked to appointment
- Manual: verify "Notes Missing" indicator appears for completed appointments without notes
- Manual: verify Cancelled appointment does NOT create a draft note

---

## Execution Order & Agent Assignments

```
WP1 (#224/#233) ─── dotnet-specialist ─── worktree isolation
WP2 (#234)      ─── dotnet-specialist ─── worktree isolation
WP3 (#232)      ─── dotnet-specialist ─── worktree isolation
```

**WP1 and WP2 are independent** — can run in parallel.
**WP3 is independent** of WP1/WP2 — can also run in parallel.

All three work packages touch different files and domains:
- WP1: `AppointmentService`, `AvailabilityService`, `IAvailabilityService`
- WP2: `MealPlanEdit.razor`, `MealPlanDetail.razor`, CSS
- WP3: New `SessionNote` entity/service/page, minor touch to `AppointmentService.UpdateStatusAsync` and `AppointmentDetail.razor`

**Potential conflict:** WP1 and WP3 both modify `AppointmentService.cs` — WP1 modifies `CreateAsync`/constructor, WP3 modifies `UpdateStatusAsync`/constructor. These touch different parts of the file but both change the constructor. Merge should be straightforward but verify after both complete.

### Post-Implementation
- **Code review:** `code-reviewer` agent on each PR
- **Close #224:** Add comment explaining it was already implemented, close the issue
- **Update docs:** `docs/scheduling/` with appointment lifecycle including availability enforcement and session notes workflow
