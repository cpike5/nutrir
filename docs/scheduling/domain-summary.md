# Scheduling Domain — Current State Summary

**Last Updated:** 2026-02-26

## Overview

The scheduling domain manages appointment booking, rescheduling, cancellations, and appointment lifecycle tracking for a solo nutrition practitioner in Canada. All functionality is complete for v1 scope.

## Core Entities & Data Model

### Appointment Entity
**File:** `/src/Nutrir.Core/Entities/Appointment.cs`

The `Appointment` entity is the central domain model linking clients to nutritionist sessions.

**Fields:**
- `Id` (int, PK) — unique appointment identifier
- `ClientId` (int, FK) — links to the client
- `NutritionistId` (string) — the practitioner performing the session (user ID)
- `Type` (AppointmentType enum) — InitialConsultation, FollowUp, CheckIn
- `Status` (AppointmentStatus enum) — Scheduled, Confirmed, Completed, NoShow, LateCancellation, Cancelled
- `StartTime` (DateTime, UTC) — session start time
- `DurationMinutes` (int) — session length in minutes
- `EndTime` (computed DateTime) — `StartTime + DurationMinutes` (not stored)
- `Location` (AppointmentLocation enum) — InPerson, Virtual, Phone
- `VirtualMeetingUrl` (string, nullable) — meeting link for virtual/phone sessions
- `LocationNotes` (string, nullable) — e.g., office address, room number
- `Notes` (string, nullable) — general appointment notes
- `CancellationReason` (string, nullable) — why the appointment was cancelled
- `CancelledAt` (DateTime, nullable) — when the appointment was cancelled (UTC)
- **Soft-Delete Tracking:** `IsDeleted`, `DeletedAt`, `DeletedBy` — follow standard compliance pattern

**Audit Timestamps:**
- `CreatedAt` (DateTime, UTC) — initialized to `DateTime.UtcNow`
- `UpdatedAt` (DateTime, nullable, UTC) — set on update

### Enums

**AppointmentType** (`/src/Nutrir.Core/Enums/AppointmentType.cs`)
- `InitialConsultation` — first-time client session
- `FollowUp` — subsequent session with established client
- `CheckIn` — brief touch-base session

**AppointmentStatus** (`/src/Nutrir.Core/Enums/AppointmentStatus.cs`)
- `Scheduled` — appointment booked but not yet confirmed
- `Confirmed` — client/nutritionist has confirmed attendance
- `Completed` — session took place
- `NoShow` — client did not attend
- `LateCancellation` — cancelled within 24 hours of session
- `Cancelled` — cancelled with advance notice

**AppointmentLocation** (`/src/Nutrir.Core/Enums/AppointmentLocation.cs`)
- `InPerson` — office visit
- `Virtual` — video call (Zoom, Google Meet, etc.)
- `Phone` — phone session

## Data Transfer Objects (DTOs)

All DTOs located in `/src/Nutrir.Core/DTOs/`.

**AppointmentDto** — read model returned by all queries
- Flattens client name (FirstName, LastName) and nutritionist display name for UI consumption
- Includes all entity fields plus computed `EndTime`
- Used throughout service and API layers

**CreateAppointmentDto** — input for appointment creation
- `ClientId`, `Type`, `StartTime`, `DurationMinutes`, `Location`
- Optional: `VirtualMeetingUrl`, `LocationNotes`, `Notes`
- Status always defaults to `Scheduled` on creation (set by service layer)

**UpdateAppointmentDto** — input for appointment updates
- All fields above plus explicit `Status` field (allows full updates)
- Optional cancellation reason is handled by separate `UpdateStatusAsync` method

## Service Layer

**IAppointmentService** & **AppointmentService** (`/src/Nutrir.Infrastructure/Services/AppointmentService.cs`)

### Core Methods

- **GetByIdAsync(int id)** — fetch single appointment by ID, returns DTO with client/nutritionist names
- **GetListAsync(fromDate?, toDate?, clientId?, status?)** — pageable query with optional filters
- **CreateAsync(CreateAppointmentDto, userId)** — create appointment, audit log, return DTO
- **UpdateAsync(UpdateAppointmentDto, userId)** — update appointment fields, log audit event
- **UpdateStatusAsync(id, newStatus, userId, cancellationReason?)** — status transition with special handling for Cancelled/LateCancellation (records `CancelledAt` and reason)
- **SoftDeleteAsync(id, userId)** — soft-delete (set `IsDeleted`, `DeletedAt`, `DeletedBy`), audit log

### Convenience Methods

- **GetTodaysAppointmentsAsync(nutritionistId)** — today's appointments for the nutritionist (used in dashboard)
- **GetUpcomingByClientAsync(clientId, count=5)** — next N upcoming appointments for a client (displayed on client detail page)
- **GetWeekCountAsync(nutritionistId)** — count of this week's appointments (dashboard metric)

### Implementation Notes

- All times stored and queried in UTC. No timezone conversion at database layer.
- Soft-delete filtering not currently enforced in queries (appointments are retrieved including deleted ones). **Action Item:** Apply query filters to exclude soft-deleted appointments unless explicitly requested.
- Audit logging integrated: every CRUD operation logs to `AuditLog` via `IAuditLogService`.
- DbContext access is direct (not using `IDbContextFactory`). Works for current v1 scope but may need refactor for Blazor Server concurrency if UI components added in future.

## AI Assistant Integration

Four write tools and two read tools expose appointment functionality to the AI assistant.

**Read Tools:**
- `list_appointments` — query with optional date range, client, status filters
- `get_appointment` — fetch single appointment details

**Write Tools:**
- `create_appointment` — create new appointment with validation
- `update_appointment` — update appointment fields and status
- `cancel_appointment` — cancel appointment with optional reason
- `delete_appointment` — soft-delete appointment

All write tools require user confirmation (Standard tier) before execution. Audit source is tagged as `"ai_assistant"` for compliance tracking.

**Tool Definitions:** `/src/Nutrir.Infrastructure/Services/AiToolExecutor.cs` lines 346–388

## CLI Tool

Comprehensive CLI command suite for appointment management.

**File:** `/src/Nutrir.Cli/Commands/AppointmentCommands.cs`

**Commands:**
- `nutrir appointments list [--client-id N] [--from ISO-8601] [--to ISO-8601] [--status StatusName]` — list appointments
- `nutrir appointments get ID` — fetch appointment details
- `nutrir appointments create --client-id N --type Type --start ISO-8601 --duration Minutes --location Location [--url URL] [--location-notes Notes] [--notes Notes]` — create appointment
- `nutrir appointments cancel ID [--reason "..."]` — cancel appointment
- `nutrir appointments delete ID` — soft-delete appointment

All commands support `--format json|table` and `--connection-string` overrides.

## UI Pages & Components

**Current Status:** Appointment list and create pages exist but are NOT exposed in routing (no Blazor pages registered in main navigation yet).

### Layout & Styling (from 2026-02-23 worklog)

**AppointmentList.razor**
- Card-based table layout with avatar circles (client initials), appointment type/time, status badges
- Row hover effects, staggered fade-in animations
- No pagination UI yet

**AppointmentCreate.razor**
- Four collapsible sections: Client, Schedule, Location, Notes
- Section headers with toggle chevrons (rotate on expand/collapse)
- All sections expanded by default on first load
- Form state persisted in collapsed sections (bound to model)
- CSS: color-mix hover effects, smooth chevron rotation

### Missing / Not Yet Implemented

- **Calendar/Week View** — no visual calendar or timeline display
- **Appointment Edit Page** — no UI to update existing appointments (service layer supports it)
- **Appointment Detail Page** — no single-appointment view
- **Conflict Detection UI** — service layer has no overlap prevention logic yet
- **Page Routing** — appointment pages not wired into main navigation
- **Timezone Display** — times shown in UTC; no conversion to practitioner's local time (Canada)

## Known Issues & Future Work

### High Priority (Scope v1)

1. **Missing Overlap Detection** — `AppointmentService.CreateAsync()` does NOT check for scheduling conflicts. A nutritionist can be double-booked.
   - **Fix:** Add `CheckOverlapAsync()` before persisting in CreateAsync and UpdateAsync
   - **Business rule:** No two appointments for same `NutritionistId` can overlap in UTC time

2. **Soft-Delete Query Filtering** — deleted appointments are returned by `GetListAsync()` unless explicitly calling `IgnoreQueryFilters()`.
   - **Fix:** Apply global query filter in `AppDbContext.OnModelCreating()` to exclude `IsDeleted == false` by default

3. **Timezone Display** — all times shown in UTC. Practitioner is in Canada (EST/EDT/CST/MST/PST depending on province).
   - **Fix:** Store practitioner's timezone preference in `ApplicationUser` profile, convert `StartTime`/`EndTime` to local for display in Blazor pages

4. **No Status Transition Validation** — can move appointments between any status states without business logic validation.
   - **Fix:** Add `ValidateStatusTransition()` method to service. Rules:
     - `Scheduled` → `Confirmed`, `Cancelled`, `LateCancellation`
     - `Confirmed` → `Completed`, `Cancelled`, `LateCancellation`, `NoShow`
     - `Completed`, `Cancelled`, `NoShow`, `LateCancellation` → no further transitions (terminal states)

5. **Client Consent Check Missing** — no validation that client has given consent before creating appointment.
   - **Fix:** Verify `ConsentForm.HasGivenConsent == true` in `CreateAsync()`

### Medium Priority (Scope v2+)

- **Reminders** — send email/SMS reminders 24h and 2h before appointment
- **Calendar Integration** — sync to Google Calendar, Apple Calendar, Outlook
- **Rescheduling Workflow** — client-initiated reschedule with availability selection
- **Availability Slots** — practitioner defines recurring availability windows
- **Waitlist** — queue clients for cancellation slots
- **Notes from Session** — progress tracking entry auto-created from completed appointment

## Database Migrations

**Migration:** `20260221234425_AddAppointments.cs`

Creates `Appointments` table with:
- Clustered PK on `Id`
- FK to `Clients(ClientId)` — cascading delete soft-deletes appointments
- FK to `AspNetUsers(NutritionistId)` for nutritionist lookup
- Indexes on `NutritionistId`, `ClientId`, `StartTime`, `Status` for query performance
- All time columns as `timestamp with time zone` (UTC)

No index on overlap detection (`NutritionistId + StartTime + DurationMinutes`) yet — this should be added if overlap prevention is implemented.

## Documentation & Standards

### Where to Add New Docs

All scheduling documentation goes in `/docs/scheduling/`.

**Expected documents (not yet created):**
- `calendar-view.md` — spec for week/month calendar UI
- `overlap-detection.md` — business rules and algorithm for double-booking prevention
- `adr-0001-timezone-handling.md` — decision record on how to handle timezone conversion
- `status-transitions.md` — state diagram and validation rules for appointment status

### Conventions

- All times in code are UTC (`DateTime.UtcNow`)
- DTOs denormalize client/nutritionist names for UI convenience
- Audit logs capture entity type `"Appointment"` and ID as string

## External Dependencies

- **Clients domain** — appointments depend on existing clients with valid consent
- **Compliance domain** — soft-delete and audit logging patterns follow compliance standards
- **Auth domain** — nutritionist ID must be valid `ApplicationUser`
- **AI Assistant** — appointment tools are integrated into phase 2a write tools

## Queries Used Across the App

From codebase search, appointments are queried/displayed in:

1. **Dashboard (`DashboardService`)** — today's count, week count, recent client appointments
2. **Client Detail Page** — upcoming appointments for a specific client
3. **Search Results** — appointments included in global search
4. **AI Assistant Tools** — list/get/create/update/cancel/delete operations

---

## Summary of Current State

**Complete:**
- Core entity model (Appointment.cs)
- Three enums (Type, Status, Location)
- Full service layer with CRUD and convenience methods
- DTOs for all operations
- CLI tool with all commands
- AI assistant integration (read + write tools)
- UI components (list, create) with modern card-based design

**Missing / Incomplete:**
- Overlap detection (CRITICAL)
- Soft-delete query filtering (CRITICAL)
- Timezone handling (CRITICAL)
- Status transition validation (HIGH)
- Client consent check (HIGH)
- Page routing and main navigation wiring
- Calendar/timeline views
- Edit and detail pages
- Comprehensive documentation

**Next Steps for Implementation:**
1. Create `/docs/scheduling/overlap-detection.md` spec
2. Add `CheckOverlapAsync()` to service layer
3. Add global query filter for soft-delete in DbContext
4. Add timezone utilities and display conversion in Blazor components
5. Create `/docs/scheduling/adr-0001-timezone-handling.md`
6. Wire appointment pages into main navigation
