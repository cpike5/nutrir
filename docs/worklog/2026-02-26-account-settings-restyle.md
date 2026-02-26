# Account Settings Restyle — Stripe-Inspired

**Date:** 2026-02-26
**Branch:** `main`

## Summary

Restyled the Account Settings pages (ManageLayout + all Manage/ pages) from the boxy Identity scaffolding look to a polished, Stripe-inspired design. Single content panel, clean typography, receding nav, and refined form controls.

## What Changed

### ManageLayout.razor.css (complete rewrite)

- Replaced `::deep .table-card` / `.section-header` / `.card-body` pattern with `::deep .settings-section` / `.section-heading` / `.section-body`
- Content panel (white bg, radius-xl, shadow-sm, border) now lives on `.manage-content` instead of per-section cards
- Sections separated by 1px dividers with `var(--space-8)` vertical margin
- Section headings: normal case, Outfit font, `text-base`, 600 weight, colored SVG icon
- Form labels: normal case, `text-sm`, 500 weight (no more uppercase)
- Inputs: `max-width: 400px` so fields don't float in space
- Added `.form-hint` for disabled field explanations
- Badges reworked: dot-style indicators (6px circle + text) instead of filled pill badges
- Alerts restyled as left-border callouts (3px colored left border, subtle tinted bg)
- Added `.setting-row` for read-only label-value display (flex between)
- Added `.danger-zone` with red-tinted divider and red heading/icon
- Added `.sub-heading` / `.sub-desc` for subsections within a section
- Sidebar narrowed from 220px to 200px
- Button selectors no longer scoped under `.card-body` — work anywhere in deep content

### ManageNavMenu.razor.css (complete rewrite)

- Removed card-wrapper/background active/hover styles
- Active state: 2px left border in primary green, primary text color, 600 weight, no background
- Hover: `translateX(1px)`, text darkens, no background
- Mobile: horizontal scrollable with bottom-border active indicator instead of left-border

### ManageNavMenu.razor

- All icon sizes updated from 16px to 18px

### Manage Page Razor Files (12 files)

All pages converted from old pattern to new:

| File | Key Changes |
|------|-------------|
| `Index.razor` | Added `form-hint` "Username cannot be changed" under disabled input |
| `Email.razor` | Changed `<hr class="divider">` to `<hr class="section-divider">` |
| `ChangePassword.razor` | Heading changed to normal case "Change password" |
| `ExternalLogins.razor` | Used `sub-heading`/`sub-desc` for registered logins and add service subsections |
| `TwoFactorAuthentication.razor` | Used `setting-row` for status and recovery code count display |
| `EnableAuthenticator.razor` | Added section description |
| `ResetAuthenticator.razor` | Added section description |
| `GenerateRecoveryCodes.razor` | Added section description, button text to normal case |
| `PersonalData.razor` | Added section description |
| `DeletePersonalData.razor` | Wrapped in `danger-zone` class for red styling |
| `SetPassword.razor` | Moved description text from card-body into `section-desc` |
| `ShowRecoveryCodes.razor` (shared) | Added section description |

### Pattern Applied Per Page

```diff
- <div class="table-card">
-     <div class="section-header">
-         <svg width="16" height="16">...</svg>
-         <span>SECTION TITLE</span>
-     </div>
-     <div class="card-body">
+ <div class="settings-section">
+     <div class="section-heading">
+         <svg width="18" height="18">...</svg>
+         <span>Section title</span>
+     </div>
+     <p class="section-desc">Brief description.</p>
+     <div class="section-body">
```

## Design Decisions

- **Single panel, not per-section cards**: All sections live inside one white panel on the content area. Sections separated by thin dividers. This feels denser and more professional — like Stripe's dashboard.
- **Normal case everywhere**: Dropped all `text-transform: uppercase` and `letter-spacing: wider`. Headings and labels use sentence case.
- **Dot badges over pill badges**: Status indicators use a small colored dot + text (e.g., "● Verified") instead of filled background pills. Lighter, more refined.
- **Left-border callouts over boxed alerts**: Alerts/warnings use a 3px colored left border with subtle tinted background instead of full border boxes.
- **Nav recedes**: No card wrapper, no background on hover/active. Just a left border indicator. Content area is the star.

## Artifacts

- HTML prototype: `docs/temp/account-settings-stripe.html` (not committed — reference only)
