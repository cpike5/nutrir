# Error Pages Implementation Plan

## 1. Requirement Summary

Add styled error pages (404, 403, 500, 503) to the Nutrir Blazor Server application. The pages use a dedicated error layout with a simplified command-center shell (icon rail with brand only, minimal topbar with breadcrumb, status bar with system status) and centered error content card. The design is defined in the HTML prototype at `docs/prototypes/pages/error-pages.html`.

## 2. Context

- **Architecture layer:** Nutrir.Web (UI layer) only — no Core or Infrastructure changes needed.
- **UI structure:** `Components/Layout/` for layouts, `Components/Pages/` for routed pages, `Components/UI/` for reusable components.
- **Existing layouts:** `MainLayout.razor` (command-center with icon rail, topbar, status bar) and `AuthLayout.razor` (centered card, no nav). Error pages need a third layout.
- **CSS approach:** Custom design system (`wwwroot/css/design-system.css`) with palette files, layout CSS (`wwwroot/css/layout.css`), and app-level CSS (`wwwroot/app.css`). Button classes `.btn`, `.btn-primary`, `.btn-ghost` already exist in `design-system.css`.
- **Entry point:** `src/Nutrir.Web/Program.cs` — currently only sets `UseExceptionHandler("/Error")` in non-Development mode. No `UseStatusCodePagesWithReExecute` is configured.
- **Router:** `src/Nutrir.Web/Components/Routes.razor` — uses `AuthorizeRouteView` with no `<NotFound>` template.

## 3. Files to Create/Modify

### New Files (6)

| Path | Purpose |
|------|---------|
| `src/Nutrir.Web/wwwroot/css/error-pages.css` | Error page styles ported from prototype |
| `src/Nutrir.Web/Components/Layout/ErrorLayout.razor` | Error page layout shell (brand-only rail + `@Body`) |
| `src/Nutrir.Web/Components/Pages/Error/NotFound.razor` | 404 page (`@page "/error/404"`) |
| `src/Nutrir.Web/Components/Pages/Error/Forbidden.razor` | 403 page (`@page "/error/403"`) |
| `src/Nutrir.Web/Components/Pages/Error/ServerError.razor` | 500 page (`@page "/error/500"`) |
| `src/Nutrir.Web/Components/Pages/Error/ServiceUnavailable.razor` | 503 page (`@page "/error/503"`) |

### Modified Files (3)

| Path | Change |
|------|--------|
| `src/Nutrir.Web/Components/Routes.razor` | Add `<NotFound>` block with ErrorLayout and 404 content |
| `src/Nutrir.Web/Program.cs` | Add `UseStatusCodePagesWithReExecute("/error/{0}")`, change exception handler to `/error/500` |
| `src/Nutrir.Web/Components/App.razor` | Add `error-pages.css` stylesheet link |

### Deleted Files (1)

| Path | Reason |
|------|--------|
| `src/Nutrir.Web/Components/Pages/Error.razor` | Replaced by `/error/500` route |

## 4. Implementation Details

### 4.1 ErrorLayout.razor

Inherits `LayoutComponentBase`. Renders only the icon rail (brand SVG only, no nav items). Wraps `@Body` — each error page renders its own topbar, content area, and status bar. This avoids the need for a shared state service (only 4 pages, minimal duplication).

**Reference files:**
- `Components/Layout/MainLayout.razor` — command-center layout pattern
- `Components/Layout/IconRailSidebar.razor` — brand SVG icon to reuse (layers icon, lines 6-9)

### 4.2 Error Page Components

Each page follows this pattern:
- `@page "/error/{code}"` route
- `@layout ErrorLayout` directive
- Renders: topbar (breadcrumb "Nutrir / {label}"), centered `.error-card`, status bar
- Uses `@inject IJSRuntime` for "Go back" and "Refresh" buttons

**Page-specific details:**

| Page | TopBar Label | Status Dot | Code Class | Actions |
|------|-------------|------------|------------|---------|
| 404 | Page Not Found | dot-ok, "System online" | (default) | Dashboard (primary), Go back (ghost) |
| 403 | Access Denied | dot-ok, "System online" | code-error | Dashboard (primary), Sign in (ghost → `/Account/Login`), Go back (ghost) |
| 500 | Server Error | dot-error, "Error detected" | code-error | Try again (primary, `location.reload()`), Dashboard (ghost) |
| 503 | Maintenance | dot-warning, "Maintenance mode" | code-warning | Refresh (primary, `location.reload()`), maintenance progress bar |

**500 page extra:** Request ID detail box showing `Activity.Current?.Id ?? HttpContext?.TraceIdentifier`.

### 4.3 CSS (error-pages.css)

Port CSS from prototype (lines 134-490), **excluding:**
- `.proto-switcher` styles (demo-only)
- `.btn` / `.btn-primary` / `.btn-ghost` base styles (already in `design-system.css`)
- `:root` token definitions (already in design system and palette files)
- Base reset and body styles (already handled)
- `fadeUp` animation (already in `layout.css` line 9-12)

**Include these class groups:**
- `.error-page` — flex layout, min-height 100vh
- `.error-rail`, `.error-rail-brand` — simplified icon rail (56px width, matches `.icon-rail`)
- `.error-main` — flex column for main area
- `.error-topbar`, `.error-topbar-brand`, `.error-topbar-sep`, `.error-topbar-label` — minimal topbar
- `.error-content` — centered content area
- `.error-card` — max-width 480px card with fadeUp animation
- `.error-code` + `.code-error`, `.code-warning`, `.code-info` — large status code
- `.error-title`, `.error-description` — text
- `.error-divider` + `.divider-error`, `.divider-warning`, `.divider-info` — colored divider
- `.error-actions` — button group
- `.error-detail`, `.error-detail-label`, `.error-detail-value` — request ID block (500)
- `.maintenance-info`, `.maintenance-row`, `.maintenance-bar-track`, `.maintenance-bar-fill` — 503 progress
- `.error-status-bar`, `.error-status-dot`, `.dot-ok`, `.dot-error`, `.dot-warning` — status bar
- Responsive rules (hide rail below 768px, adjust padding/font sizes)
- `prefers-reduced-motion` support
- `.error-actions .btn svg` sizing (14px)

### 4.4 Routes.razor Changes

Add `<NotFound>` block inside the `<Router>` component to handle client-side navigation to unmatched routes. Render 404 content using `ErrorLayout`.

### 4.5 Program.cs Changes

Add after `UseHttpsRedirection()` and before `UseAntiforgery()`:
```csharp
app.UseStatusCodePagesWithReExecute("/error/{0}");
```

Update existing exception handler:
```csharp
app.UseExceptionHandler("/error/500");
```

### 4.6 JS Interop

- "Go back" buttons: `await JSRuntime.InvokeVoidAsync("history.back")`
- "Try again" / "Refresh" buttons: `await JSRuntime.InvokeVoidAsync("location.reload")`
- No custom JS file needed — standard browser APIs callable directly through `IJSRuntime`.

## 5. Execution Order

Single agent, sequential:

1. Create `error-pages.css` (CSS from prototype)
2. Create `ErrorLayout.razor` (layout component)
3. Create the 4 error page components (404, 403, 500, 503)
4. Modify `Routes.razor` (add NotFound block)
5. Modify `Program.cs` (add middleware, change exception handler)
6. Modify `App.razor` (add CSS link)
7. Delete old `Error.razor`

## 6. Acceptance Criteria

1. Navigating to a non-existent route shows the styled 404 page with ErrorLayout
2. Navigating to `/error/404`, `/error/403`, `/error/500`, `/error/503` renders corresponding pages
3. The 500 page displays a request ID when available
4. The 503 page shows the maintenance progress bar
5. "Dashboard" buttons link to `/`
6. "Go back" buttons invoke `history.back()` via JS interop
7. "Try again" / "Refresh" buttons invoke `location.reload()` via JS interop
8. "Sign in" button on 403 links to `/Account/Login`
9. Error pages are visually consistent with the HTML prototype designs
10. Error pages use existing design system tokens — no hardcoded color values
11. `dotnet build Nutrir.sln` compiles without errors
12. Responsive: icon rail hides below 768px, content adjusts padding
13. `prefers-reduced-motion` disables animations

## 7. Design Decisions

**Why a separate ErrorLayout instead of MainLayout?** The prototype shows a deliberately simplified shell: no navigation items in the rail (just the brand), no search bar in the topbar (just a breadcrumb), and a simpler status bar. This prevents users from interacting with a broken app and keeps the error experience focused.

**Why no shared state service?** With only 4 error pages, each page rendering its own topbar and status bar content is simpler and more self-contained than introducing a scoped state service for communication between layout and page.

**Why not use scoped CSS (.razor.css)?** The error page styles are substantial (~150 lines) and shared across 4 pages plus the layout. A standalone CSS file is cleaner than duplicating scoped styles.

**Why `UseStatusCodePagesWithReExecute` over `UseStatusCodePages`?** Re-execute runs the full Blazor pipeline, so Razor components render properly with layout, DI, etc. Simple redirects would cause an extra round-trip and lose request context (like the trace ID for the 500 page).
