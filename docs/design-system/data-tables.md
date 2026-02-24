# Data Tables

Conventions for rendering data tables across the application (clients, appointments, meal plans, etc.).

## Layout Pattern

Wrap the `<table>` in a `.table-card` div — white background, `radius-xl`, `shadow-sm`, border, `overflow: hidden`. Do **not** use the `<Card>` component (it adds inner padding that conflicts with full-width table rows).

```html
<div class="table-card">
    <table class="clients-table" role="grid">
        ...
    </table>
</div>
```

## Header Row

- Background: `var(--color-bg-alt)`
- Text: uppercase, `text-xs`, `font-weight: 600`, `letter-spacing: var(--tracking-wider)`, `color: var(--color-text-muted)`
- First cell gets extra left padding (`--space-6`); last cell right-aligned with extra right padding
- Screen-reader-only label for the Actions column: `<span class="sr-only">Actions</span>`

## Identity Cells

Combine a circular avatar with stacked primary/secondary text:

```
┌──────────────────────────┐
│  ┌──┐  Name              │
│  │ST│  email@example.com  │
│  └──┘                     │
└──────────────────────────┘
```

- **Avatar**: 38px circle, `var(--color-primary-muted)` background, `var(--color-primary)` text, `font-display`, initials from first + last name
- **Name**: `font-weight: 600`, `text-sm`
- **Email**: `text-xs`, `color-text-muted`, truncated with `text-overflow: ellipsis`
- Avatar scales to `1.08` on row hover

## Status Badges

Use the existing `<Badge>` component with a dot indicator:

```razor
<Badge Variant="BadgeVariant.Success"><span class="badge-dot"></span> Given</Badge>
<Badge Variant="BadgeVariant.Warning"><span class="badge-dot"></span> Pending</Badge>
```

The `.badge-dot` is a 6px circle colored by `currentColor`.

## Row Hover Effects

- Background changes to `var(--color-bg-alt)`
- 3px primary-colored left border appears via `::before` pseudo-element
- Avatar scales up
- Action buttons fade in (from `opacity: 0` to `opacity: 1`)

## Action Buttons

Icon-only buttons (`.btn-icon`) using inline SVGs. Hidden by default (`opacity: 0`), visible on row hover. Standard icons:

| Action | Icon | aria-label |
|--------|------|------------|
| View | Eye | `View {Name}` |
| Edit | Pencil | `Edit {Name}` |

Always visible on mobile (no hover available).

## Row Entrance Animation

Staggered `rowFadeIn` keyframe: rows translate up 6px and fade in with 30ms incremental delays per row (up to 10 rows).

```css
@keyframes rowFadeIn {
    from { opacity: 0; transform: translateY(6px); }
    to   { opacity: 1; transform: translateY(0); }
}
```

## Responsive Breakpoints

| Breakpoint | Hidden columns | Other changes |
|------------|----------------|---------------|
| ≤ 860px | Phone, Nutritionist | Search goes full-width, count wraps to new line |
| ≤ 600px | Consent, Created | Reduced cell padding, action buttons always visible, page padding reduced |

Use CSS classes on both `<th>` and `<td>` to toggle visibility: `.col-phone`, `.col-nutritionist`, `.col-consent`, `.col-date`.
