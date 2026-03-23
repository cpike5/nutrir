# Progress Domain — Current State Summary

**Last Updated:** 2026-03-22

## Overview

The progress domain tracks client outcomes, goals, and lifestyle metrics over time. It enables practitioners to document measurable changes, set client objectives, and visualize progress through charts and reports. This domain supports goal-based nutrition care planning and evidence of clinical efficacy.

## Core Entities & Data Model

### Progress Entry Entity

**File:** `/src/Nutrir.Core/Entities/ProgressEntry.cs`

Represents a single progress check-in capturing multiple measurements on a specific date.

**Fields:**

- `Id` (int, PK) — unique entry identifier
- `ClientId` (int, FK) — links to the client
- `CreatedByUserId` (string) — practitioner who recorded the entry
- `EntryDate` (DateOnly) — date measurements were taken (may be historical)
- `Notes` (string, nullable) — observations, context, or client comments (e.g., "Client reports feeling more energetic", "Started new medication")

**Soft-Delete Tracking:**

- `IsDeleted`, `DeletedAt`, `DeletedBy` — follow standard compliance pattern

**Audit Timestamps:**

- `CreatedAt` (DateTime, UTC) — when entry was recorded in system
- `UpdatedAt` (DateTime, nullable, UTC) — last modification time

**Navigation:**

- `Measurements` (List<ProgressMeasurement>) — all metrics captured in this entry

**Key Design Note:** EntryDate is DateOnly (not DateTime) because measurements are typically taken once per day/session, and time-of-day is usually not clinically relevant. Practitioners can record historical entries from past sessions.

### Progress Measurement Entity

**File:** `/src/Nutrir.Core/Entities/ProgressMeasurement.cs`

A single metric within a progress entry (e.g., weight, body fat %, blood pressure).

**Fields:**

- `Id` (int, PK)
- `ProgressEntryId` (int, FK) — parent entry
- `MetricType` (MetricType enum) — predefined metrics or custom
- `CustomMetricName` (string, nullable) — if MetricType = Custom, the name (e.g., "Ankle swelling")
- `Value` (decimal) — the measurement value
- `Unit` (string, nullable) — unit of measure (kg, %, mmHg, cm, etc.)

**Key Design Note:** No timestamp on measurements — they inherit EntryDate from parent entry. This denormalization reduces schema complexity.

### Progress Goal Entity

**File:** `/src/Nutrir.Core/Entities/ProgressGoal.cs`

Represents a nutritional or health goal for a client.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK)
- `CreatedByUserId` (string) — practitioner who created the goal
- `Title` (string) — goal title (e.g., "Lose 5 kg", "Reduce blood pressure to <130/80")
- `Description` (string, nullable) — detailed goal description and rationale
- `GoalType` (GoalType enum) — Weight, BodyComposition, Dietary, or Custom
- `TargetValue` (decimal, nullable) — numeric target (e.g., 85 for 85 kg weight goal)
- `TargetUnit` (string, nullable) — unit for target (kg, %, etc.)
- `TargetDate` (DateOnly, nullable) — when goal should be achieved
- `Status` (GoalStatus enum) — Active, Achieved, or Abandoned

**Soft-Delete Tracking:**

- `IsDeleted`, `DeletedAt`, `DeletedBy`

**Audit Timestamps:**

- `CreatedAt`, `UpdatedAt`

**Key Design Note:** Goals are independent entities not directly linked to Measurements. Practitioners manually track whether goals are being met via progress entry notes and visual inspection of charts.

### Enums

**MetricType** (`/src/Nutrir.Core/Enums/MetricType.cs`)

Predefined progress metrics:

- `Weight` — body weight
- `BodyFatPercentage` — body composition
- `WaistCircumference` — waist measurement
- `HipCircumference` — hip measurement
- `BMI` — body mass index
- `BloodPressureSystolic` — systolic BP
- `BloodPressureDiastolic` — diastolic BP
- `RestingHeartRate` — resting heart rate
- `Custom` — any user-defined metric

**GoalType** (`/src/Nutrir.Core/Enums/GoalType.cs`)

Categories of goals:

- `Weight` — weight loss/gain
- `BodyComposition` — fat loss, muscle gain
- `Dietary` — behavior change (e.g., increase fiber, reduce sugar)
- `Custom` — any other goal

**GoalStatus** (`/src/Nutrir.Core/Enums/GoalStatus.cs`)

Goal lifecycle states:

- `Active` — currently being worked toward
- `Achieved` — goal met
- `Abandoned` — goal discontinued (changed or client withdrew consent)

## Data Transfer Objects (DTOs)

All DTOs located in `/src/Nutrir.Core/DTOs/`.

### Progress Entry DTOs

#### CreateProgressEntryDto

Input for creating a new entry.

```csharp
record CreateProgressEntryDto(
    int ClientId,
    DateOnly EntryDate,
    string? Notes,
    List<CreateProgressMeasurementDto> Measurements);

record CreateProgressMeasurementDto(
    MetricType MetricType,
    string? CustomMetricName,
    decimal Value,
    string? Unit);
```

#### UpdateProgressEntryDto

Input for updating an entry.

```csharp
record UpdateProgressEntryDto(
    DateOnly EntryDate,
    string? Notes,
    List<CreateProgressMeasurementDto> Measurements);
```

#### ProgressEntrySummaryDto

List/card view.

```csharp
record ProgressEntrySummaryDto(
    int Id,
    int ClientId,
    DateOnly EntryDate,
    int MeasurementCount,
    string? NotePreview,
    DateTime CreatedAt);
```

#### ProgressEntryDetailDto

Full entry with all measurements.

```csharp
record ProgressEntryDetailDto(
    int Id,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    DateOnly EntryDate,
    string? Notes,
    List<ProgressMeasurementDto> Measurements,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

record ProgressMeasurementDto(
    int Id,
    MetricType MetricType,
    string? CustomMetricName,
    decimal Value,
    string? Unit);
```

### Progress Goal DTOs

#### CreateProgressGoalDto

Input for goal creation.

```csharp
record CreateProgressGoalDto(
    int ClientId,
    string Title,
    string? Description,
    GoalType GoalType,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate);
```

#### UpdateProgressGoalDto

Input for goal update.

```csharp
record UpdateProgressGoalDto(
    string Title,
    string? Description,
    GoalType GoalType,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate);
```

#### ProgressGoalSummaryDto

List/card view.

```csharp
record ProgressGoalSummaryDto(
    int Id,
    int ClientId,
    string Title,
    GoalType GoalType,
    GoalStatus Status,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate,
    DateTime CreatedAt);
```

#### ProgressGoalDetailDto

Full goal view.

```csharp
record ProgressGoalDetailDto(
    int Id,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    string Title,
    string? Description,
    GoalType GoalType,
    GoalStatus Status,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
```

### Chart Data DTO

#### ProgressChartDataDto

Used to render weight/metric trend charts.

Contains:

- `MetricType` — which metric to chart
- `DataPoints` — array of (Date, Value) tuples
- `TargetValue` (optional) — goal line on chart
- `GoalTargetDate` (optional) — target date line

## Service Layer

### IProgressService & ProgressService

**File:** `/src/Nutrir.Infrastructure/Services/ProgressService.cs`

Manages entries, measurements, and goals.

#### Entry Methods

- **GetEntryByIdAsync(id)** — fetch single entry with measurements
- **GetEntriesByClientAsync(clientId)** — fetch all entries for a client, most recent first
- **GetPagedAsync(ProgressEntryListQuery)** — paginated entries with sorting/filtering
- **CreateEntryAsync(CreateProgressEntryDto, userId)** — create entry with measurements, audit log, return DTO
- **UpdateEntryAsync(id, UpdateProgressEntryDto, userId)** — update entry date/notes/measurements
- **SoftDeleteEntryAsync(id, userId)** — soft-delete entry, audit log
- **RestoreEntryAsync(id, userId)** — restore soft-deleted entry

#### Goal Methods

- **GetGoalByIdAsync(id)** — fetch single goal
- **GetGoalsByClientAsync(clientId)** — fetch all goals for a client
- **GetActiveGoalsAsync(clientId)** — fetch only Active status goals
- **CreateGoalAsync(CreateProgressGoalDto, userId)** — create goal, audit log
- **UpdateGoalAsync(id, UpdateProgressGoalDto, userId)** — update goal fields
- **UpdateGoalStatusAsync(id, newStatus, userId)** — transition goal status
- **SoftDeleteGoalAsync(id, userId)** — soft-delete goal

#### Chart/Analytics Methods

- **GetChartDataAsync(clientId, metricType)** — return all entries with that metric over time, for charting
- **GetClientProgressSummaryAsync(clientId)** — aggregated stats:
  - Total entries recorded
  - Date range of measurements
  - List of active goals
  - Recent entries preview

#### Implementation Notes

- `IDbContextFactory<AppDbContext>` for paged queries (concurrency)
- Direct context for single-entity operations
- Audit logging on every mutation
- `INotificationDispatcher` notified for real-time updates
- `IRetentionTracker` called to update `Client.LastInteractionDate`
- Entry dates can be historical (no validation that EntryDate ≤ today)

## UI Pages & Components

**Current Status:** Progress entry and goal pages likely exist; routing status varies.

### ProgressEntry List Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressEntryList.razor` (presumed)

Lists all entries for a client.

- Card or table view with entry date, measurement count, note preview
- Filter by client
- Sort by date (newest first)
- Link to entry detail
- "New Entry" button

### ProgressEntry Detail Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressEntryDetail.razor` (presumed)

Shows single entry.

- Entry date, notes
- All measurements listed with type, value, unit
- Edit button
- Delete button
- Back to list

### ProgressEntry Create/Edit Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressEntryCreate.razor` or inline modal (presumed)

Form for creating/editing entry.

- Client selector (dropdown or pre-selected)
- Entry date picker
- Notes textarea
- Dynamic measurement list:
  - Metric type selector (Weight, BMI, Custom)
  - Value input
  - Unit input
  - Add/remove measurement buttons
- Submit and cancel buttons

### ProgressGoal List Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressGoalList.razor` (presumed)

Lists all goals for a client.

- Card view with title, type, status badge, target value/date
- Filter by status (Active, Achieved, Abandoned)
- Sort options
- Link to goal detail
- "New Goal" button

### ProgressGoal Detail Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressGoalDetail.razor` (presumed)

Shows single goal.

- Goal title, description, type, status badge
- Target value, unit, target date
- Edit button
- Status change dropdown (Active → Achieved or Abandoned)
- Delete button

### ProgressGoal Create/Edit Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressGoalCreate.razor` (presumed)

Form for creating/editing goal.

- Client selector
- Title input
- Description textarea
- Goal type selector
- Target value, unit inputs (optional)
- Target date picker (optional)
- Submit and cancel buttons

### Progress Dashboard / Analytics Page

**Path:** `src/Nutrir.Web/Components/Pages/Progress/ProgressDashboard.razor` (presumed)

Overview of client progress:

- Active goals display
- Recent entries timeline
- Metric trend charts (weight, body fat %, etc.)
- "Add Entry" quick button
- "Create Goal" quick button

## Known Issues & Future Work

### High Priority (v1 Scope Completion)

1. **Goal-Entry Linking** — no automatic connection between goals and entries
   - **Current:** Goals are standalone; practitioners manually track progress
   - **Fix:** Add optional `RelatedGoalId` to entry, allow filtering entries by goal

2. **Chart Rendering** — no visualization of metric trends over time
   - **Fix:** Implement `GetChartDataAsync()` and use Radzen/ChartJS to render line charts

3. **Goal Completion Calculation** — no auto-detection when goal is achieved
   - **Current:** Practitioners manually update status to Achieved
   - **Fix:** Add logic to check if latest measurement meets target value (for weight, BMI, etc.)

4. **Progress Report Export** — no ability to generate progress summary for client or records
   - **Fix:** Create `ProgressReportService` to generate PDF/email-friendly progress summary

### Medium Priority (v2+)

- **Comparative analysis** — compare current vs. initial weight, rate of change, etc.
- **Milestone tracking** — intermediate waypoints toward goal (e.g., "5 kg per month")
- **Prediction** — estimate time to goal based on current trend
- **Custom metrics** — UI for defining custom metrics beyond predefined list
- **Biofeedback integration** — import data from Fitbit, Apple Health, Garmin
- **Client self-entry** — allow clients to log measurements via portal

## Database Migrations

**Base Migration:** `20260218123456_AddProgress.cs` (presumed)

Creates progress tables:

- `ProgressEntries` — FK to Clients
- `ProgressMeasurements` — FK to ProgressEntries
- `ProgressGoals` — FK to Clients

**Indexes:**

- `ProgressEntries(ClientId, EntryDate)` for time-series queries
- `ProgressMeasurements(ProgressEntryId, MetricType)` for metric filtering
- `ProgressGoals(ClientId, Status)` for active goal queries

## Documentation & Standards

### Where to Add New Docs

All progress documentation goes in `/docs/progress/`.

**Existing documents:**

- `domain-summary.md` — this file

**Expected documents (not yet created):**

- `adr-0001-goal-entry-linking.md` — decision on how entries relate to goals
- `chart-rendering-spec.md` — spec for metric trend visualization
- `progress-report-spec.md` — design for progress summary export

### Conventions

- All times stored as UTC
- Entry dates are DateOnly (no time component)
- Goal target dates are DateOnly (no time component)
- DTOs denormalize client names and creator names
- Measurements inherit entry date (no separate timestamp)
- Custom metrics allowed via MetricType.Custom and CustomMetricName field

## External Dependencies

- **Clients domain** — entries and goals scoped to clients
- **Compliance domain** — soft-delete and audit logging follow compliance standards
- **Auth domain** — `CreatedByUserId` must be valid `ApplicationUser`

## Queries Used Across the App

Progress entities queried in:

1. **Progress List/Detail Pages** — entries and goals for a specific client
2. **Client Detail Page** — recent entries and active goals summary
3. **Dashboard** — active goals count, recent entries
4. **Search Results** — entries may be indexed
5. **AI Assistant Tools** — list/get/create/update operations (presumed)

---

## Summary of Current State

**Complete:**

- Core progress entry, measurement, and goal entity models
- Full service layer with CRUD for entries and goals
- DTOs for all operations and views
- Chart data DTO for trend visualization

**Missing / Incomplete:**

- Entry-to-goal linking (entries and goals are unconnected)
- Metric trend chart rendering
- Auto-detection of goal completion
- Progress report/export generation
- Client self-entry capability
- Biofeedback integrations

**Next Steps for Implementation:**

1. Create `/docs/progress/adr-0001-goal-entry-linking.md`
2. Implement `GetChartDataAsync()` and render charts in dashboard
3. Add goal completion auto-detection logic
4. Create `ProgressReportService` for PDF export
5. Wire progress pages into main navigation if not already done
6. Consider adding `RelatedGoalId` field to ProgressEntry entity if goal linking is prioritized
