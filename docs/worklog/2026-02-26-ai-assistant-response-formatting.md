# AI Assistant Response Formatting

**Date:** 2026-02-26
**PR:** [#49](https://github.com/cpike5/nutrir/pull/49)
**Branch:** `feat/ai-assistant-response-formatting`

## Summary

Improved the visual formatting of AI assistant responses in the sidebar panel. Headings now have clear hierarchy, tables match the app's data table convention, status keywords render as colored badges, and lists/blockquotes/horizontal rules all received polish — using existing design tokens only.

## What Changed

### AiAssistantPanel.razor.css

- **Heading hierarchy**: H1 uses `--font-display` (Outfit), `--text-lg`, weight 700, subtle `--color-primary-muted` bottom border. H2 uses `--font-display`, `--text-base`. H3 is uppercase label style with `--tracking-wider` and `--color-text-muted` — matches dashboard panel title convention.
- **Table styling**: Wrapper gets `border: 1px solid var(--color-border)` + `border-radius: var(--radius-md)`. Header row uses `--color-bg-alt` background, uppercase, `--text-xs`, `--tracking-wider`. Cells use horizontal-only borders (`border-bottom`). Row hover with subtle primary tint. Tables fill full width within message bubble.
- **Status badges**: Three semantic badge classes (`cc-ai-status-success`, `cc-ai-status-warning`, `cc-ai-status-error`) using `rgba(color, 0.12)` background + semantic color text — same pattern as the existing `Badge` component.
- **List styling**: Primary-colored `::marker` pseudo-elements, `--space-1` bottom margin on `li`, `--leading-normal` line-height.
- **Blockquote styling**: 3px `--color-primary` left border, `--color-primary-muted` background, rounded right corners.
- **Horizontal rule**: `border-top: 1px solid var(--color-border)`, `--space-3` vertical margin — matches app's `.divider` pattern.
- **Paragraph spacing**: Assistant message `line-height` upgraded from `1.5` to `var(--leading-relaxed)`.

### AiAssistantPanel.razor (C#)

- **Status badge detection**: Added `ApplyStatusBadge()` method with three `HashSet<string>` lookups (case-insensitive). Called per cell in `ConvertTable()`. Keywords detected:
  - Success (green): Confirmed, Active, Completed
  - Warning (amber): Scheduled, Pending, Draft
  - Error (red): Cancelled, No-show, Expired
- **Blockquote parsing**: Added `^&gt; (.+)$` regex in `RenderMarkdown()` (after headers, before horizontal rules). Converts `> text` lines to `<div class="cc-ai-blockquote">` elements.

## Design Decisions

- **Existing tokens only**: No new CSS custom properties or shared components were introduced. All styling references existing design tokens from the design system.
- **Uppercase H3**: Matches the dashboard panel title convention (uppercase, tracked, muted) to provide visual consistency across the app.
- **Status detection by exact match**: Only whole-cell matches trigger badges to avoid false positives on partial text like "Confirmed appointment with...".
- **Blockquote via `<div>` not `<blockquote>`**: Uses a class-styled div to stay consistent with the existing markdown renderer pattern (all block elements are divs with `cc-ai-` prefixed classes).
