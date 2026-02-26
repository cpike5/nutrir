# Calendar View — Implementation Spec

**Status:** Draft
**Date:** 2026-02-26
**Scope:** Proof of concept — read-only calendar view of existing appointments

## Goal

Add a calendar view to the Appointments section using the [Radzen Blazor Scheduler](https://blazor.radzen.com/scheduler) component. This PoC focuses on **viewing** appointments in day/week/month layouts. Scheduling via the calendar (click-to-create, drag-to-reschedule) is out of scope for this phase.

## Why Radzen

- MIT license, free, no revenue restrictions
- Mature Blazor component library (100+ components, v7.x)
- Scheduler supports day/week/month/timeline views out of the box
- Drag-and-drop, slot click, and templates available for future phases
- Keyboard accessible (WCAG-compliant)
- Themeable — can override CSS to match Nutrir's design tokens

## Scope

### In Scope (PoC)
- Install and configure `Radzen.Blazor` NuGet package
- New page: `/appointments/calendar`
- Render appointments in `RadzenScheduler` with day, week, and month views
- Color-code appointments by type (InitialConsultation, FollowUp, CheckIn)
- Show appointment details on click (read-only popup or navigate to detail page)
- Toggle between list view (`/appointments`) and calendar view (`/appointments/calendar`)
- Override Radzen theme CSS to align with Nutrir design tokens
- Responsive behavior at existing breakpoints (1100px, 768px)

### Out of Scope (Future)
- Click-to-create appointments from calendar slots
- Drag-and-drop rescheduling
- Recurring appointments
- Multi-nutritionist resource view
- External calendar sync (Google, Outlook)
- Overlap detection (tracked separately)

## Technical Design

### 1. Package Installation

Add to `src/Nutrir.Web/Nutrir.Web.csproj`:

```xml
<PackageReference Include="Radzen.Blazor" Version="7.*" />
```

### 2. Global Configuration

**`_Imports.razor`** — add namespace:

```razor
@using Radzen
@using Radzen.Blazor
```

**`App.razor`** — add inside `<head>`:

```html
<link rel="stylesheet" href="_content/Radzen.Blazor/css/material-base.css">
```

> We use `material-base.css` (unstyled base) rather than a full Radzen theme so we can apply our own design tokens without fighting Radzen's defaults.

**`App.razor`** — add before closing `</body>`:

```html
<script src="_content/Radzen.Blazor/Radzen.Blazor.js"></script>
```

**No `<RadzenTheme>` component** — we'll handle theming through CSS overrides to stay consistent with Nutrir's design system.

### 3. New Page: `AppointmentCalendar.razor`

**Route:** `/appointments/calendar`
**Render mode:** `@rendermode InteractiveServer` (required for Radzen interactivity)

```razor
@page "/appointments/calendar"
@rendermode InteractiveServer
@attribute [Authorize]

@inject IAppointmentService AppointmentService
@inject NavigationManager Navigation
```

#### Component Structure

```
┌─────────────────────────────────────────────────┐
│  Appointments          [List] [Calendar]        │  ← Page header + view toggle
├─────────────────────────────────────────────────┤
│  ┌─────────────────────────────────────────────┐│
│  │  < Jan 2026  >    [Day] [Week] [Month]     ││  ← Radzen scheduler header
│  │─────────────────────────────────────────────││
│  │  Mon   Tue   Wed   Thu   Fri   Sat   Sun   ││
│  │  ...   ...   ...   ...   ...   ...   ...   ││
│  │  ...  [Appt] ...  [Appt] ...   ...   ...   ││  ← Appointment blocks
│  │  ...   ...  [Appt] ...   ...   ...   ...   ││
│  │  ...   ...   ...   ...   ...   ...   ...   ││
│  └─────────────────────────────────────────────┘│
└─────────────────────────────────────────────────┘
```

#### Core Markup

```razor
<RadzenScheduler @ref="scheduler"
                 TItem="AppointmentDto"
                 Data="@appointments"
                 StartProperty="StartTime"
                 EndProperty="EndTime"
                 TextProperty="DisplayText"
                 AppointmentSelect="@OnAppointmentSelect"
                 LoadData="@OnLoadData"
                 Style="height: 700px;">
    <RadzenDayView />
    <RadzenWeekView />
    <RadzenMonthView />
</RadzenScheduler>
```

#### Key Properties

| Radzen Property | Maps To | Notes |
|-----------------|---------|-------|
| `StartProperty` | `AppointmentDto.StartTime` | Already UTC DateTime |
| `EndProperty` | `AppointmentDto.EndTime` | Computed property on DTO |
| `TextProperty` | Custom `DisplayText` | See display text section |

### 4. Data Loading

Use the existing `IAppointmentService.GetListAsync()` with date-range filtering:

```csharp
private List<AppointmentDto> appointments = new();
private RadzenScheduler<AppointmentDto> scheduler = default!;

private async Task OnLoadData(SchedulerLoadDataEventArgs args)
{
    // Radzen passes the visible date range — use it to query only what's needed
    appointments = await AppointmentService.GetListAsync(
        fromDate: args.Start,
        toDate: args.End);
}
```

This leverages the existing service method with its `fromDate`/`toDate` parameters. No new service methods are required.

### 5. Appointment Display Text

Add a helper to format what shows on the calendar block:

```csharp
private string GetDisplayText(AppointmentDto apt)
{
    var time = apt.StartTime.ToLocalTime().ToString("h:mm tt");
    return $"{time} — {apt.ClientFirstName} {apt.ClientLastName}";
}
```

Since `TextProperty` needs a string property on the DTO, we have two options:

**Option A — ViewModel wrapper** (preferred for PoC):

```csharp
private class CalendarAppointment
{
    public AppointmentDto Dto { get; init; } = default!;
    public string DisplayText => $"{Dto.StartTime.ToLocalTime():h:mm tt} — {Dto.ClientFirstName} {Dto.ClientLastName}";
    public DateTime StartTime => Dto.StartTime;
    public DateTime EndTime => Dto.EndTime;
}
```

**Option B — Use `Template` rendering** (more flexible, use if Option A feels limiting):

```razor
<RadzenScheduler TItem="AppointmentDto" ...>
    <Template Context="apt">
        <div class="appointment-block appointment-@apt.Type.ToString().ToLower()">
            <strong>@apt.ClientFirstName @apt.ClientLastName</strong>
            <span>@apt.Type.ToDisplayString()</span>
        </div>
    </Template>
    ...
</RadzenScheduler>
```

**Recommendation:** Start with Option A for simplicity. Move to Option B if we want richer appointment block rendering (icons, badges, etc.).

### 6. Color Coding by Appointment Type

Apply distinct colors per `AppointmentType` using Radzen's `AppointmentRender` event:

```csharp
private void OnAppointmentRender(SchedulerAppointmentRenderEventArgs<AppointmentDto> args)
{
    var apt = args.Data;

    args.Attributes["style"] = apt.Type switch
    {
        AppointmentType.InitialConsultation => "background-color: var(--color-primary);",
        AppointmentType.FollowUp           => "background-color: var(--color-secondary);",
        AppointmentType.CheckIn             => "background-color: var(--color-accent);",
        _ => ""
    };
}
```

**Color mapping:**

| Type | Token | Color | Rationale |
|------|-------|-------|-----------|
| InitialConsultation | `--color-primary` | #b0687d (mauve pink) | Primary/important — first meeting |
| FollowUp | `--color-secondary` | #5c4d59 (plum) | Secondary — routine |
| CheckIn | `--color-accent` | #a59e9d (warm taupe) | Subtle — brief check-in |

**Cancelled/NoShow** appointments: render with reduced opacity (0.5) and strikethrough text.

### 7. Appointment Click Handler

Navigate to the existing detail page on click:

```csharp
private void OnAppointmentSelect(SchedulerAppointmentSelectEventArgs<AppointmentDto> args)
{
    Navigation.NavigateTo($"/appointments/{args.Data.Id}");
}
```

No popup/modal for the PoC — reuse the existing `AppointmentDetail.razor` page.

### 8. View Toggle (List ↔ Calendar)

Add a toggle to both the list and calendar pages:

```razor
<div class="view-toggle">
    <a href="/appointments" class="view-toggle-btn @(isCalendar ? "" : "active")">
        <!-- List icon SVG -->
        List
    </a>
    <a href="/appointments/calendar" class="view-toggle-btn @(isCalendar ? "active" : "")">
        <!-- Calendar icon SVG -->
        Calendar
    </a>
</div>
```

Style as pill-toggle buttons consistent with Nutrir's design:

```css
.view-toggle {
    display: inline-flex;
    gap: var(--space-1);
    background: var(--color-bg-alt);
    border-radius: var(--radius-md);
    padding: var(--space-1);
}

.view-toggle-btn {
    padding: var(--space-2) var(--space-4);
    border-radius: var(--radius-sm);
    font-size: var(--text-sm);
    font-weight: 500;
    color: var(--color-text-muted);
    text-decoration: none;
    transition: all var(--duration-fast) var(--ease-default);
}

.view-toggle-btn.active {
    background: var(--color-bg-card);
    color: var(--color-text);
    box-shadow: var(--shadow-sm);
}
```

### 9. Radzen CSS Overrides

Create `AppointmentCalendar.razor.css` (CSS isolation) with overrides to align Radzen's scheduler with Nutrir's design system:

```css
/* Target Radzen scheduler via ::deep for CSS isolation */

::deep .rz-scheduler {
    font-family: var(--font-sans);
    border: 1px solid var(--color-border);
    border-radius: var(--radius-xl);
    overflow: hidden;
    box-shadow: var(--shadow-sm);
    background: var(--color-bg-card);
}

::deep .rz-scheduler-header {
    background: var(--color-bg-alt);
    border-bottom: 1px solid var(--color-border);
    font-family: var(--font-display);
}

::deep .rz-scheduler-nav-today {
    background: var(--color-primary);
    color: white;
    border-radius: var(--radius-md);
}

::deep .rz-event {
    border-radius: var(--radius-sm);
    font-size: var(--text-xs);
    font-weight: 500;
    border: none;
}

::deep .rz-scheduler-view-day .rz-event,
::deep .rz-scheduler-view-week .rz-event {
    font-size: var(--text-sm);
}

/* Today's column highlight */
::deep .rz-today {
    background: rgba(var(--rgb-primary), 0.05);
}

/* Time slot borders */
::deep .rz-slot {
    border-color: var(--color-border);
}
```

> **Note:** The exact Radzen CSS class names should be verified against the installed version. Inspect the rendered DOM during development and adjust selectors as needed.

### 10. Responsive Behavior

```css
/* Tablet: reduce scheduler height */
@media (max-width: 1100px) {
    ::deep .rz-scheduler {
        height: 600px !important;
    }
}

/* Mobile: force day view, reduce height */
@media (max-width: 768px) {
    ::deep .rz-scheduler {
        height: 500px !important;
    }
}
```

On mobile, the day view is the most usable. Consider programmatically switching to day view on small screens via a `MediaQuery` check.

### 11. Navigation Update

No sidebar changes needed — the existing "Appointments" nav item at `/appointments` stays. The calendar is accessed via the view toggle on the appointments page. Both views share the same nav highlight.

## File Changes Summary

| File | Action | Description |
|------|--------|-------------|
| `Nutrir.Web.csproj` | Edit | Add `Radzen.Blazor` NuGet reference |
| `_Imports.razor` | Edit | Add `@using Radzen` and `@using Radzen.Blazor` |
| `App.razor` | Edit | Add Radzen CSS + JS references |
| `AppointmentCalendar.razor` | **New** | Calendar view page |
| `AppointmentCalendar.razor.css` | **New** | Scoped CSS overrides for Radzen |
| `AppointmentList.razor` | Edit | Add view toggle header |

## Acceptance Criteria

1. `/appointments/calendar` renders a Radzen Scheduler with month view as default
2. Day, week, and month views are available and switchable
3. Existing appointments appear on the calendar with correct date/time positioning
4. Appointments are color-coded by type (3 distinct colors using design tokens)
5. Clicking an appointment navigates to `/appointments/{id}` (existing detail page)
6. View toggle (List/Calendar) appears on both `/appointments` and `/appointments/calendar`
7. Calendar uses Nutrir design tokens (fonts, colors, borders, radii) — not default Radzen styling
8. Page requires authentication (`[Authorize]`)
9. Data loads efficiently — only fetches appointments in the visible date range
10. Works at desktop (1200px+), tablet (768-1100px), and mobile (<768px) widths

## Future Phases

Once the PoC is validated:

1. **Phase 2 — Click to create**: Click empty slot → pre-fill date/time → navigate to create page (or inline form)
2. **Phase 3 — Drag to reschedule**: Drag appointment to new slot → call `UpdateAsync` with new time
3. **Phase 4 — Richer rendering**: Status badges on blocks, tooltip on hover, cancelled appointment styling
4. **Phase 5 — Resource view**: Multi-nutritionist timeline view when multi-practitioner support is added
