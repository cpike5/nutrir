# ADR-0001: Calendar Component Selection

**Status**: Accepted
**Date**: 2026-03-05

## Context

The scheduling domain requires a calendar UI for practitioners to view, create, and manage appointments. The calendar must support:

- Day, week, and month views
- Click-to-book (slot click) and drag-to-select interactions
- Custom rendering per appointment (color-coding by appointment type)
- Full CSS control to match Nutrir design tokens
- Blazor Server (.NET 9) — server-side rendering with SignalR interactivity

The solution must be maintainable long-term, avoid significant JS interop complexity, and carry no licensing cost at the current scale.

## Decision

Use **Radzen Blazor Scheduler** as the calendar component.

## Options Considered

### Option 1: Radzen Blazor Scheduler (Selected)

| Attribute | Detail |
|-----------|--------|
| License | MIT — free, no attribution requirement |
| Integration | Native Blazor component — no JS interop |
| Views | Day, Week, Month out of the box |
| Interactivity | `SlotSelect` (drag-to-select), `SlotRender`, `AppointmentSelect`, `AppointmentMove` events |
| Custom rendering | `AppointmentTemplate` and `TooltipTemplate` for full control over appointment appearance |
| Theming | `material-base.css` provides an unstyled structural base; all visual styles can be overridden with project CSS variables |
| Maintenance | Actively maintained, large open-source community |

**Strengths**: Drop-in Blazor component, rich event model, template slots for color-coding by type, theming approach aligns with the Nutrir design system.

**Weaknesses**: Scheduler-specific CSS classes must be learned; some advanced layout customizations require inspecting rendered output.

---

### Option 2: FullCalendar.js via JS Interop

FullCalendar is the most widely deployed JS calendar library. It supports all required views and interactions.

**Strengths**: Battle-tested, excellent documentation, broad community.

**Weaknesses**:
- Requires a JS interop bridge (IJSRuntime calls) for every Blazor ↔ calendar interaction (appointment CRUD, view switching, event data updates)
- Data flow becomes bidirectional and error-prone: Blazor state changes must be pushed to JS; JS events must be marshalled back to C#
- No native Blazor lifecycle integration — must manually synchronise component state
- Premium features (resource views, timeline) require a paid licence

This adds substantial complexity to the Blazor Server architecture for no functional gain over a native component.

---

### Option 3: Custom CSS Grid Calendar

Build a calendar from scratch using CSS Grid and Blazor components.

**Strengths**: Zero third-party dependency, total control over markup and behaviour.

**Weaknesses**:
- Drag-to-select, cross-day spanning, and time-slot collision layout require hundreds of lines of event math
- View switching (day/week/month) must be implemented and tested independently
- Keyboard accessibility and screen-reader support require explicit ARIA work
- Maintenance burden falls entirely on this project

Scope is disproportionate to the benefit given that a capable, free, native alternative exists.

## Consequences

- **Dependency added**: `Radzen.Blazor` NuGet package (MIT).
- **Styling**: Import `_content/Radzen.Blazor/css/material-base.css` as the structural base. Override scheduler-specific classes (`.rz-scheduler`, `.rz-slot`, `.rz-appointment`, etc.) in project CSS using Nutrir design tokens. Do not import a Radzen theme file — use the base only.
- **Appointment colour-coding**: Implemented via `AppointmentTemplate` — render a `<div>` with a CSS class or inline style derived from the appointment type enum.
- **Click-to-book**: Wire `SlotSelect` event to open the appointment creation dialog.
- **Data binding**: Pass `IEnumerable<AppointmentDto>` to `Data` parameter; refresh by calling `StateHasChanged` after repository mutations.
- **JS interop**: None required for core functionality.
- **Future views**: Resource/practitioner views are not in v1 scope. Radzen does not currently offer a resource timeline view; if that becomes a requirement it should trigger a re-evaluation of this decision.
