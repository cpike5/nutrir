# .NET Codebase Deep-Dive Review — Code Duplication & Simplification Opportunities

**Date:** 2026-03-15
**Scope:** All 266 C# files and 99 Razor components across Nutrir.Core, Nutrir.Infrastructure, Nutrir.Web, Nutrir.Cli

---

## Executive Summary

The Nutrir codebase follows clean architecture principles with clear layer separation. However, as the codebase has grown to ~266 C# files and ~99 Razor components, several patterns of code duplication have emerged. This review identifies **7 high-priority** and **12 medium-priority** refactoring opportunities that would eliminate an estimated 800–1,200 lines of duplicated code and significantly improve maintainability.

---

## 1. Entity Layer — Missing Base Class (CRITICAL)

### Problem

15+ entities independently declare identical soft-delete and audit-tracking properties:

```csharp
// Repeated in Client, Appointment, MealPlan, ProgressEntry, ProgressGoal,
// ClientAllergy, ClientMedication, ClientCondition, ClientDietaryRestriction,
// SessionNote, Allergen, Medication, Condition, PractitionerTimeBlock, PractitionerSchedule, IntakeForm
public int Id { get; set; }
public bool IsDeleted { get; set; }
public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
public DateTime? UpdatedAt { get; set; }
public DateTime? DeletedAt { get; set; }
public string? DeletedBy { get; set; }
```

**Inconsistencies found:**
- `ApplicationUser` uses `CreatedDate` instead of `CreatedAt`
- `ConsentForm` uses `GeneratedAt` instead of `CreatedAt`
- `AuditLogEntry`, `ConsentEvent` — no soft-delete (append-only by design, acceptable)
- `ProgressMeasurement`, `AiConversationMessage` — missing audit properties entirely

### Recommendation

Create a base entity class and optional interface:

```csharp
public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAt { get; set; }
    string? DeletedBy { get; set; }
}

public abstract class AuditableEntity : ISoftDeletable
{
    public int Id { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
}
```

**Impact:** 13 entities refactored, ~78 duplicated lines eliminated, enables generic soft-delete helpers in DbContext.

---

## 2. Soft-Delete Logic — Duplicated Across 5+ Services (HIGH)

### Problem

Identical soft-delete method bodies appear in `ClientService`, `AppointmentService`, `MealPlanService`, `ProgressService`, and `SessionNoteService`:

```csharp
public async Task<bool> SoftDeleteAsync(int id, string userId)
{
    var entity = await _dbContext.[Entity].FindAsync(id);
    if (entity is null) return false;

    entity.IsDeleted = true;
    entity.DeletedAt = DateTime.UtcNow;
    entity.DeletedBy = userId;

    await _dbContext.SaveChangesAsync();

    _logger.LogInformation("[Entity] soft-deleted: {EntityId} by {UserId}", id, userId);

    await _auditLogService.LogAsync(userId, "[Entity]SoftDeleted", "[Entity]",
        id.ToString(), "Soft-deleted [entity] record");

    await TryDispatchAsync("[Entity]", id, EntityChangeType.Deleted, userId);
    return true;
}
```

### Recommendation

Create a generic soft-delete extension or helper:

```csharp
public static class SoftDeleteExtensions
{
    public static async Task<bool> SoftDeleteAsync<T>(
        this DbContext db, int id, string userId,
        IAuditLogService audit, INotificationDispatcher dispatcher, ILogger logger)
        where T : AuditableEntity { ... }
}
```

**Impact:** ~50 duplicated lines per service × 5 services = ~250 lines.

---

## 3. TryDispatchAsync — Copy-Pasted into 5 Services (HIGH)

### Problem

This identical private method exists in `ClientService`, `AppointmentService`, `MealPlanService`, `SessionNoteService`, and `IntakeFormService`:

```csharp
private async Task TryDispatchAsync(string entityType, int entityId,
    EntityChangeType changeType, string practitionerUserId)
{
    try
    {
        await _notificationDispatcher.DispatchAsync(new EntityChangeNotification(
            entityType, entityId, changeType, practitionerUserId, DateTime.UtcNow));
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Failed to dispatch {ChangeType} notification for {EntityType} {EntityId}",
            changeType, entityType, entityId);
    }
}
```

### Recommendation

Move to an extension method on `INotificationDispatcher` or create a base service class:

```csharp
public static class NotificationDispatcherExtensions
{
    public static async Task TryDispatchAsync(this INotificationDispatcher dispatcher,
        string entityType, int entityId, EntityChangeType changeType,
        string userId, ILogger logger) { ... }
}
```

**Impact:** Eliminates ~60 lines of identical code.

---

## 4. User Name Resolution — Duplicated in 4+ Services (HIGH)

### Problem

Four services implement identical private methods to resolve user display names:

```csharp
// Duplicated in ClientService, AppointmentService, MealPlanService, ProgressService
private async Task<string?> GetUserNameAsync(string userId)
{
    var user = await _dbContext.Users.FindAsync(userId);
    if (user is ApplicationUser appUser)
        return !string.IsNullOrEmpty(appUser.DisplayName)
            ? appUser.DisplayName
            : $"{appUser.FirstName} {appUser.LastName}".Trim();
    return null;
}
```

Additionally, a **batch-resolve** variant repeats in 5 services:

```csharp
var userIds = entities.Select(x => x.CreatedByUserId).Distinct().ToList();
var names = await db.Users
    .Where(u => userIds.Contains(u.Id))
    .OfType<ApplicationUser>()
    .ToDictionaryAsync(u => u.Id, u =>
        !string.IsNullOrEmpty(u.DisplayName) ? u.DisplayName
        : $"{u.FirstName} {u.LastName}".Trim());
```

### Recommendation

Create a shared `IUserNameResolver` service:

```csharp
public interface IUserNameResolver
{
    Task<string?> GetDisplayNameAsync(string userId);
    Task<Dictionary<string, string>> BatchResolveAsync(IEnumerable<string> userIds);
}
```

**Impact:** Eliminates ~80 lines of duplicated name-resolution logic.

---

## 5. Multi-Term Search Pattern — Duplicated in 5+ Locations (HIGH)

### Problem

Identical text search logic appears in `ClientService.GetListAsync`, `ClientService.GetPagedAsync`, `SearchService.SearchClientsAsync`, `AppointmentService.GetPagedAsync`, and `UserManagementService.GetUsersAsync`:

```csharp
var terms = searchTerm.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
foreach (var term in terms)
{
    query = query.Where(c =>
        c.FirstName.ToLower().Contains(term) ||
        c.LastName.ToLower().Contains(term) ||
        (c.Email != null && c.Email.ToLower().Contains(term)));
}
```

### Recommendation

Create IQueryable extension methods:

```csharp
public static class SearchQueryExtensions
{
    public static IQueryable<T> ApplyMultiTermSearch<T>(
        this IQueryable<T> query, string? searchTerm,
        params Expression<Func<T, string?>>[] fields) { ... }
}
```

**Impact:** Centralizes search logic, ensures consistent behavior across all search features.

---

## 6. Paging + Sorting Pattern — Duplicated in 4+ Services (HIGH)

### Problem

The same paging and sorting infrastructure repeats across `ClientService`, `AppointmentService`, `MealPlanService`, and `AuditLogService`:

```csharp
var page = Math.Max(1, query.Page);
var pageSize = query.PageSize;
// ... filtering ...
var totalCount = await dbQuery.CountAsync();
// ... sort switch statement ...
var entities = await dbQuery
    .Skip((page - 1) * pageSize)
    .Take(pageSize)
    .ToListAsync();
return new PagedResult<TDto>(dtos, totalCount, page, pageSize);
```

Additionally, **`GridQuery` exists as a base record but is never used** — all list query DTOs (`ClientListQuery`, `AppointmentListQuery`, `MealPlanListQuery`) independently redeclare `Page`, `PageSize`, `SortColumn`, and `SortDirection`.

### Recommendation

1. **Fix DTO inheritance** — make list queries extend `GridQuery`:
```csharp
public record ClientListQuery(
    int Page = 1, int PageSize = 25,
    string? SortColumn = null, SortDirection SortDirection = SortDirection.None,
    string? SearchTerm = null, string? ConsentFilter = null
) : GridQuery(Page, PageSize, SortColumn, SortDirection);
```

2. **Create paging extension**:
```csharp
public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int page, int pageSize) { ... }
}
```

**Impact:** Eliminates ~40 lines per service, ensures consistent paging behavior.

---

## 7. Health Profile Service — Four Identical CRUD Subsystems (HIGH)

### Problem

`ClientHealthProfileService.cs` contains four nearly identical CRUD method sets (~300 lines total) for:
- Allergies (lines 39–151)
- Medications (lines 153–241)
- Conditions (lines 243–333)
- Dietary Restrictions (lines 335–419)

Each follows the exact same pattern: validate client exists → create/update/delete entity → log → audit → dispatch notification.

Similarly, three lookup services (`ConditionService`, `MedicationService`, `AllergenService`) implement nearly identical `SearchAsync` and `GetOrCreateAsync` methods with identical race-condition handling.

### Recommendation

Extract a generic interface and base implementation:

```csharp
public interface IReferenceLookupService<TEntity> where TEntity : class
{
    Task<List<TEntity>> SearchAsync(string query, int limit = 10);
    Task<TEntity> GetOrCreateAsync(string name, string? category = null);
}
```

For health profile CRUD, consider a generic helper class with action callbacks.

**Impact:** ~300 lines in ClientHealthProfileService, ~150 lines across lookup services.

---

## 8. Blazor Components — List Page Duplication (HIGH)

### Problem

All four list pages (`ClientList.razor`, `AppointmentList.razor`, `MealPlanList.razor`, `ProgressList.razor`) duplicate:

| Pattern | Approximate Lines | Occurrences |
|---------|------------------|-------------|
| `OnInitializedAsync` with notification setup | 6 | 4 |
| Debounced data loading (Timer-based) | 12 | 2+ |
| `OnEntityChanged` handler with debounce | 12 | 4 |
| Real-time update banner markup | 5 | 4 |
| `IAsyncDisposable` cleanup | 7 | 4 |
| Loading/not-found state markup | 10 | 4+ |

**Example — identical in every list page:**
```csharp
protected override async Task OnInitializedAsync()
{
    NotificationService.OnEntityChanged += OnEntityChanged;
    await NotificationService.StartAsync();
    await LoadDataAsync();
}
```

### Recommendation

Create a `RealTimeListBase<TDto>` component base class that encapsulates:
- Notification subscription/disposal
- Debounced reload
- Entity change handling
- Loading/error state management

**Impact:** ~50 lines per list page × 4 pages = ~200 lines.

---

## 9. Blazor Components — Form Page Patterns (MEDIUM)

### Problem

All create/edit pages duplicate:

| Pattern | Occurrences |
|---------|-------------|
| Error banner markup (SVG + message) | 6+ |
| Submit button with loading state | 6+ |
| `GetFieldError()` validation helper | 6+ |
| `EditContext` initialization | 6+ |
| Auth state → userId extraction | 15+ |
| `_isSubmitting` / `_errorMessage` state fields | 10+ |

**Most repeated pattern (15+ pages):**
```csharp
var authState = await AuthState;
var userId = authState.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;
```

### Recommendation

1. **Extract `GetUserIdAsync()`** into a shared service or component base
2. **Create `FormPageBase<TModel>`** that handles EditContext, validation, and submit state
3. **Create shared `ErrorBanner.razor`** and `DeleteConfirmDialog.razor` components

**Impact:** ~20 lines per form × 10+ forms = ~200 lines.

---

## 10. Blazor Widgets — Health Profile Section Duplication (MEDIUM)

### Problem

`AllergiesSection.razor`, `ConditionsSection.razor`, `MedicationsSection.razor`, and `DietaryRestrictionsSection.razor` each implement identical:

- State variables: `_collapsed`, `_showAddForm`, `_isSaving`, `_formError`, `_editingId`, `_deletingId`
- Methods: `ShowAddForm()`, `CancelAdd()`, `SaveAdd()`, `StartEdit()`, `CancelEdit()`, `SaveEdit()`, `ConfirmDelete()`, `CancelDelete()`, `ExecuteDelete()`
- Identical `GetUserIdAsync()` helper
- Same inline form markup structure

### Recommendation

Create a `CrudSectionBase<TDto, TCreateDto>` component base or a generic `InlineCrudSection<T>` component.

**Impact:** ~100 lines per widget × 4 widgets = ~400 lines.

---

## 11. Enum Formatting Methods — Scattered Across Files (MEDIUM)

### Problem

`FormatType()`, `FormatLocation()`, `FormatStatus()`, and `GetStatusVariant()` methods for appointment enums are duplicated between `AppointmentList.razor` and `AppointmentDetail.razor`. Similar pattern exists for progress-related formatters.

### Recommendation

Create a static `EnumFormatters` class in the UI layer:

```csharp
public static class EnumFormatters
{
    public static string Format(AppointmentType type) => type switch { ... };
    public static string Format(AppointmentStatus status) => status switch { ... };
    public static BadgeVariant GetVariant(AppointmentStatus status) => status switch { ... };
}
```

**Impact:** Eliminates ~60 lines of duplicated switch expressions.

---

## 12. PDF Rendering — Shared Styles & Structure (MEDIUM)

### Problem

Three PDF renderers (`MealPlanPdfRenderer`, `ConsentFormPdfRenderer`, `DataExportPdfRenderer`) duplicate:

| Pattern | Occurrences |
|---------|-------------|
| Color constants (`PrimaryColor`, `TextColor`, `MutedColor`) | 3 |
| Document factory setup (Page size, margins, text style) | 3 |
| Footer with page numbers | 3 |
| Table header cell styling | 8+ (DataExportPdfRenderer alone) |
| Alternating row background | 8+ |
| Empty state "No results" text | 9+ |

### Recommendation

```csharp
public static class PdfStyles
{
    public const string PrimaryColor = "#2d6a4f";
    public const string TextColor = "#2a2d2b";
    public const string MutedColor = "#636865";
    public const string AlternateRowColor = "#f9f9f9";

    public static void ComposeStandardFooter(IContainer container, string documentTitle) { ... }
    public static string GetRowBackground(int index) => index % 2 == 1 ? AlternateRowColor : "#ffffff";
}
```

**Impact:** Centralizes visual consistency, eliminates ~80 lines.

---

## 13. DbContext Query Filter Repetition (LOW)

### Problem

`AppDbContext.OnModelCreating` applies `HasQueryFilter(e => !e.IsDeleted)` individually to 16+ entities:

```csharp
builder.Entity<Client>(entity => { entity.HasQueryFilter(c => !c.IsDeleted); });
builder.Entity<Appointment>(entity => { entity.HasQueryFilter(a => !a.IsDeleted); });
// ... 14 more times
```

### Recommendation

With the `ISoftDeletable` interface from item #1:

```csharp
foreach (var entityType in modelBuilder.Model.GetEntityTypes()
    .Where(t => typeof(ISoftDeletable).IsAssignableFrom(t.ClrType)))
{
    // Apply query filter dynamically
}
```

**Impact:** Eliminates ~16 repetitive filter lines, auto-applies to new entities.

---

## 14. CLI Command Boilerplate (LOW)

### Problem

Each of the 9 CLI command files repeats:
- `using var host = CliHostBuilder.Build(connStr);`
- `using var scope = host.Services.CreateScope();`
- Try-catch-finally error handling
- `OutputFormatter.Write()` / `OutputFormatter.WriteError()` patterns

~50 boilerplate lines per command file.

### Recommendation

Create `CliCommandHelper.ExecuteAsync<TService>()`:

```csharp
public static async Task ExecuteAsync<TService>(
    string? connStr, string format,
    Func<TService, Task> action) where TService : class
{
    using var host = CliHostBuilder.Build(connStr);
    using var scope = host.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<TService>();
    try { await action(service); }
    catch (Exception ex) { OutputFormatter.WriteError(format, ex.Message); }
}
```

**Impact:** ~300 lines across 9 command files.

---

## Summary Table

| # | Area | Severity | Est. Lines Eliminated | Effort |
|---|------|----------|----------------------|--------|
| 1 | Base entity class (`AuditableEntity`) | CRITICAL | 78 | Low |
| 2 | Generic soft-delete helper | HIGH | 250 | Medium |
| 3 | `TryDispatchAsync` extraction | HIGH | 60 | Low |
| 4 | `IUserNameResolver` service | HIGH | 80 | Low |
| 5 | Multi-term search extension | HIGH | 40 | Low |
| 6 | Paging extensions + DTO inheritance | HIGH | 160 | Medium |
| 7 | Generic health profile CRUD | HIGH | 450 | Medium |
| 8 | `RealTimeListBase` component | HIGH | 200 | Medium |
| 9 | Form page base class | MEDIUM | 200 | Medium |
| 10 | CRUD section widget base | MEDIUM | 400 | Medium |
| 11 | Enum formatters consolidation | MEDIUM | 60 | Low |
| 12 | PDF styles & helpers | MEDIUM | 80 | Low |
| 13 | Dynamic query filter registration | LOW | 16 | Low |
| 14 | CLI command helper | LOW | 300 | Low |
| | **Total** | | **~2,374** | |

---

## Recommended Implementation Order

### Phase 1 — Foundation (Low risk, high leverage)
1. Create `AuditableEntity` base class + `ISoftDeletable` interface
2. Create `IUserNameResolver` service
3. Extract `TryDispatchAsync` to extension method
4. Consolidate enum formatters

### Phase 2 — Service Layer (Medium risk)
5. Create paging/sorting extensions + fix DTO inheritance from `GridQuery`
6. Create multi-term search extensions
7. Extract generic soft-delete helper
8. Create `IReferenceLookupService<T>` for lookup services

### Phase 3 — UI Layer (Medium risk, high reward)
9. Create PDF style constants and helpers
10. Create `RealTimeListBase` Blazor component base
11. Create shared form components (`ErrorBanner`, `DeleteConfirmDialog`)
12. Create `CrudSectionBase` for health profile widgets

### Phase 4 — Polish (Low priority)
13. Dynamic query filter registration in DbContext
14. CLI command helper
15. Refactor `ClientHealthProfileService` into smaller services

---

## Non-Standard Patterns Worth Noting

| Pattern | Location | Issue |
|---------|----------|-------|
| `CreatedDate` vs `CreatedAt` | `ApplicationUser` | Inconsistent naming |
| `GeneratedAt` vs `CreatedAt` | `ConsentForm` | Inconsistent naming |
| `GridQuery` exists but unused | `DTOs/GridQuery.cs` | Dead code — list queries redeclare fields |
| `AuditLogPageResult` alongside `PagedResult<T>` | `DTOs/` | Redundant paging DTOs |
| `AuditLogQueryRequest` alongside `GridQuery` | `DTOs/` | Redundant query DTOs |
| Mixed `_dbContext` and `_dbContextFactory` usage | Various services | Some services use both; should standardize on factory for Blazor Server |
