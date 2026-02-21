# Architecture Overview

## Three-Layer Architecture

```
┌─────────────────────────────────────────┐
│  Nutrir.Web                             │
│  src/Nutrir.Web/                        │
│  Blazor Server, UI, Auth, Program.cs    │
│                                         │
│  References: Core, Infrastructure       │
├─────────────────────────────────────────┤
│  Nutrir.Infrastructure                  │
│  src/Nutrir.Infrastructure/             │
│  EF Core, Repositories, Migrations     │
│                                         │
│  References: Core                       │
├─────────────────────────────────────────┤
│  Nutrir.Core                            │
│  src/Nutrir.Core/                       │
│  Domain entities, interfaces, enums     │
│                                         │
│  References: (none)                     │
└─────────────────────────────────────────┘
```

**Dependency rule**: Dependencies flow downward only. Core has no project references. Infrastructure references Core. Web references both Core and Infrastructure.

## Key Entry Points

| File | Purpose |
|------|---------|
| `src/Nutrir.Web/Program.cs` | App startup, DI registration, Serilog config, middleware pipeline |
| `src/Nutrir.Infrastructure/DependencyInjection.cs` | `AddInfrastructure()` extension method — registers EF Core and infrastructure services |
| `src/Nutrir.Infrastructure/Data/AppDbContext.cs` | EF Core context, inherits `IdentityDbContext<ApplicationUser>` |
| `src/Nutrir.Core/Entities/ApplicationUser.cs` | Domain user entity, extends `IdentityUser` |

## DI Registration Pattern

Infrastructure services are registered via an `IServiceCollection` extension method:

```csharp
// In Program.cs
builder.Services.AddInfrastructure(builder.Configuration);
```

```csharp
// In DependencyInjection.cs
public static IServiceCollection AddInfrastructure(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
    return services;
}
```

As the project grows, all infrastructure-level registrations (repositories, external services, etc.) go into `AddInfrastructure()`.

## Middleware Pipeline

The middleware pipeline in `Program.cs` executes in this order:

| Order | Middleware | Purpose |
|-------|-----------|---------|
| 1 | `UseMigrationsEndPoint()` | Dev only — EF Core migration UI |
| 2 | `UseHsts()` | Production only — HTTP Strict Transport Security |
| 3 | `UseExceptionHandler(...)` | Catches unhandled exceptions, redirects to `/error/500` |
| 4 | `UseHttpsRedirection()` | Redirects HTTP → HTTPS |
| 5 | `UseStatusCodePagesWithRedirects(...)` | Maps HTTP status codes to `/error/{code}` pages |
| 6 | `UseAntiforgery()` | CSRF protection for forms |
| 7 | `MapStaticAssets()` | Serves static files (CSS, JS, images) |
| 8 | `MapRazorComponents<App>()` | Blazor component routing with interactive server mode |
| 9 | `MapAdditionalIdentityEndpoints()` | Identity account endpoints (login, register, etc.) |

Dev-only endpoints (`/dev/status/{code}`, `/dev/throw`) are registered conditionally when `IsDevelopment()`.

## UI Structure

### Layouts

Located in `src/Nutrir.Web/Components/Layout/`:

| Layout | Purpose |
|--------|---------|
| `MainLayout.razor` | Primary app shell — sidebar, topbar, status bar, content area |
| `AuthLayout.razor` | Minimal layout for login/register pages |
| `ErrorLayout.razor` | Standalone layout for error pages (no sidebar/nav) |

Supporting components in the same folder:

| Component | Purpose |
|-----------|---------|
| `TopBar.razor` | Top navigation bar |
| `IconRailSidebar.razor` | Narrow icon-based sidebar navigation |
| `StatusBar.razor` | Bottom status bar (command-center style) |

### Routing

- `Components/App.razor` — HTML shell, loads CSS and Blazor script
- `Components/Routes.razor` — Router with `AuthorizeRouteView`, defaults to `MainLayout`; unauthenticated users are redirected to login; 404s use `ErrorLayout`

### Pages

Located in `src/Nutrir.Web/Components/Pages/`:

| Page | Route | Purpose |
|------|-------|---------|
| `Home.razor` | `/` | Dashboard |
| `Auth.razor` | `/auth` | Auth test page |
| `Counter.razor` | `/counter` | Sample counter |
| `Weather.razor` | `/weather` | Sample weather |
| `Admin/DevTools.razor` | `/admin/dev-tools` | Dev tools for testing error pages |
| `Error/NotFound.razor` | `/error/404` | 404 error page |
| `Error/Forbidden.razor` | `/error/403` | 403 error page |
| `Error/ServerError.razor` | `/error/500` | 500 error page |
| `Error/ServiceUnavailable.razor` | `/error/503` | 503 error page |

### Identity Account Pages

Scaffolded ASP.NET Identity pages live in `Components/Account/` — login, register, manage profile, 2FA, external login, etc.

## Design System

### CSS Architecture

CSS files are in `src/Nutrir.Web/wwwroot/css/` and loaded via `App.razor`:

| File | Purpose |
|------|---------|
| `design-system.css` | CSS custom properties (variables), base styles, component classes |
| `layout.css` | Layout-specific styles (sidebar, topbar, status bar, content grid) |
| `error-pages.css` | Styles for error page components |
| `palettes/*.css` | Swappable color palette files |

### Color Palettes

The active palette is set in `App.razor` via a CSS link. Available palettes:

- `palette-pink-mauve.css` (active)
- `palette-pink.css`
- `palette-pink-soft.css`
- `palette-pink-deep.css`
- `palette-pink-lilac.css`
- `palette-sage.css`

To switch palettes, change the `<link>` in `App.razor`.

### Reusable Components

Located in `src/Nutrir.Web/Components/UI/`:

| Component | Purpose |
|-----------|---------|
| `Button.razor` | Styled button with variants |
| `Card.razor` | Content card container |
| `Panel.razor` | Section panel |
| `Badge.razor` | Status/label badge |
| `Divider.razor` | Visual divider |
| `FormInput.razor` | Text input with label |
| `FormSelect.razor` | Dropdown select |
| `FormCheckbox.razor` | Checkbox input |
| `FormGroup.razor` | Form field grouping |

### Typography

Fonts loaded from Google Fonts in `App.razor`:
- **Inter** (400, 500, 600, 700) — body text
- **Outfit** (400, 500, 600, 700) — headings/display

## Configuration Files

| File | Purpose |
|------|---------|
| `Nutrir.sln` | Solution file linking all three projects |
| `src/Nutrir.Web/appsettings.json` | Base config — connection string, Seq URL, allowed hosts |
| `src/Nutrir.Web/appsettings.Development.json` | Dev overrides — log levels |
| `docker-compose.yml` | Docker services (app, db, seq) |
| `.env.example` | Template for environment variables |
| `src/Nutrir.Web/Dockerfile` | Multi-stage Docker build |
