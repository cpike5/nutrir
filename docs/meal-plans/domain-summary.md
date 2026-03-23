# Meal Plans Domain — Current State Summary

**Last Updated:** 2026-03-22

## Overview

The meal plans domain manages the creation, editing, and lifecycle of personalized meal plans for clients. It includes macronutrient targeting, allergen checking and warnings, PDF export for client distribution, and archival workflows. Meal plans are a primary tool for nutrition practitioners to deliver prescribed eating guidance to clients.

## Core Entities & Data Model

### Meal Plan Entity Hierarchy

The meal plan uses a four-level nested structure: Meal Plan → Days → Meal Slots → Meal Items.

#### MealPlan

**File:** `/src/Nutrir.Core/Entities/MealPlan.cs`

Root entity representing a complete meal plan assignment.

**Fields:**

- `Id` (int, PK) — unique meal plan identifier
- `ClientId` (int, FK) — links to the client
- `CreatedByUserId` (string) — practitioner who created the plan
- `Title` (string) — meal plan name (e.g., "Diabetes Management - Week 1")
- `Description` (string, nullable) — additional context/goals
- `Status` (MealPlanStatus enum) — Draft, Active, or Archived
- `StartDate` (DateOnly, nullable) — when plan becomes active
- `EndDate` (DateOnly, nullable) — when plan expires
- `CalorieTarget` (decimal, nullable) — daily calorie target
- `ProteinTargetG` (decimal, nullable) — daily protein target in grams
- `CarbsTargetG` (decimal, nullable) — daily carbs target in grams
- `FatTargetG` (decimal, nullable) — daily fat target in grams
- `Notes` (string, nullable) — practitioner notes about the plan
- `Instructions` (string, nullable) — special instructions for client (e.g., meal prep tips, substitution rules)

**Soft-Delete Tracking:**

- `IsDeleted`, `DeletedAt`, `DeletedBy` — follow standard compliance pattern

**Audit Timestamps:**

- `CreatedAt` (DateTime, UTC)
- `UpdatedAt` (DateTime, nullable, UTC)

**Navigation Properties:**

- `Days` (List<MealPlanDay>) — typically 7 days, but flexible
- `AllergenWarningOverrides` (List<AllergenWarningOverride>) — practitioner-acknowledged allergen flags

#### MealPlanDay

**File:** `/src/Nutrir.Core/Entities/MealPlanDay.cs`

Represents a single day within a meal plan.

**Fields:**

- `Id` (int, PK)
- `MealPlanId` (int, FK) — parent meal plan
- `DayNumber` (int) — 1-7 (or beyond for longer plans)
- `Label` (string, nullable) — day name (e.g., "Monday", "Day 1")
- `Notes` (string, nullable) — day-specific notes (e.g., "Meal prep day", "Social event — flexible")

**Navigation:**

- `MealSlots` (List<MealSlot>) — typically 4-6 slots (breakfast, lunch, dinner, snacks)

#### MealSlot

**File:** `/src/Nutrir.Core/Entities/MealSlot.cs`

Represents a meal or snack occasion within a day (e.g., breakfast, afternoon snack).

**Fields:**

- `Id` (int, PK)
- `MealPlanDayId` (int, FK) — parent day
- `MealType` (MealType enum) — Breakfast, MorningSnack, Lunch, AfternoonSnack, Dinner, EveningSnack
- `CustomName` (string, nullable) — override standard name (e.g., "Post-Workout Shake")
- `SortOrder` (int) — display order within the day
- `Notes` (string, nullable) — meal-occasion notes (e.g., "~30 min before workout")

**Navigation:**

- `Items` (List<MealItem>) — foods to consume at this meal

#### MealItem

**File:** `/src/Nutrir.Core/Entities/MealItem.cs`

Represents a single food/dish within a meal slot.

**Fields:**

- `Id` (int, PK)
- `MealSlotId` (int, FK) — parent meal slot
- `FoodName` (string) — food description (e.g., "Grilled chicken breast", "Brown rice")
- `Quantity` (decimal) — amount (e.g., 150)
- `Unit` (string) — unit of measure (g, ml, oz, cup, tbsp, etc.)
- `CaloriesKcal` (decimal) — calories per serving
- `ProteinG` (decimal) — grams of protein
- `CarbsG` (decimal) — grams of carbohydrates
- `FatG` (decimal) — grams of fat
- `Notes` (string, nullable) — cooking instructions or substitution notes
- `SortOrder` (int) — display order within meal slot

**Key Design Note:** MealItem has no soft-delete fields — items are managed by changing MealSlot contents, and day/slot structure changes delete old items. This simplifies the editing flow.

### Allergen Warning Override Entity

#### AllergenWarningOverride

**File:** `/src/Nutrir.Core/Entities/AllergenWarningOverride.cs`

Records practitioner acknowledgment of allergen flags (either accepting the risk or noting why it's safe).

**Fields:**

- `Id` (int, PK)
- `MealPlanId` (int, FK)
- `FoodName` (string) — the flagged food (e.g., "Peanut butter")
- `AllergenCategory` (AllergenCategory enum) — category of allergen
- `OverrideNote` (string) — why practitioner approved (e.g., "Client reports resolved peanut allergy — cleared by GP")
- `AcknowledgedByUserId` (string) — practitioner who acknowledged
- `AcknowledgedAt` (DateTime, UTC) — when acknowledged
- `CreatedAt` (DateTime, UTC)

### Enums

**MealPlanStatus** (`/src/Nutrir.Core/Enums/MealPlanStatus.cs`)

- `Draft` — not yet active, can be edited freely
- `Active` — in use by client, limited edits
- `Archived` — no longer current, view-only

**MealType** (`/src/Nutrir.Core/Enums/MealType.cs`)

- `Breakfast`
- `MorningSnack`
- `Lunch`
- `AfternoonSnack`
- `Dinner`
- `EveningSnack`

**AllergenCategory** — see `docs/compliance/field-encryption.md` or allergen service definitions

## Data Transfer Objects (DTOs)

All DTOs located in `/src/Nutrir.Core/DTOs/`.

### MealPlanSummaryDto

List/card view of a meal plan.

```csharp
record MealPlanSummaryDto(
    int Id,
    string Title,
    MealPlanStatus Status,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    DateOnly? StartDate,
    DateOnly? EndDate,
    decimal? CalorieTarget,
    decimal? ProteinTargetG,
    decimal? CarbsTargetG,
    decimal? FatTargetG,
    int DayCount,
    int TotalItems,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
```

### MealPlanDetailDto

Full plan with all days, slots, and items (for display and editing).

Contains:

- All summary fields
- `Days` — array of day/slot/item structure
- Each day has `Slots` with `Items`
- Each item includes macro breakdown

### CreateMealPlanDto

Input for meal plan creation.

Fields:

- `ClientId`, `Title`, `Description` (optional)
- `Status` (defaults to Draft)
- `StartDate`, `EndDate` (optional)
- `CalorieTarget`, `ProteinTargetG`, `CarbsTargetG`, `FatTargetG` (all optional)
- `Notes`, `Instructions` (optional)
- `Days` — initial day/slot/item structure

### SaveMealPlanContentDto

Input for updating meal plan structure (days, slots, items).

Used when practitioner edits the content after initial creation.

## Service Layer

### IMealPlanService & MealPlanService

**File:** `/src/Nutrir.Infrastructure/Services/MealPlanService.cs`

#### Core Methods

- **GetByIdAsync(id)** — fetch single meal plan with full structure and allergen warnings
- **GetListAsync(clientId?, status?)** — list all plans with optional filters
- **GetPagedAsync(MealPlanListQuery)** — paginated query with sorting and filtering
- **CreateAsync(CreateMealPlanDto, userId)** — create plan, audit log, return DTO
- **UpdateAsync(id, UpdateMealPlanDto, userId)** — update metadata (title, targets, dates, notes)
- **SaveContentAsync(id, SaveMealPlanContentDto, userId)** — update day/slot/item structure
- **UpdateStatusAsync(id, newStatus, userId)** — transition status (Draft → Active → Archived), audit log
- **SoftDeleteAsync(id, userId)** — soft-delete plan and all children

#### Status Lifecycle

- **Draft** → **Active** — plan ready for client use
- **Active** → **Archived** — plan completed or superseded
- **Archived** → can manually revert to Active if needed (audit logged)

#### Implementation Notes

- `IDbContextFactory<AppDbContext>` used for paged queries (concurrency)
- Direct context for single-entity operations
- Audit logging on every mutation
- `IAllergenCheckService` called after every content update to detect new warnings
- `INotificationDispatcher` notified for real-time updates
- `IRetentionTracker` called to update `Client.LastInteractionDate`

### IMealPlanPdfService & MealPlanPdfService

**File:** `/src/Nutrir.Infrastructure/Services/MealPlanPdfService.cs`

Generates PDF export of a meal plan for client printing/sharing.

#### Methods

- **GeneratePdfAsync(mealPlanId, userId)** — render meal plan as PDF bytes, audit log

#### Implementation

Uses `MealPlanPdfRenderer` (custom PDF renderer, see `docs/meal-plans/pdf-export-layout.md`) to produce professional layout with:

- Client name and plan title at top
- Nutritionist contact info
- Day-by-day meal listing
- Macro totals per day and overall
- Instructions section
- Footer with generation date

See `docs/meal-plans/pdf-export-layout.md` for design spec.

### IAllergenCheckService & AllergenCheckService

**File:** `/src/Nutrir.Infrastructure/Services/AllergenCheckService.cs`

Scans a meal plan for foods that conflict with client's declared allergies.

#### Methods

- **CheckAsync(mealPlanId)** — return `List<AllergenWarningDto>` with all flagged items

#### Algorithm

For each meal item in the plan:

1. Fetch all food allergies for the client (type = Food)
2. For each allergy, use `AllergenKeywordMap` to:
   - Map allergy name to category (e.g., "Peanuts" → TreeNuts)
   - Match food name against category keywords
   - Or perform direct string match
3. If match found, return `AllergenWarningDto` with:
   - Food name, meal location (day/slot), severity, allergy name
   - Whether practitioner has already acknowledged (via `AllergenWarningOverride`)
4. Filter out already-overridden warnings

#### Keyword Mapping

`AllergenKeywordMap` maintains category definitions:

- **TreeNuts** — almonds, cashews, pecans, walnuts, peanuts, etc.
- **Shellfish** — shrimp, crab, lobster, mussels, etc.
- **Milk** — cheese, yogurt, butter, cream, milk, whey, casein, etc.
- **Eggs** — egg, mayonnaise (typically contains eggs), etc.
- **Wheat** — wheat, bread, pasta, flour, cereal, etc.
- **Fish** — salmon, tuna, cod, halibut, etc.
- **Soy** — soy, tofu, soy sauce, edamame, tempeh, etc.
- **Sesame** — sesame, tahini, etc.

#### Practitioner Override Workflow

When practitioner reviews warnings:

1. For each flagged item, practitioner can:
   - **Acknowledge & Accept Risk** — create `AllergenWarningOverride` with note explaining why (e.g., "Client has resolved allergy")
   - **Ignore Warning** — same as above (implies practitioner approval)
2. Override is tied to food name + allergen category (not item-specific)
3. Once overridden, future plans with same food won't re-flag that allergen

## UI Pages & Components

**Current Status:** Meal plan list and create/edit pages exist and are fully routed.

### MealPlanList.razor

**Path:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanList.razor`

- Card-based table layout with meal plan title, client name, status badge
- Filters: client, status (Draft/Active/Archived)
- Columns: Title, Client, Status, Start Date, End Date, Macros, Actions
- Real-time updates via `INotificationDispatcher`
- Pagination via `DataGrid<MealPlanSummaryDto>`
- Row hover and animations

### MealPlanDetail.razor

**Path:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanDetail.razor`

Single meal plan view with:

1. **Metadata** section — title, client, status, date range, macro targets
2. **Days/Slots/Items** — visual layout of meal structure
   - Each day as collapsible section
   - Each slot shows meal type and items
   - Item cards show food, quantity, unit, and macro breakdown
3. **Allergen Warnings** — collapsible section showing flagged items with:
   - Food name, severity, matched allergy
   - Acknowledgment status and note
   - "Accept" button to record override
4. **Actions** — Edit, PDF Export, Change Status, Delete buttons

### MealPlanCreate.razor

**Path:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanCreate.razor`

Multi-step form:

1. **Step 1: Select Client** — client dropdown
2. **Step 2: Plan Metadata** — title, description, status, dates, macro targets, notes, instructions
3. **Step 3: Build Meal Structure** — dynamically add days, slots, items
   - Day selector (1-7+) with optional label
   - Meal slot generator (breakfast, lunch, dinner, snacks)
   - Item entry with food, quantity, unit, macros
   - Macro calculator to auto-fill from food database or manual entry
4. **Step 4: Review & Create** — summary with allergen check preview
5. **Confirmation** — success message with links to view/export

### MealPlanEdit.razor

**Path:** `src/Nutrir.Web/Components/Pages/MealPlans/MealPlanEdit.razor`

Similar to detail page but with inline editing:

- Metadata editable in place (title, targets, dates, notes)
- Status change dropdown (with confirmation for Active→Archived)
- Day/slot/item structure editable (add/remove/reorder)
- Allergen warnings re-checked on save
- Auto-save or explicit save button

## Status Lifecycle & Validation

### Valid Transitions

| From → To | Valid | Notes |
|-----------|-------|-------|
| Draft → Active | ✓ | Plan approved and shared with client |
| Draft → Archived | ✓ | Plan discarded without use |
| Active → Archived | ✓ | Plan completed or replaced |
| Archived → Active | ✓ | Plan reactivated (rare but allowed) |
| Draft → Draft | ✓ | Edit in place |
| Active → Active | ✓ | Limited edits (metadata only) |
| Archived → Archived | ✓ | View-only state |
| Any → Draft | ✗ | Cannot downgrade status |

### Business Rules

- **Draft plans** can be edited freely (days, slots, items, metadata)
- **Active plans** can update metadata (title, targets, instructions) but day/slot/item changes require explicit "Save Content" action (triggers allergen re-check and audit log)
- **Archived plans** are read-only; to edit, must revert to Draft

## Allergen Checking Workflow

1. **Create or Save Content** — after changes saved to database
2. **AllergenCheckService.CheckAsync()** called
3. If warnings found:
   - Return to user with list of flagged items
   - Practitioner reviews each warning
   - For each warning: either acknowledge (create override) or remove the food
   - Re-check until no warnings (or all acknowledged)
4. Once approved, plan is ready

## PDF Export

See `docs/meal-plans/pdf-export-layout.md` for full specification.

**Key Features:**

- Professional layout with client name, nutritionist branding, meal schedule
- Macro totals per day and overall plan
- Portion sizes clearly listed
- Customizable footer with contact/practice info
- Printer-friendly color palette

## Known Issues & Future Work

### High Priority (v1 Scope Completion)

1. **Macro Calculator** — no food database integration; practitioners manually enter macros per item
   - **Fix:** Integrate with USDA FoodData Central or similar API for quick macro lookup

2. **Meal Plan Templates** — no ability to save/reuse plan structures across clients
   - **Fix:** Create `MealPlanTemplate` entity and template library UI

3. **Recurring/Weekly Plans** — plans are fixed-structure; no built-in repeat cycles
   - **Fix:** Add `RecurrencePattern` field to allow 2-week, 4-week, monthly cycles

### Medium Priority (v2+)

- **Client feedback** — no mechanism for clients to report if they disliked a meal or had issues
- **Macro analytics** — no chart showing adherence to macro targets over time
- **Food swaps** — no suggestion engine for allergen-safe substitutions
- **Shopping list export** — PDF or text export of ingredients for shopping
- **Nutritional analysis** — micronutrient breakdown (vitamins, minerals, fiber)
- **Seasonal/cultural adaptations** — plan variations for holidays, cultural events

## Database Migrations

**Base Migration:** `20260201230418_AddMealPlans.cs`

Creates meal plan tables:

- `MealPlans` — main table with FK to Clients
- `MealPlanDays` — FK to MealPlans
- `MealSlots` — FK to MealPlanDays
- `MealItems` — FK to MealSlots
- `AllergenWarningOverrides` — FK to MealPlans

**Indexes:**

- `MealPlans(ClientId, Status, CreatedAt)` for filtering
- `MealPlanDays(MealPlanId, DayNumber)` for ordering
- `MealSlots(MealPlanDayId, SortOrder)` for ordering
- `MealItems(MealSlotId, SortOrder)` for ordering

## Documentation & Standards

### Where to Add New Docs

All meal plans documentation goes in `/docs/meal-plans/`.

**Existing documents:**

- `domain-summary.md` — this file
- `pdf-export-layout.md` — PDF export design spec (page setup, layout, colors, tables)

**Expected documents (not yet created):**

- `adr-0001-status-lifecycle.md` — decision on status transitions and edit permissions per status
- `allergen-system-spec.md` — detailed allergen keyword mapping and override workflow
- `macro-calculator-integration.md` — spec for food database integration

### Conventions

- All times in code are UTC
- DTOs denormalize client names and creator names for UI convenience
- Macro targets are always decimal (allow half-gram precision)
- Allergen categories are enum-based for consistency
- Soft-delete applied only to MealPlan (not Days/Slots/Items)

## External Dependencies

- **Clients domain** — meal plans depend on clients; allergen checking reads `ClientAllergy` records
- **Compliance domain** — soft-delete and audit logging follow compliance standards
- **Auth domain** — `CreatedByUserId` must be valid `ApplicationUser`

## Queries Used Across the App

Meal plans queried/displayed in:

1. **Meal Plan List Page** — all plans with client and status filters
2. **Client Detail Page** — active meal plans for a specific client
3. **Meal Plan Detail/Edit Pages** — single plan with full structure
4. **Dashboard** — recent meal plans, active plans count
5. **Search Results** — meal plans included in global search
6. **AI Assistant Tools** — list/get/create/update operations

---

## Summary of Current State

**Complete:**

- Core meal plan entity hierarchy (MealPlan, MealPlanDay, MealSlot, MealItem)
- Allergen warning and override entities
- Full service layer with CRUD, PDF export, status transitions, allergen checking
- DTOs for list, detail, create, and edit operations
- Meal plan list and detail/edit Blazor pages
- PDF export functionality
- Real-time updates via `INotificationDispatcher`
- Status lifecycle validation
- Allergen checking and override workflow

**Missing / Incomplete:**

- Macro calculator / food database integration
- Meal plan templates / reusable structures
- Recurring/weekly plan cycles
- Client feedback mechanism
- Macro analytics and adherence tracking
- Food substitution suggestions
- Shopping list export

**Next Steps for Implementation:**

1. Create `/docs/meal-plans/adr-0001-status-lifecycle.md`
2. Create `/docs/meal-plans/allergen-system-spec.md`
3. Research food database API options for macro calculator
4. Create `MealPlanTemplate` entity if template feature is high priority
5. Wire meal plan pages into main navigation if not already done
