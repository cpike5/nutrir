# Appointment Pages Redesign

**Date:** 2026-02-23
**Branch:** `worktree-appointments-redesign`

## Summary

Redesigned the appointment list and create pages to match the visual standard from the client pages redesign. Added collapsible form sections to the create page for reduced visual clutter.

## What Changed

### Appointment List (`AppointmentList.razor` / `.razor.css`)

- Replaced plain table with `.table-card` layout using avatar circles, inline SVG icons, and status badges
- Added staggered `sectionFadeIn` entrance animations
- Row layout with client avatar initials, appointment type/time meta, and status indicators

### Appointment Create — Sectioned Form (`AppointmentCreate.razor` / `.razor.css`)

- Reorganized form into 4 sections (Client, Schedule, Location, Notes) with `.section-header` bars and `.form-body` content areas
- Each section header has an icon SVG and uppercase label

### Appointment Create — Collapsible Sections

- Added `_expandedSections` HashSet tracking which sections are open (all expanded by default)
- `ToggleSection(string section)` method adds/removes from the set on header click
- Each `.section-header` has `@onclick` and a chevron SVG (`section-chevron`) that rotates on toggle
- `.form-body` conditionally rendered via `@if (_expandedSections.Contains(...))` — fields retain bound values when collapsed
- CSS: `cursor: pointer`, `user-select: none`, hover background via `color-mix`, chevron rotation transition (`-90deg` when collapsed)

## Design Decisions

- **Conditional render vs CSS `display:none`** — used `@if` to remove collapsed sections from the DOM entirely. Form fields are bound to `_model` so values persist regardless of render state. This keeps the DOM lighter and avoids hidden-field edge cases with validation.
- **Single-quote HTML attributes for `@onclick`** — Razor `@onclick="..."` with double-quoted C# string arguments causes parse errors. Used single-quoted attributes (`@onclick='() => ToggleSection("Client")'`) to avoid escaping.
- **All sections expanded by default** — new users see the full form on first load; returning users can collapse sections they don't need.

## Lessons Learned

- **Razor attribute quoting** — `@onclick="() => Method("arg")"` fails because the inner double quotes terminate the attribute. Use single-quoted attributes (`@onclick='...'`) when the C# expression contains string literals.
