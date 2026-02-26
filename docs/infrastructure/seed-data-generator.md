# Seed Data Generator

## Overview

The dynamic seed data generator produces varied, domain-coherent development data using [Bogus](https://github.com/bchavez/Bogus). It replaces any hardcoded seed rows with a configurable, profile-driven system that creates realistic clients, appointments, meal plans, progress records, and audit log entries in a single pass.

The generator only runs in development. `DatabaseSeeder.SeedAsync` accepts an `isDevelopment` parameter; only when `true` does it call `SeedDashboardDataAsync`, which invokes `SeedDataGenerator`. In production the method exits after seeding roles and the admin account.

Seeding is **idempotent**: if any `Client` rows already exist in the database, the entire dashboard seed step is skipped with an informational log message.

## Configuration

All options live in the `"Seed"` section of `appsettings.json` (or `appsettings.Development.json`). They are bound to `SeedOptions` (`src/Nutrir.Infrastructure/Data/SeedOptions.cs`).

| Property | Type | Default | Description |
|---|---|---|---|
| `AdminEmail` | `string` | `"admin@nutrir.ca"` | Email used to create (or locate) the seeded admin account |
| `AdminPassword` | `string` | `"ChangeMe123!"` | Password for the seeded admin and nutritionist accounts |
| `ClientCount` | `int` | `20` | Number of client records to generate |
| `AppointmentsPerClient` | `int` | `4` | Average appointments per client — actual count varies ±2 |
| `MealPlansPerClient` | `int` | `1` | Average meal plans per client — actual count varies ±1 |
| `ProgressEntriesPerClient` | `int` | `6` | Average progress entries per client — actual count varies ±3 |
| `RandomSeed` | `int?` | `42` | Bogus random seed. Set to `null` for non-deterministic output |

Example `appsettings.Development.json` override:

```json
"Seed": {
  "AdminEmail": "admin@nutrir.ca",
  "AdminPassword": "ChangeMe123!",
  "ClientCount": 50,
  "AppointmentsPerClient": 6,
  "MealPlansPerClient": 2,
  "ProgressEntriesPerClient": 10,
  "RandomSeed": 42
}
```

Set `RandomSeed` to `null` if you want a different dataset on every application restart:

```json
"RandomSeed": null
```

## Architecture

### File Layout

```
src/Nutrir.Infrastructure/Data/
├── SeedOptions.cs                    Options class bound to "Seed" config section
├── DatabaseSeeder.cs                 Entry point — calls SeedDataGenerator in dev
└── Seeding/
    ├── SeedDataGenerator.cs          Orchestrator — coordinates all generators
    ├── ClientProfile.cs              8 clinical archetypes driving all generation
    ├── FoodDatabase.cs               ~90 tagged food entries with consistent macros
    └── Generators/
        ├── ClientGenerator.cs        Clients + ConsentEvents
        ├── AppointmentGenerator.cs   Appointments
        ├── MealPlanGenerator.cs      MealPlans + Days + Meals + MealItems
        ├── ProgressGenerator.cs      ProgressGoals + ProgressEntries + Measurements
        └── AuditLogGenerator.cs      AuditLogEntries derived from generated entities
```

### Profile-Driven Design

Every client is assigned a random `ClientProfile` at generation time. The profile is a record that carries all the domain-specific parameters needed to produce coherent downstream data:

- **Note templates** — realistic appointment and progress note text
- **Meal plan title/description templates** — contextually appropriate plan names
- **Macro targets** — calorie and macronutrient ranges appropriate to the condition
- **Relevant goal types** — `GoalType` enum values likely for this client population
- **Relevant metrics** — `MetricType` enum values tracked for this condition
- **Goal title templates** — realistic goal descriptions
- **Food pool tags** — which food categories to draw from when building meal items

The profile cascades through every generator: `ClientGenerator` assigns it, `MealPlanGenerator` and `ProgressGenerator` receive it via the `GeneratedClient` record, and they use it to select appropriate foods, metrics, and copy.

### Generation Pipeline

`SeedDataGenerator.Generate` runs the pipeline in dependency order:

```
ClientGenerator        → GeneratedClient[]  (Client + Profile + ConsentEvents)
        |
        ├─ AppointmentGenerator   → Appointment[]
        ├─ MealPlanGenerator      → MealPlan[] (with full hierarchy)
        └─ ProgressGenerator      → ProgressGoal[] + ProgressEntry[]
                                            |
                                  AuditLogGenerator  → AuditLogEntry[]
```

`DatabaseSeeder` then persists the results in three staged `SaveChangesAsync` calls to satisfy foreign key constraints:

1. `Clients` (ConsentEvents saved via cascade)
2. `Appointments`, `MealPlans`, `ProgressGoals`, `ProgressEntries`
3. `AuditLogEntries`

### Bogus Faker Configuration

`SeedDataGenerator` initialises Bogus with `"en_CA"` locale so generated names, phone numbers, and addresses are Canadian in flavour. The `RandomSeed` option sets `Randomizer.Seed` globally before any generator runs, which ensures the entire pipeline is deterministic when a fixed seed is provided.

## Client Profiles

Eight archetypes are defined as static entries in `ClientProfile.All`. The profile tag is not stored on the `Client` entity — it exists only during generation to drive data creation.

| Profile Tag | Calorie Range | Tracked Metrics | Food Focus |
|---|---|---|---|
| `weight-management` | 1800–2000 kcal | Weight, BodyFatPercentage, WaistCircumference | Lean protein, controlled portions, low-calorie |
| `diabetes` | 1600–1800 kcal | Weight, BloodPressureSystolic, BloodPressureDiastolic | Low-GI, consistent carbohydrate portions |
| `sports-nutrition` | 2800–3200 kcal | Weight, BodyFatPercentage | High-carb, energy-dense, high-protein |
| `prenatal` | 2200–2400 kcal | Weight | Iron-rich, folate-rich, prenatal-safe foods |
| `ibs-fodmap` | 1700–1900 kcal | Weight, WaistCircumference | Low-FODMAP safe foods only |
| `cardiac-rehab` | 1800–2000 kcal | Weight, BloodPressureSystolic, BloodPressureDiastolic, RestingHeartRate | Low-sodium, Mediterranean-style |
| `general-wellness` | 1800–2000 kcal | Weight | Balanced whole foods |
| `post-surgical` | 2000–2200 kcal | Weight, BodyFatPercentage | High-protein, anti-inflammatory |

Each profile also carries three meal plan title/description templates and three goal title templates, picked at random when generating that client's data.

## Food Database

`FoodDatabase` (`src/Nutrir.Infrastructure/Data/Seeding/FoodDatabase.cs`) contains approximately 90 curated `FoodEntry` records covering proteins, legumes, grains, dairy, vegetables, fruits, healthy fats, and composite meals.

### Structure

```csharp
public record FoodEntry(
    string Name,
    decimal Quantity,
    string Unit,
    decimal CaloriesKcal,
    decimal ProteinG,
    decimal CarbsG,
    decimal FatG,
    string[] Tags,
    string? Notes = null);
```

### Macro Consistency Rule

Every entry must satisfy:

```
(ProteinG * 4 + CarbsG * 4 + FatG * 9) ≈ CaloriesKcal  (within 10%)
```

Inline comments on each entry show the check explicitly, for example:

```csharp
// Salmon fillet: P25*4=100, C0*4=0, F13*9=117 => 217 vs 220 (1.4%)
new("Salmon Fillet (baked)", 150m, "g", 220m, 25m, 0m, 13m, ...)
```

### Tags

Tags drive which foods appear in which client profiles. A food may carry multiple tags. The `general` tag is on every entry and serves as a fallback pool.

| Tag | Meaning |
|---|---|
| `general` | Included in all profiles |
| `high-protein` | Elevated protein content |
| `high-carb` | Elevated carbohydrate content |
| `low-calorie` | Suitable for caloric-deficit plans |
| `energy-dense` | High calorie-per-gram ratio |
| `low-gi` | Low glycaemic index |
| `diabetic-friendly` | Appropriate for blood sugar management |
| `low-fodmap` | Safe during FODMAP elimination |
| `low-sodium` | Suitable for cardiac and hypertension plans |
| `mediterranean` | Aligns with Mediterranean diet pattern |
| `iron-rich` | High dietary iron |
| `folate-rich` | High folate / folic acid |
| `prenatal` | Safe and appropriate during pregnancy |
| `anti-inflammatory` | Anti-inflammatory food sources |
| `balanced` | General balanced eating |

### Querying the Food Database

```csharp
// All foods tagged "low-fodmap"
var fodmapFoods = FoodDatabase.GetByTag("low-fodmap");

// Foods matching any of the profile's tags
var profileFoods = FoodDatabase.GetByTags(profile.FoodPoolTags);
```

### Adding New Foods

1. Open `FoodDatabase.cs` and locate the appropriate section (Proteins, Grains, etc.).
2. Add a new `FoodEntry` with accurate macros. Verify the consistency rule: `(P*4 + C*4 + F*9)` should be within 10% of `CaloriesKcal`.
3. Assign appropriate tags. Include `"general"` unless the food is highly specialised.
4. Add an inline comment showing the macro check for future reviewers.

## Determinism

When `RandomSeed` is set to an integer value (default `42`), `Randomizer.Seed` is fixed before any generation begins. Running the application with the same seed and the same `ClientCount` produces identical entity data every time, which is useful for:

- Reproducing specific UI states during debugging
- Stable screenshot or demo data
- Consistent test assertions against seeded records

Setting `RandomSeed: null` uses a random seed drawn from `Randomizer.Seed.Next()` at startup, producing a different dataset on each database reset.

Note that determinism applies to the **generated data values** only. The database-assigned primary keys (`Guid`) are generated by EF Core at insert time and will differ between runs even with the same seed.

## Extending the Generator

### Adding a New Client Profile

1. Open `ClientProfile.cs` and add a new `ClientProfile(...)` entry to the `All` array.
2. Define all required constructor arguments:
   - `Tag` — a unique kebab-case identifier
   - `NoteTemplates` — four realistic appointment/progress notes
   - `MealPlanTemplates` — three `(Title, Description)` tuples
   - `MacroTargets` — `(MinCal, MaxCal, MinP, MaxP, MinC, MaxC, MinF, MaxF)`
   - `RelevantGoalTypes` — applicable `GoalType` enum values
   - `RelevantMetrics` — applicable `MetricType` enum values
   - `GoalTitleTemplates` — three realistic goal descriptions
   - `FoodPoolTags` — tags from `FoodDatabase` that suit this profile
3. No other changes are required — the profile will be picked up automatically by `ClientProfile.All` and assigned to clients at random.

### Adding a New Generator

1. Create a new class in `src/Nutrir.Infrastructure/Data/Seeding/Generators/`.
2. Accept a `Faker` instance via constructor (do not create a new `Faker` inside the generator — share the one from `SeedDataGenerator`).
3. Return a typed list from a `Generate(...)` method.
4. Add the new entity type to the `SeedDataSet` record in `SeedDataGenerator.cs`.
5. Call the new generator in `SeedDataGenerator.Generate` and include its output in the returned `SeedDataSet`.
6. Add the appropriate `AddRange` / `SaveChangesAsync` call in `DatabaseSeeder.SeedDashboardDataAsync`, placed after any entities it references via foreign key.

## Troubleshooting

### Seeder Is Skipped on Startup

`SeedDashboardDataAsync` skips if `_dbContext.Clients.AnyAsync()` returns `true`. This is by design to prevent duplicate data on repeated restarts. To re-seed, clear the database first (see below).

### Seeder Does Not Run at All

Check that the application is starting in Development environment. The Docker Compose service requires `ASPNETCORE_ENVIRONMENT=Development` to be set. Without it, `isDevelopment` is `false` and only roles and the admin account are seeded.

### Clearing the Database to Re-seed

**Option 1 — Docker volume reset (full reset):**

```bash
docker compose down -v
docker compose up -d
```

This destroys the PostgreSQL data volume and re-runs all migrations and seeding on next startup.

**Option 2 — Truncate tables only:**

Connect to the database (port `7103`, user `nutrir`, password `nutrir_dev`) and truncate the client-side tables in reverse dependency order:

```sql
TRUNCATE TABLE "AuditLogEntries" CASCADE;
TRUNCATE TABLE "ProgressEntries" CASCADE;
TRUNCATE TABLE "ProgressGoals" CASCADE;
TRUNCATE TABLE "MealPlans" CASCADE;
TRUNCATE TABLE "Appointments" CASCADE;
TRUNCATE TABLE "Clients" CASCADE;
```

The seeder will re-run on next application startup.

### Bogus Version Compatibility

Bogus is pinned in `Nutrir.Infrastructure.csproj`. If upgrading Bogus, verify that `Randomizer.Seed` assignment is still the correct API for global seed control, as this has shifted between major versions.
