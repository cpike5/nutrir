# Meal Plan PDF Export — Layout Spec

## Overview

Generates a PDF document from a `MealPlanDetailDto`, providing a printable or shareable version of a meal plan with full nutritional information.

## Page Setup

| Property | Value |
|----------|-------|
| Page size | US Letter (8.5 × 11 in) |
| Horizontal margin | 60 pt |
| Vertical margin | 50 pt |
| Base font size | 10 pt |
| Font | System default (QuestPDF) |

## Color Palette

| Token | Hex | Usage |
|-------|-----|-------|
| Primary | `#2d6a4f` | Brand text, section headings, header border |
| Text | `#2a2d2b` | Body text |
| Muted | `#636865` | Labels, footer text |
| Day accent | `#e8f5e9` | Day header background |

## Header

- **Top row** — left: "Nutrir" (18 pt, bold, primary); right: plan title (14 pt, bold, text color)
- **Bottom border** — 2 pt line in primary color
- **Metadata rows** (below border, 10 pt):
  - Row 1: "Client: {FirstName} {LastName}" (left) | "Date: {StartDate} — {EndDate}" (right)
  - Row 2: "Practitioner: {CreatedByName}" (left) | "Status: {Status}" (right)
- **Macro targets row** (conditional — only if any target is set):
  - "{CalorieTarget} kcal | {ProteinTargetG}g P | {CarbsTargetG}g C | {FatTargetG}g F"
  - Muted color, centered

## Content

### Optional Text Sections

Rendered in order if non-empty. Each has a heading (11 pt, bold, primary) and body (10 pt, 1.4× line height):

1. **Description**
2. **Client Instructions**
3. **Internal Notes**

### Day Panels

For each day in `Days`, ordered by `DayNumber`:

#### Day Header Row
- Background: day accent color (`#e8f5e9`)
- Left: day label (bold) — `Label ?? "Day {DayNumber}"`
- Right: day-level macro totals — "{TotalCalories} kcal · {TotalProtein}g P · {TotalCarbs}g C · {TotalFat}g F"

#### Meal Slots

For each slot in the day, ordered by `SortOrder`:

- **Slot header**: slot name (bold, 10 pt) — `CustomName ?? MealType.ToString()`
- **Slot totals** (muted, right-aligned): same macro format as day totals

#### Item Table

QuestPDF `Table` component with columns:

| Column | Width | Alignment |
|--------|-------|-----------|
| Food | Relative (flex) | Left |
| Qty | 60 pt | Right |
| Cal | 45 pt | Right |
| P | 40 pt | Right |
| C | 40 pt | Right |
| F | 40 pt | Right |

- Header row: bold, muted color
- Data rows: alternating white / light gray (`#f9f9f9`)
- Food cell includes item notes in muted italic below the name (if present)
- Slot total row at bottom of each table: bold values

## Footer

- **Top border** — 1 pt line in `#e0e0e0`
- Left: "Nutrir — Meal Plan" (8 pt, muted)
- Right: "Page N of M" (8 pt, muted)

## Page Break Behavior

QuestPDF handles page breaks natively within Table components. Day panels use `EnsureSpace(72)` to avoid orphaned headers at page bottom.
