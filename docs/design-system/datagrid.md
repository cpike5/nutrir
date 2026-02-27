# DataGrid Component

Reusable generic data grid with server-side pagination, 3-state column sorting, responsive column hiding, skeleton loading, and real-time update banner.

## When to Use

Use `DataGrid<TItem>` for any flat list page that displays tabular data with pagination. Currently used by ClientList, AppointmentList, and MealPlanList. **Not** suitable for multi-section pages like ProgressList.

## Component Files

```
src/Nutrir.Web/Components/UI/DataGrid/
    DataGrid.razor          Main grid component (generic)
    DataGrid.razor.css      Consolidated table styles
    DataGridColumn.razor    Column definition (parameter-only, no DOM)
    DataGridPager.razor     Pagination controls
    DataGridPager.razor.css Pager-specific styles
```

## DataGrid Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Items` | `IReadOnlyList<TItem>?` | `null` | Current page of data |
| `TotalCount` | `int` | `0` | Total matching records (across all pages) |
| `Page` | `int` | `1` | Current page number |
| `PageSize` | `int` | `25` | Items per page |
| `SortColumn` | `string?` | `null` | Currently sorted column key |
| `SortDirection` | `SortDirection` | `None` | Current sort direction |
| `OnQueryChanged` | `EventCallback<GridQuery>` | — | Fires on any sort or page change |
| `IsLoading` | `bool` | `false` | Shows skeleton loading rows |
| `ToolbarContent` | `RenderFragment?` | `null` | Slot for search/filter controls |
| `EmptyContent` | `RenderFragment?` | `null` | Custom empty state content |
| `Columns` | `RenderFragment?` | `null` | Contains `DataGridColumn` children |
| `ShowRealtimeBanner` | `bool` | `false` | Shows "Updated in real time" banner |
| `TableClass` | `string?` | `null` | Additional CSS class on `<table>` |

## DataGridColumn Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Key` | `string` | `""` | Unique column identifier (matches sort column names) |
| `Header` | `string` | `""` | Column header text |
| `CellTemplate` | `RenderFragment<TItem>?` | `null` | Template for rendering each cell |
| `Sortable` | `bool` | `false` | Enables click-to-sort on header |
| `HideBelow` | `string?` | `null` | CSS class for responsive hiding (`hide-below-860`, `hide-below-600`) |
| `Width` | `string?` | `null` | Optional fixed width (e.g., `"120px"`) |

`DataGridColumn` renders no DOM — it registers itself with the parent `DataGrid` via `CascadingParameter` during `OnInitialized`.

## DataGridPager Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `Page` | `int` | Current page |
| `PageSize` | `int` | Items per page |
| `TotalCount` | `int` | Total records |
| `OnPageChanged` | `EventCallback<int>` | Fires with new page number |

The pager renders automatically when `TotalCount > PageSize`. It displays "Showing X-Y of Z" text and up to 5 page number buttons centered on the current page.

## Supporting Types

### `SortDirection` (`Nutrir.Core.Enums`)

```csharp
public enum SortDirection { None, Ascending, Descending }
```

### `GridQuery` (`Nutrir.Core.DTOs`)

```csharp
public record GridQuery(
    int Page = 1, int PageSize = 25,
    string? SortColumn = null, SortDirection SortDirection = SortDirection.None);
```

Fired via `OnQueryChanged` when the user clicks a sort header or pagination button.

### `PagedResult<T>` (`Nutrir.Core.DTOs`)

```csharp
public record PagedResult<T>(
    IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => Page > 1;
    public bool HasNextPage => Page < TotalPages;
}
```

### Domain Query Records

Each list page has a domain-specific query record with pagination + filters:

- `ClientListQuery` — `SearchTerm`, `ConsentFilter`
- `AppointmentListQuery` — `From`, `To`, `StatusFilter`
- `MealPlanListQuery` — `ClientId`, `StatusFilter`

All include `Page`, `PageSize`, `SortColumn`, `SortDirection`.

## Usage Example

```razor
<DataGrid TItem="MealPlanSummaryDto"
          Items="@_result?.Items"
          TotalCount="@(_result?.TotalCount ?? 0)"
          Page="@_query.Page"
          PageSize="@_query.PageSize"
          SortColumn="@_query.SortColumn"
          SortDirection="@_query.SortDirection"
          OnQueryChanged="HandleQueryChanged"
          IsLoading="@_isLoading"
          ShowRealtimeBanner="@_refreshedByRealTime">
    <ToolbarContent>
        <div class="filter-group">
            <span class="filter-label">Status</span>
            <select class="filter-select" @onchange="OnStatusChanged">
                <option value="">All Statuses</option>
                <option value="Draft">Draft</option>
                <option value="Active">Active</option>
            </select>
        </div>
    </ToolbarContent>
    <Columns>
        <DataGridColumn TItem="MealPlanSummaryDto" Key="title" Header="Title" Sortable>
            <CellTemplate Context="plan">
                <span class="cell-title">@plan.Title</span>
            </CellTemplate>
        </DataGridColumn>
        <DataGridColumn TItem="MealPlanSummaryDto" Key="days" Header="Days" HideBelow="hide-below-860">
            <CellTemplate Context="plan">@plan.DayCount</CellTemplate>
        </DataGridColumn>
        <DataGridColumn TItem="MealPlanSummaryDto" Key="actions" Header="">
            <CellTemplate Context="plan">
                <div class="row-actions">
                    <a href="meal-plans/@plan.Id" class="btn-icon" title="View">...</a>
                </div>
            </CellTemplate>
        </DataGridColumn>
    </Columns>
    <EmptyContent>
        <div class="empty-state">
            <p class="empty-state-title">No meal plans found</p>
        </div>
    </EmptyContent>
</DataGrid>
```

### Page code pattern:

```csharp
private PagedResult<MealPlanSummaryDto>? _result;
private MealPlanListQuery _query = new();

private async Task LoadDataAsync()
{
    _isLoading = true;
    _result = await MealPlanService.GetPagedAsync(_query);
    _isLoading = false;
}

private async Task HandleQueryChanged(GridQuery gridQuery)
{
    _query = _query with
    {
        Page = gridQuery.Page,
        PageSize = gridQuery.PageSize,
        SortColumn = gridQuery.SortColumn,
        SortDirection = gridQuery.SortDirection
    };
    await LoadDataAsync();
}
```

## Sorting

**3-state cycle:** None -> Ascending -> Descending -> None

Clicking a sortable column header cycles through the states. Changing sort always resets to page 1. The `OnQueryChanged` callback fires with the new `GridQuery`.

**Keyboard:** Enter or Space triggers sort on focused headers.

**ARIA:** `aria-sort` attribute updates on `<th>` elements (`"none"`, `"ascending"`, `"descending"`). Sort indicator shows Unicode arrows: `↕` (unsorted), `↑` (ascending), `↓` (descending).

### Sort column keys per domain

| Domain | Column Keys |
|--------|-------------|
| Clients | `name`, `email`, `consent`, `created`, `lastappointment` |
| Appointments | `date`, `client`, `status` |
| Meal Plans | `title`, `client`, `status`, `created` |

Keys are matched case-insensitively in services via `query.SortColumn?.ToLowerInvariant()`.

## Pagination

The `DataGridPager` renders automatically when `TotalCount > PageSize`. It shows:
- "Showing X-Y of Z" info text
- Previous/Next buttons (disabled at boundaries)
- Up to 5 numbered page buttons centered on the current page

Page changes fire `OnQueryChanged` preserving the current sort state.

## Responsive Column Hiding

Use the `HideBelow` parameter on `DataGridColumn`:

| Value | Columns hidden at |
|-------|-------------------|
| `"hide-below-860"` | `max-width: 860px` |
| `"hide-below-600"` | `max-width: 600px` |

Both `<th>` and `<td>` elements receive the class, and `DataGrid.razor.css` applies `display: none` via `::deep` selectors.

## Loading State

When `IsLoading="true"`, the grid renders skeleton rows matching `PageSize` count. Each cell shows an animated pulse placeholder to prevent layout shift.

## Empty State

When `Items` is null or empty and `IsLoading` is false:
- If `EmptyContent` is provided, it renders that
- Otherwise renders a default "No results found" message

## Real-Time Banner

Set `ShowRealtimeBanner="true"` to display an "Updated in real time" banner with a pulsing dot above the table card. Pages typically set this flag after receiving a SignalR notification and reloading data.

## CSS Architecture

**`DataGrid.razor.css`** contains all shared table styles:
- `.table-card` wrapper, `.datagrid-table` base styles
- `.sortable` header styles, `.sort-indicator`
- Row hover, left border accent, `@keyframes rowFadeIn`
- `.row-actions`, `.btn-icon` (via `::deep`)
- `.skeleton-row`, `.skeleton-cell`, `@keyframes skeletonPulse`
- `.badge-dot` (via `::deep`)
- `.realtime-banner`, `.realtime-dot`, `@keyframes realtimePulse`
- `.toolbar`, `.sr-only`
- Responsive breakpoints for `hide-below-860`, `hide-below-600`

**Page-specific `.razor.css` files** retain only:
- Page layout (`.clients-page`, `.appointments-page`, etc.)
- Page header styles
- Filter/search input styles (rendered in ToolbarContent slot)
- Domain-specific cell formatting (`.client-avatar`, `.appt-datetime`, `.location-tag`, etc.)
- Domain-specific responsive adjustments

## Service Layer Pattern

Each domain service exposes a `GetPagedAsync` method alongside the existing `GetListAsync`:

```csharp
Task<PagedResult<ClientDto>> GetPagedAsync(ClientListQuery query);
```

Implementation pattern (follows `AuditLogService`):

1. Create short-lived DbContext via `IDbContextFactory`
2. Build `IQueryable` with WHERE filters
3. Apply ORDER BY via switch on `SortColumn`
4. `CountAsync()` on filtered query
5. `Skip((page - 1) * pageSize).Take(pageSize)`
6. Materialize and batch-resolve related names
7. Return `PagedResult<T>`

Sort columns that require joins (e.g., client name on appointments) are sorted in-memory within the page after materialization.
