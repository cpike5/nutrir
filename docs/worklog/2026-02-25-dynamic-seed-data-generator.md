# Dynamic Seed Data Generator

**Date:** 2026-02-25
**Commit:** `1046fcc`

## Summary

Replaced ~650 lines of hardcoded development seed data in `DatabaseSeeder.cs` with a configurable, profile-driven generator powered by [Bogus](https://github.com/bchavez/Bogus). Generates clinically coherent test data across 8 archetypes (weight-management, diabetes, sports-nutrition, prenatal, IBS/FODMAP, cardiac-rehab, general-wellness, post-surgical) with consistent appointment notes, meal plans, and progress metrics tied to each profile.

## What Changed

### New Files (9)

- `src/Nutrir.Infrastructure/Data/Seeding/ClientProfile.cs` — 8 clinical archetypes with condition-specific templates for notes, meal plans, goals, and metrics
- `src/Nutrir.Infrastructure/Data/Seeding/FoodDatabase.cs` — ~90 curated food items with macro-consistent nutritional values and categorical tags (general, high-protein, low-fodmap, mediterranean, etc.)
- `src/Nutrir.Infrastructure/Data/Seeding/SeedDataGenerator.cs` — 3-stage orchestrator generating data in FK-dependency order (clients → child entities → audit logs)
- `src/Nutrir.Infrastructure/Data/Seeding/Generators/ClientGenerator.cs` — Canadian-diverse clients with age distributions, consent events, and soft-delete percentages
- `src/Nutrir.Infrastructure/Data/Seeding/Generators/AppointmentGenerator.cs` — Realistic scheduling with business hours, overlap prevention, and status distributions
- `src/Nutrir.Infrastructure/Data/Seeding/Generators/MealPlanGenerator.cs` — Full plan hierarchy (days → slots → items) drawing from profile-appropriate food pools
- `src/Nutrir.Infrastructure/Data/Seeding/Generators/ProgressGenerator.cs` — Goals + entries with trending weight measurements and profile-relevant metrics
- `src/Nutrir.Infrastructure/Data/Seeding/Generators/AuditLogGenerator.cs` — Audit trail derived from all generated entities
- `docs/infrastructure/seed-data-generator.md` — Developer guide with configuration reference

### Modified Files (4)

- `src/Nutrir.Infrastructure/Nutrir.Infrastructure.csproj` — Added Bogus v35.4.0 NuGet package
- `src/Nutrir.Infrastructure/Data/SeedOptions.cs` — Added `ClientCount`, `AppointmentsPerClient`, `MealPlansPerClient`, `ProgressEntriesPerClient`, `RandomSeed` options
- `src/Nutrir.Infrastructure/Data/DatabaseSeeder.cs` — Replaced hardcoded `SeedDashboardDataAsync` + helpers (~650 lines) with ~40 lines calling the staged generator
- `docs/README.md` — Added link to seed data generator documentation

## Design Decisions

**Profile-driven architecture:** Each client is assigned one of 8 clinical archetypes at generation time. All downstream entities (appointments, meal plans, progress entries, audit logs) are generated with coherent data based on that profile. A diabetes client receives diabetes-appropriate foods, blood sugar metrics in their progress tracking, and appointment notes reflecting diabetes nutrition counseling.

**3-stage persistence:** The generator splits persistence into three stages with `SaveChangesAsync` calls between each:
1. Generate and persist clients (EF Core assigns real database IDs)
2. Generate child entities (appointments, meal plans) referencing persisted client IDs
3. Generate audit logs for all created entities

This avoids FK constraint violations where child generators tried to reference `Client.Id = 0`.

**Deterministic by default:** `RandomSeed=42` in default development config ensures reproducible test data across container restarts and team development. Can be set to `null` in `appsettings.Development.json` for non-deterministic output.

**Bogus library:** Chosen for its Canadian locale support (realistic names, phone numbers, emails), seed-based reproducibility, and fluent API for building coherent test data.

## Verified Output (default config)

Generated data with `ClientCount=20`, `AppointmentsPerClient=4`, `MealPlansPerClient=1`, `ProgressEntriesPerClient=5`:

| Entity | Count |
|--------|-------|
| Clients | 20 |
| Appointments | 79 |
| Meal Plans | 18 |
| Progress Goals | 23 |
| Progress Entries | 86 |
| Audit Log Entries | 132 |

(Totals vary slightly due to soft-deletes and stochastic profile distributions.)

## Technical Notes

**FK constraint bug caught during Docker testing:** Initial implementation assigned `ClientId` at generation time when `Client.Id` was still 0. EF Core hadn't persisted the client yet, so foreign keys violated constraints. Fix: split `SeedDataGenerator` into 3 explicit stages with persistence between each.

**Food database macro consistency:** All food entries validate that `(protein*4 + carbs*4 + fat*9) ≈ calories` within 10% to ensure realistic nutritional data for meal plan calculations.

**Configuration:** Seed behavior is fully configurable via `appsettings.Development.json` under the `"Seed"` section:
```json
{
  "Seed": {
    "Enabled": true,
    "ClientCount": 20,
    "AppointmentsPerClient": 4,
    "MealPlansPerClient": 1,
    "ProgressEntriesPerClient": 5,
    "RandomSeed": 42
  }
}
```

Set `RandomSeed` to `null` for non-deterministic data, or `Enabled: false` to skip seeding on startup.
