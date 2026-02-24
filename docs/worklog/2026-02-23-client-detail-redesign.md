# Client Detail Page Redesign

**Date:** 2026-02-23
**Branch:** `worktree-client-details-redesign`
**PR:** #16

## Summary

Redesigned the `/clients/{id}` detail page to match the visual standard established by the client list table redesign. Replaced the plain card layout with a hero header, section cards, and staggered entrance animations.

## What Changed

### Markup (`ClientDetail.razor`)

- **Hero header** — 64px avatar circle with initials, client name (`--text-2xl`), contact info rows with inline SVG icons (envelope, phone, calendar, user), consent badge with dot indicator and metadata
- **Action buttons** moved from bottom of page into the hero header (Edit with pencil icon, Delete with trash icon, Back with chevron)
- **Delete confirmation** — now uses warning-tinted border (`color-mix` with `--color-error`), triangle warning SVG icon, displayed inline below hero
- **Notes section** — own `.table-card` with uppercase header bar, only rendered when notes exist
- **Appointments & Meal Plans** — replaced `<Panel>` components with `.table-card` sections using uppercase header bars with counts and action links. Rows display title, meta info, and status badges
- **Metadata footer** — "Created/Updated" timestamps moved outside cards as muted footer text
- **Not-found state** — switched from `<Card>` to `.table-card` for consistency
- Added `GetInitials()` helper (same pattern as client list) and `GetApptStatusVariant()` for appointment status badges

### Styles (`ClientDetail.razor.css`)

Full CSS rewrite:

- `.table-card` wrapper matching client list pattern (`bg-card`, `radius-xl`, `shadow-sm`, border)
- `.hero` — flexbox layout with avatar, info block, and actions
- `.section-header` — uppercase label bar with `bg-alt` background, matching list page header rows
- `.badge-dot` — 6px circle matching client list consent badges
- `.list-row` — clickable rows with hover `bg-alt`, title + meta layout
- `.delete-card` — border tinted with `color-mix(in srgb, var(--color-error) 40%, var(--color-border))`
- Staggered `sectionFadeIn` animation: hero 0ms → notes 80ms → appointments 160ms → meal plans 240ms → footer 320ms
- Responsive: at 768px hero stacks vertically; at 600px avatar centers, padding reduces to `--space-4`
- Page centered with `margin: 0 auto`

### Prototype

- Created `docs/temp/client-detail-prototype.html` — self-contained dark-theme HTML prototype used for visual reference during implementation

## Design Decisions

- **No `<Panel>` or `<Card>` components** — used raw `.table-card` divs with `.section-header` bars instead, matching the client list pattern and avoiding unwanted inner padding from the Card component
- **Actions in hero, not bottom** — reduces scrolling and groups identity + actions together, matching common detail page patterns
- **No backend changes** — all existing services, DTOs, and logic unchanged
