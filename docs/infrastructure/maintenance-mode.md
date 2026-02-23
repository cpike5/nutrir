# Maintenance Mode

## Overview

Maintenance mode allows the practitioner to temporarily take the application offline for scheduled maintenance. When enabled, non-admin users are redirected to a 503 page with dynamic status information, while admin/nutritionist users can continue using the application normally.

## Architecture

- **`MaintenanceState`** (`Nutrir.Core/Models/`) — POCO holding current state (enabled, timestamps, message)
- **`IMaintenanceService`** (`Nutrir.Core/Interfaces/`) — Interface for getting/setting maintenance state
- **`MaintenanceService`** (`Nutrir.Infrastructure/Services/`) — Thread-safe singleton with in-memory state
- **`MaintenanceModeMiddleware`** (`Nutrir.Web/Middleware/`) — HTTP pipeline middleware that intercepts requests
- **Admin API** — Minimal API endpoints in `Program.cs` for toggling maintenance mode

State is in-memory only — it resets when the application restarts. This is acceptable for v1 since maintenance mode is inherently tied to application uptime.

## API Endpoints

### Get Status (Public)

```
GET /api/admin/maintenance/status
```

Returns current maintenance state. No authentication required (useful for health checks and monitoring).

**Response:**
```json
{
  "isEnabled": false,
  "startedAt": null,
  "estimatedEndAt": null,
  "message": null,
  "enabledBy": null
}
```

### Enable Maintenance Mode (Requires Admin/Nutritionist)

```
POST /api/admin/maintenance/enable
Content-Type: application/json

{
  "message": "Upgrading database schema",
  "estimatedMinutes": 30
}
```

Both fields are optional. If `estimatedMinutes` is provided, the 503 page shows a progress bar and estimated remaining time.

**Response:** Returns the updated `MaintenanceState`.

### Disable Maintenance Mode (Requires Admin/Nutritionist)

```
POST /api/admin/maintenance/disable
```

No request body needed. Clears all maintenance state.

**Response:** Returns the cleared `MaintenanceState`.

## Middleware Behavior

The middleware runs early in the pipeline (after `UseHttpsRedirection`, before `UseAntiforgery`) and applies these rules in order:

1. **Not enabled** — pass through (no overhead)
2. **503 page** (`/error/503`) — allow (prevent redirect loop)
3. **Maintenance API** (`/api/admin/maintenance/*`) — allow (admin needs access)
4. **Static assets** (`_framework`, `_content`, `_blazor`, CSS, JS, fonts, images) — allow
5. **Authenticated Admin/Nutritionist** — allow (admin bypass)
6. **Everyone else** — return 503 with `Retry-After` header and redirect to `/error/503`

## 503 Page

The page at `/error/503` dynamically displays:

- Custom message (if provided) or default maintenance text
- Progress bar with estimated remaining time (if `estimatedMinutes` was set)
- "Maintenance in progress" label (if no estimate was given)
- Refresh button

## Usage Examples

Enable with curl (requires auth cookie):
```bash
curl -X POST https://localhost:7100/api/admin/maintenance/enable \
  -H "Content-Type: application/json" \
  -d '{"message": "Upgrading database", "estimatedMinutes": 30}' \
  --cookie ".AspNetCore.Identity.Application=<cookie>"
```

Disable:
```bash
curl -X POST https://localhost:7100/api/admin/maintenance/disable \
  --cookie ".AspNetCore.Identity.Application=<cookie>"
```

Check status (no auth needed):
```bash
curl https://localhost:7100/api/admin/maintenance/status
```

## Admin UI

The maintenance mode admin page is available at `/admin/maintenance` (requires Admin or Nutritionist role). It provides a visual interface for toggling maintenance mode without needing curl commands.

### Features

- **Status card**: Shows current state (Active/Inactive badge), who enabled it, start time, estimated end time, and custom message
- **Controls card**:
  - When **inactive**: Form with optional message and estimated minutes fields, plus an "Enable Maintenance Mode" button
  - When **active**: Summary text and a "Disable Maintenance Mode" button
- Sidebar navigation link (wrench icon) in the admin section

The page injects `IMaintenanceService` directly (singleton) — no HTTP calls to the API endpoints are needed.

## Limitations

- **In-memory state**: Resets on application restart. Not persisted to database.
- **Single instance**: In a multi-instance deployment, each instance manages its own state independently.
- **No scheduled activation**: Must be toggled manually via API. Scheduling can be added in v2.
