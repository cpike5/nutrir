# Food/Nutrition Database

## Overview

The food database provides a curated set of foods with accurate macronutrient data that practitioners can search and select when building meal plans. Selecting a food auto-fills quantity, unit, calories, protein, carbs, and fat fields, reducing manual data entry errors.

## Entity: `Food`

| Field | Type | Description |
|-------|------|-------------|
| `Id` | int | Primary key |
| `Name` | string (200, unique) | Food name, e.g. "Chicken Breast (grilled)" |
| `ServingSize` | decimal (18,2) | Default serving size |
| `ServingSizeUnit` | string (50) | Unit: g, mL, oz, cup, piece, serving |
| `CaloriesKcal` | decimal (18,2) | Calories per serving |
| `ProteinG` | decimal (18,2) | Protein grams per serving |
| `CarbsG` | decimal (18,2) | Carbohydrate grams per serving |
| `FatG` | decimal (18,2) | Fat grams per serving |
| `Tags` | text[] | Categorical tags (e.g. "high-protein", "mediterranean") |
| `Notes` | text | Optional notes |
| `IsDeleted` | bool | Soft-delete flag |
| `CreatedAt` | datetime | Creation timestamp |
| `DeletedAt` | datetime? | Deletion timestamp |
| `DeletedBy` | string? | User who deleted |

### Relationship to MealItem

`MealItem` has an optional `FoodId` (nullable int FK to `Food`). When a food is selected from the autocomplete, the `FoodId` is stored alongside the free-text `FoodName`. The `FoodName` field is always populated (either from the database or typed manually), ensuring backward compatibility with existing meal plans.

## Seed Data

The initial 94 foods are seeded from `src/Nutrir.Infrastructure/Data/Seeding/FoodDatabase.cs` via a SQL data migration. Foods span categories: proteins, legumes, grains/starches, dairy, vegetables, fruits, healthy fats/nuts, prepared foods, and specialty/dietary items.

Each seed entry includes validated macronutrient data where `(ProteinG * 4 + CarbsG * 4 + FatG * 9)` approximates `CaloriesKcal` within 10%.

## Search

- Case-insensitive substring matching via `EF.Functions.ILike`
- Default limit: 15 results
- Results ordered alphabetically by name
- Soft-deleted foods are excluded via a global query filter

## Autocomplete UX

The `FoodAutocomplete` component in the meal plan editor:
1. Accepts text input with 300ms debounce
2. Searches the food database when 2+ characters are typed
3. Displays matching foods with macro summary in a dropdown
4. Supports keyboard navigation (Up/Down/Enter/Escape)
5. On selection: auto-fills quantity, unit, calories, protein, carbs, fat
6. Free-text fallback: practitioners can type any food name without selecting from the database

## Admin Management

Admin users can manage foods at `/admin/foods`:
- **List**: Searchable table of all foods with macros and tags
- **Create**: Add new food at `/admin/foods/create`
- **Edit**: Update food at `/admin/foods/{id}/edit`
- **Delete**: Soft-delete with confirmation dialog

All CRUD operations are audit-logged.

## Service Layer

`IFoodService` / `FoodService`:
- `SearchAsync(query, limit)` - autocomplete search
- `GetByIdAsync(id)` - single food lookup
- `GetAllAsync()` - all non-deleted foods
- `CreateAsync(dto, userId)` - create with audit log
- `UpdateAsync(id, dto, userId)` - update with audit log
- `SoftDeleteAsync(id, userId)` - soft-delete with audit log

Uses `IDbContextFactory<AppDbContext>` pattern for Blazor Server compatibility.
