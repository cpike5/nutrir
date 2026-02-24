# Global Search — UX Design Specification

Design document for the global search feature. Covers interaction model, visual design, component anatomy, states, keyboard behavior, and implementation notes for Blazor.

Prototype: `docs/temp/global-search-prototype.html`

---

## Decision Summary

| Question | Decision | Rationale |
|---|---|---|
| Dropdown vs. full page | Dropdown overlay | Maintains context; professionals need to glance and navigate, not leave current work |
| Result grouping | By entity type, in fixed order | Reduces cognitive parsing; users know where to look |
| Keyboard shortcut | Ctrl+K / Cmd+K | Industry standard; already has `.cc-kbd` component in the status bar |
| Initial state (empty) | Recent searches (last 5) | Reduces keystrokes for repeat lookups; practitioners search the same clients repeatedly |
| Dismiss behavior | Click outside, Escape, or selecting a result | All three; Escape returns focus to the input briefly then blurs on second press |
| Mobile behavior | Full-screen overlay on tap | Dropdown is too narrow on mobile; full-screen gives enough room to read results |
| Loading state | Inline skeleton rows, not spinner | Maintains layout stability; no jarring size change |
| Max results shown | 3 per section, 9 total | Keeps dropdown compact; a "See all N results" footer per section handles overflow |

---

## Interaction Model

### Trigger

- Keyboard shortcut **Ctrl+K** (Windows/Linux) or **Cmd+K** (macOS) focuses the `.cc-search input` from anywhere in the app.
- Clicking the search input also opens the dropdown.
- The input expands from `200px` to `260px` on focus (already implemented in `.cc-search input:focus`). The dropdown anchors to the expanded input width minimum, but has its own `min-width: 420px`.

### Typing Behavior

- **Debounce: 250ms** after the last keystroke before firing the search query.
- Minimum query length: **2 characters**. Below 2 chars, show the Recent Searches panel instead.
- Results are live-updated as the user types (no submit button, no Enter-to-search).

### Dismiss

| Action | Result |
|---|---|
| Click outside dropdown | Dismiss, blur input |
| Press Escape (first press) | Clear input text if any; if input already empty, dismiss and blur |
| Press Escape (second press, input focused) | Blur input |
| Select a result (click or Enter) | Navigate to result, dismiss dropdown |
| Press Tab | Move focus to first result row; Tab again cycles through results; Tab past last result dismisses and moves to next topbar element |

---

## Dropdown Anatomy

```
┌─────────────────────────────────────────────────┐
│  [Search icon]  [input text...]        [⌘K] [✕] │  ← search input (existing .cc-search)
└───────────┬─────────────────────────────────────┘
            │
┌───────────▼─────────────────────────────────────┐  ← .search-dropdown
│                                                  │
│  CLIENTS  ──────────────────────────────  3 of 7 │  ← .search-section-header
│  [👤] Sarah Mitchell          Active      →      │  ← .search-result-row
│  [👤] Mike Thompson           Inactive    →      │
│  [👤] Priya Sharma            Active      →      │
│       See all 7 clients                          │  ← .search-section-footer
│                                                  │
│  APPOINTMENTS  ─────────────────────────  2 of 2 │
│  [📅] Initial Consult · Sarah Mitchell   Mar 2   │
│  [📅] Follow-up · Mike Thompson          Mar 9   │
│                                                  │
│  MEAL PLANS  ───────────────────────────  1 of 1 │
│  [🥗] Mediterranean Week 1 · Sarah Mitchell      │
│                                                  │
└──────────────────────────────────────────────────┘
```

### Dropdown Dimensions

| Property | Value |
|---|---|
| `min-width` | `420px` |
| `max-width` | `560px` |
| `max-height` | `480px` |
| `overflow-y` | `auto` (only scrolls the results area, not section headers) |
| `border-radius` | `var(--radius-lg)` — `0.75rem` |
| `box-shadow` | `var(--shadow-lg)` — `0 8px 30px rgba(0,0,0,0.10)` |
| Background | `var(--color-bg-card)` — `#ffffff` |
| Border | `1px solid var(--color-border)` |
| `z-index` | `1000` (above topbar which is sticky) |
| Position | `absolute`, anchored to the right edge of `.cc-search` |

Right-anchor the dropdown to align with the search input's right edge. This prevents the dropdown from overflowing the right edge of the viewport.

---

## Result Row Design

### Clients

```
┌─────────────────────────────────────────────────────┐
│  [SM]  Sarah Mitchell                    Active  →  │
│        sarah.mitchell@email.com                     │
└─────────────────────────────────────────────────────┘
```

| Element | Spec |
|---|---|
| Avatar | 28px circle, `var(--color-primary-muted)` bg, `var(--color-primary)` text, `font-display`, initials. Matches `.cc-client-avatar`. |
| Primary text | Client full name. `font-size: var(--text-sm)`, `font-weight: 500`, `color: var(--color-text)`. |
| Secondary text | Email address. `font-size: var(--text-xs)`, `color: var(--color-text-muted)`. Truncate with ellipsis if too long. |
| Status badge | `badge badge-success` for Active, `badge badge-accent` for Inactive. |
| Arrow icon | HeroIcon `chevron-right`, 14px, `color: var(--color-accent)`. Hidden until row hover, then visible. |
| Link target | `/clients/{id}` |

### Appointments

```
┌─────────────────────────────────────────────────────┐
│  [📅]  Initial Consult                 Scheduled →  │
│        Sarah Mitchell · Mar 2, 2026, 10:00 AM       │
└─────────────────────────────────────────────────────┘
```

| Element | Spec |
|---|---|
| Icon | 28px circle, `var(--color-secondary-muted)` bg, calendar HeroIcon in `var(--color-secondary)`. 16px icon. |
| Primary text | Appointment type/title. `font-weight: 500`. |
| Secondary text | Client name · Date/time in local timezone, format `MMM d, yyyy h:mm a`. |
| Status badge | `badge-success` Scheduled, `badge-accent` Completed, `badge-error` Cancelled. |
| Link target | `/appointments/{id}` |

### Meal Plans

```
┌─────────────────────────────────────────────────────┐
│  [🥗]  Mediterranean Week 1          Draft      →   │
│        Sarah Mitchell · Created Feb 20, 2026        │
└─────────────────────────────────────────────────────┘
```

| Element | Spec |
|---|---|
| Icon | 28px circle, `var(--color-accent-muted)` bg, clipboard HeroIcon in `var(--color-text-muted)`. 16px icon. |
| Primary text | Meal plan name. `font-weight: 500`. |
| Secondary text | Assigned client · Created date (local timezone). |
| Status badge | `badge-primary` Active, `badge-warning` Draft, `badge-accent` Archived. |
| Link target | `/meal-plans/{id}` |

### Row Interaction States

| State | Visual |
|---|---|
| Default | White background |
| Hover | `background: rgba(var(--rgb-primary), 0.03)` — matches `.cc-list-item:hover` |
| Keyboard focused | `background: rgba(var(--rgb-primary), 0.06)`, `outline: none` (focus is managed by JS) |
| Active/pressed | `background: rgba(var(--rgb-primary), 0.10)` |

Row padding: `var(--space-3) var(--space-4)` — matches `.cc-list-item`.

---

## Section Headers

Styled like `.cc-panel-header` but without the background tint — just a dividing line.

```css
.search-section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: var(--space-2) var(--space-4);
  border-top: 1px solid var(--color-border);   /* separator above each section */
  background: var(--color-bg-alt);             /* #f1eee9 — same as panel header bg */
}

.search-section-label {
  font-size: var(--text-xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.06em;
  color: var(--color-text-muted);
}

.search-section-count {
  font-size: var(--text-xs);
  color: var(--color-text-muted);
}
```

The first section header does not get `border-top` — the dropdown's own top padding serves as the visual separator.

---

## Section Footers ("See all N results")

Shown only when the entity type has more than 3 results.

```css
.search-section-footer {
  padding: var(--space-2) var(--space-4);
  border-bottom: 1px solid var(--color-border);
}

.search-section-footer a {
  font-size: var(--text-xs);
  font-weight: 500;
  color: var(--color-primary);
  text-decoration: none;
}
```

Link target: the entity list page pre-filtered with the current query string.
Example: `/clients?q=sarah` for a Client "See all" link.

---

## States

### Initial State (focused, empty input)

Show the 5 most recent searches. Recent searches are stored in `localStorage` under the key `nutrir_recent_searches` as an array of `{ label, url, type, timestamp }` objects. Max 10 stored; display 5.

```
┌──────────────────────────────────────────────┐
│  RECENT                                      │
│  [◷]  Sarah Mitchell              client  →  │
│  [◷]  Mediterranean Week 1     meal plan  →  │
│  [◷]  Initial Consult         appointment →  │
│  [◷]  Mike Thompson              client  →   │
│  [◷]  Priya Sharma               client  →   │
│                                              │
│                              Clear history   │
└──────────────────────────────────────────────┘
```

- Clock icon (HeroIcon `clock`), 14px, `color: var(--color-accent)`
- Type label right-aligned in muted text
- "Clear history" link: bottom right, `text-xs`, `color-text-muted`, calls `localStorage.removeItem`

If there are no recent searches, show nothing — do not show an empty state for the initial focus. The dropdown simply does not appear until the user types 2+ characters.

### Loading State

Replace the result rows with skeleton placeholders. Skeletons maintain the same row height so the dropdown does not resize when results arrive.

```
┌──────────────────────────────────────────────┐
│  CLIENTS                                     │
│  [░░]  ████████████████       ░░░░░░░░       │
│  [░░]  ████████████           ░░░░░░░        │
│  [░░]  ████████████████████   ░░░░░          │
│                                              │
│  APPOINTMENTS                                │
│  [░░]  ████████████████       ░░░░░░░░       │
└──────────────────────────────────────────────┘
```

Use the existing `.cc-skeleton` shimmer animation. Render 2–3 skeleton rows per section type. The section headers remain visible during loading so the structure is stable.

### Empty State (query returned no results)

```
┌──────────────────────────────────────────────┐
│                                              │
│          No results for "xyz"                │
│     Try a name, appointment type, or         │
│          meal plan title.                    │
│                                              │
└──────────────────────────────────────────────┘
```

- Centered, `padding: var(--space-8) var(--space-4)`
- Primary line: `text-sm`, `color: var(--color-text)`
- Secondary line: `text-xs`, `color: var(--color-text-muted)`
- No icon — the empty state in `.cc-empty-state` omits icons for density

### Error State

If the search API call fails, show a single muted line inside the dropdown rather than crashing the dropdown:

```
  Could not load results. Try again.
```

`text-xs`, `color: var(--color-error)`, `padding: var(--space-4)`.

---

## Keyboard Navigation

```
Ctrl+K / Cmd+K      → Focus search input, open dropdown
Type                → Filter results (debounced 250ms)
Arrow Down          → Move focus into results list (first row)
Arrow Up            → From first result, returns focus to input
Arrow Down/Up       → Navigate between result rows
Enter               → Navigate to focused result
Escape              → If input has text: clear text, stay focused
                      If input empty: close dropdown and blur
Tab                 → From input: move to first result
                      From last result: close dropdown, move to next topbar element
```

Focus management must be implemented in JavaScript (not Blazor SSR). The dropdown component requires `@rendermode InteractiveServer`.

---

## Ctrl+K Hint in the Input

Add a `<kbd>` hint inside the search input. Position it absolutely on the right side of the input. Hidden when the input is focused (because the user is already in it).

```
[🔍  Search...                     ⌘K]
```

The hint uses `.cc-kbd` styling from `layout.css`:
```css
.cc-search-hint {
  position: absolute;
  right: var(--space-3);
  top: 50%;
  transform: translateY(-50%);
  display: flex;
  align-items: center;
  gap: 2px;
  pointer-events: none;
  transition: opacity var(--duration-fast) var(--ease-default);
}

.cc-search:focus-within .cc-search-hint {
  opacity: 0;
}
```

Use the existing `.cc-kbd` class for each key cap.

---

## Backdrop / Click Outside

A transparent backdrop `div` covers the page behind the dropdown at `z-index: 999`. Clicking it dismisses the dropdown. The backdrop does NOT dim or blur the page — no `background` or `backdrop-filter`. This keeps the interaction lightweight and avoids disrupting the practitioner's view of the current page.

```css
.search-backdrop {
  position: fixed;
  inset: 0;
  z-index: 999;
  background: transparent;
}
```

---

## Mobile Behavior (max-width: 768px)

On mobile, the search input in the topbar becomes a full-screen overlay rather than a dropdown. The icon rail is already hidden at this breakpoint.

**Trigger:** Tapping the search input (or the magnifying glass icon) opens the full-screen overlay with a slide-up animation (`slideUp 200ms ease`).

**Overlay layout:**
```
┌──────────────────────────────────────────────┐
│  [←]  [🔍  Search clients, appointments...] │  ← fixed header
├──────────────────────────────────────────────┤
│                                              │
│  RECENT                                      │
│  [◷]  Sarah Mitchell              client  →  │
│  ...                                         │
│                                              │
│  CLIENTS                                     │
│  [👤] Sarah Mitchell        Active       →  │
│  ...                                         │
│                                              │
└──────────────────────────────────────────────┘
```

- Back arrow dismisses and returns to previous page state
- Input is auto-focused when the overlay opens
- Result rows are taller: `padding: var(--space-4)` (easier tap targets)
- `min-height: 100dvh`, `background: var(--color-bg-card)`

---

## CSS Class Reference

| Class | Purpose |
|---|---|
| `.search-dropdown` | The dropdown container |
| `.search-backdrop` | Transparent click-outside capture layer |
| `.search-section` | Wraps one entity type's header + rows + footer |
| `.search-section-header` | Section label row (CLIENTS, APPOINTMENTS, etc.) |
| `.search-section-label` | The uppercase entity type label |
| `.search-section-count` | "3 of 7" count |
| `.search-section-footer` | "See all N results" link row |
| `.search-result-row` | Individual result item; extends `.cc-list-item` pattern |
| `.search-result-icon` | 28px icon circle (avatar or entity-type icon) |
| `.search-result-content` | Primary + secondary text stack |
| `.search-result-primary` | Result name/title |
| `.search-result-secondary` | Supporting metadata |
| `.search-result-meta` | Right-side status badge + chevron |
| `.search-empty` | No-results message container |
| `.search-recent` | Recent searches section |
| `.search-recent-clear` | "Clear history" link |
| `.search-hint` | Ctrl+K hint inside the input |
| `.search-overlay` | Mobile full-screen overlay |
| `.search-overlay-header` | Mobile overlay top bar with back button |

---

## Search Query Scope

The backend search should query across:

| Entity | Fields searched |
|---|---|
| Clients | `FirstName`, `LastName`, `Email`, `PhoneNumber` |
| Appointments | `AppointmentType`, linked Client name |
| Meal Plans | `Name`, linked Client name |

All searches are scoped to the current practitioner's data. No cross-practitioner results.

Results are returned sorted by relevance (exact prefix match first, then substring), then by recency (most recent `UpdatedAt`).

---

## Implementation Notes for Blazor

- The dropdown requires `@rendermode InteractiveServer` — keyboard events, focus management, and localStorage access cannot run in SSR.
- Debouncing should be implemented with `System.Threading.Timer` or `CancellationTokenSource` inside the component, not with a JS `setTimeout`.
- Recent searches stored in `localStorage` must be read/written via JS interop (`IJSRuntime`).
- The Ctrl+K global listener should be registered in `OnAfterRenderAsync` and disposed in `IAsyncDisposable.DisposeAsync`.
- The `search-backdrop` div is rendered into the component tree and conditionally shown with `@if (IsOpen)` — do not portal it to `<body>` unless z-index conflicts arise.
- Inject `NavigationManager` for navigating to result URLs on selection.
- The component should emit a `blazor:navigating` event handler cleanup when the route changes (close the dropdown on navigation).

---

## Accessibility

| Requirement | Implementation |
|---|---|
| `role="combobox"` | On the `<input>` element |
| `aria-expanded` | `true`/`false` reflecting dropdown open state |
| `aria-controls` | Points to the dropdown `id` |
| `aria-activedescendant` | Updates to the focused result row's `id` |
| `role="listbox"` | On the `.search-dropdown` container |
| `role="option"` | On each `.search-result-row` |
| `role="group"` + `aria-label` | On each `.search-section` (e.g., `aria-label="Clients"`) |
| Focus ring | All keyboard-focused rows get `outline: 2px solid var(--color-primary); outline-offset: -2px` |
| Screen reader announcements | Use an `aria-live="polite"` region to announce result count changes: "5 results found" |
| Color contrast | All text combinations meet WCAG 2.1 AA. Status badges use both color and text label — never color alone. |

---

## What Is Not Included (v1 Scope)

- Filtering by entity type within the dropdown (no "Clients only" toggle)
- Saving searches as favorites
- Advanced query syntax (field: value operators)
- Search analytics / most-searched terms dashboard
- Full-page search results page (the "See all" links navigate to filtered list pages instead)
