# Client Table Redesign

**Date:** 2026-02-23
**Branch:** `worktree-client-table-redesign`
**Commit:** `0b7cf12`

## Summary

Redesigned the `/clients` page table from a plain HTML table to a richer, interactive design based on an approved prototype (`docs/temp/client-table-prototype.html`).

## What Changed

### Markup (`ClientList.razor`)

- **Add Client button** now includes an SVG user-plus icon
- **Search bar** replaced `FormGroup`/`FormInput` wrapper with an inline search input featuring an embedded SVG search icon and a live client count indicator
- **Table wrapper** switched from `<Card>` component to a custom `.table-card` div (Card added unwanted inner padding)
- **Identity cell** — circular avatar with initials (first+last) alongside stacked name/email
- **Consent column** — `<Badge>` component with dot indicator (Success = Given, Warning = Pending)
- **Action buttons** — icon-only (eye for View, pencil for Edit) using `<a>` tags, visible on row hover
- **Empty state** — SVG illustration above the "No clients found" message
- Skipped Status column and filter buttons (no `Status` field on entity — only `IsDeleted`, and the service already filters deleted clients)

### Styles (`ClientList.razor.css`)

Full restyle ported from the prototype:

- `.table-card` — white bg, `radius-xl`, `shadow-sm`, border, overflow hidden
- Header row — `bg-alt`, uppercase, muted text, `tracking-wider`
- Avatar — 38px circle, `primary-muted` bg, scales to 1.08 on row hover
- Row hover — `bg-alt` background + 3px primary left border accent
- Action buttons — `opacity: 0` → `1` on hover (always visible on mobile)
- Staggered `rowFadeIn` entrance animation (30ms delay per row, up to 10)
- Responsive breakpoints: hide phone/nutritionist at 860px, consent/date at 600px

### Documentation

- Created `docs/design-system/data-tables.md` — table styling conventions for reuse across future tables
- Updated `docs/README.md` — added data-tables doc link

## Bug Fix: Table Header Misalignment

**Problem:** Column headers were visually misaligned with their data cells.

**Root cause:** The row hover left-border accent used `tr::before` with `position: absolute`, which required `position: relative` on `<tr>`. However, `position: relative` on `<tr>` elements has inconsistent behavior across browsers and was causing layout shifts that pushed data cells out of alignment with headers.

**Fix:** Moved the `::before` pseudo-element from `tbody tr` to `tbody td:first-child`. Since `<td>` handles `position: relative` reliably, the accent renders correctly without affecting table layout.

**Additional note:** During debugging, discovered that Docker's `dotnet publish` generates pre-compressed `.br` and `.gz` versions of static assets. These compressed files are cached across Docker build layers, so CSS changes may not appear even with browser cache disabled. A `docker compose build --no-cache` is needed to regenerate them.
