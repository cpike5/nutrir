# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

# Nutrir — Project Instructions

## Project Overview

CRM application for a solo nutritionist/dietician. Client tracking, scheduling, meal plans, and progress tracking.

## Tech Stack

- **Framework:** Blazor Server, .NET 9, C#
- **Database:** PostgreSQL with EF Core
- **Auth:** ASP.NET Identity + Google/Microsoft OAuth
- **CSS:** Custom design system with Tailwind
- **Logging:** Serilog → Seq (dev), Elastic (prod, external)
- **Hosting:** Docker on self-hosted Linux VPS

## Build & Run Commands

```bash
# Build the entire solution
dotnet build Nutrir.sln

# Run the web app (from repo root)
dotnet run --project src/Nutrir.Web

# Run with hot reload
dotnet watch --project src/Nutrir.Web

# Docker (app + PostgreSQL + Seq)
docker compose up -d        # start all services
docker compose down          # stop all services

# EF Core migrations (run from repo root)
dotnet ef migrations add <Name> --project src/Nutrir.Infrastructure --startup-project src/Nutrir.Web
dotnet ef database update --project src/Nutrir.Infrastructure --startup-project src/Nutrir.Web
```

No test projects exist yet.

## Architecture

Three-layer architecture with solution file `Nutrir.sln`:
- **Nutrir.Core** (`src/Nutrir.Core/`) — Domain entities, interfaces, enums. No dependencies on other layers.
- **Nutrir.Infrastructure** (`src/Nutrir.Infrastructure/`) — EF Core `AppDbContext`, repositories, migrations, external service integrations. References Core. Registers itself via `services.AddInfrastructure(configuration)` extension method.
- **Nutrir.Web** (`src/Nutrir.Web/`) — Blazor Server project, UI components, auth configuration, `Program.cs` entry point. References Core and Infrastructure.

### Key Entry Points
- `src/Nutrir.Web/Program.cs` — App startup, DI registration, Serilog config, Identity setup
- `src/Nutrir.Infrastructure/DependencyInjection.cs` — Infrastructure DI registration (`AddInfrastructure`)
- `src/Nutrir.Infrastructure/Data/AppDbContext.cs` — EF Core context (inherits `IdentityDbContext<ApplicationUser>`)

### UI Structure
- `Components/Layout/` — `MainLayout`, `AuthLayout`, `TopBar`, `IconRailSidebar`, `StatusBar` (command-center style layout)
- `Components/UI/` — Reusable design system components: `Button`, `Card`, `Badge`, `Panel`, `FormInput`, `FormSelect`, `FormCheckbox`, `FormGroup`, `Divider`
- `Components/Pages/` — Route-level pages
- `Components/Account/` — ASP.NET Identity scaffolded account pages (login, register, manage, 2FA)

## Key Conventions

### Authentication & OAuth
- Use ASP.NET Identity for user management
- OAuth providers (Google, Microsoft) must be registered **dynamically** — only add a provider if its client ID and secret are configured. Never register a provider without valid credentials or it will cause runtime errors.

### Infrastructure
- Docker Compose includes: app, PostgreSQL, Seq
- Elastic cluster is external and out of scope for Docker setup — assume it exists when configuring production logging
- Seq is the primary dev logging sink; optionally used in prod alongside Elastic
- **Dev ports**: App `7100`, Seq UI `7101`, Seq ingestion `7102`, PostgreSQL `7103`

### DI & Configuration
- Use `IServiceCollection` extension methods for DI registration
- Use Options pattern (`IOptions<T>`) for configuration sections
- Serilog for all logging

### Frontend / Design System
- Custom CSS design system built with Tailwind
- All UI components should be custom-styled Blazor components
- Components require API documentation (parameters, events, usage examples)
- Design system documentation lives alongside components

### Localization
- v1 is English (en-CA) only
- Structure code for future localization: use resource files, avoid hardcoded strings in UI
- Use culture-aware formatting for dates, numbers, currency

### Data & Domain
- v1 scope: Clients, Appointments/Scheduling, Meal Plans, Progress Tracking
- Invoicing/payments are out of scope
- Client registration is practitioner-only for v1 (future: invite-code based)

### Compliance & Privacy

**Must have (v1)** — low effort, painful to retrofit:
- Consent capture at client intake (checkbox + timestamp + policy version)
- Audit log table (append-only — who viewed/edited what record, when)
- Soft-delete pattern on all client data (never hard-delete without explicit workflow)
- MFA on practitioner login (ASP.NET Identity supports this out of the box)
- HTTPS enforced, secure cookies, HSTS headers
- Canadian VPS with data residency in Canada

**Should have (v2)** — higher effort, safely deferrable:
- Application-level field encryption for sensitive health data (AES-256, keys outside DB)
- Per-client data export (JSON/PDF) for access requests
- Retention tracking (last interaction date, flag records approaching retention limit)
- Data purge workflow (hard-delete after retention period, with logging)
- Breach response plan documentation

### Documentation Organization
Docs are **domain-oriented**. Place documents in the domain folder they belong to:

```
docs/
├── clients/          # Client management
├── scheduling/       # Appointments, sessions
├── meal-plans/       # Meal plan specs
├── progress/         # Goal/progress tracking
├── auth/             # Auth architecture, OAuth
├── design-system/    # Component API docs, tokens, guidelines
├── infrastructure/   # Docker, deployment, DB, logging
├── compliance/       # Privacy, regulatory
├── worklog/          # Development session notes
└── temp/             # Drafts — move to proper folder when done
```

- **Feature specs:** `domain/feature-name.md`
- **Architecture decisions:** `domain/adr-NNNN-title.md`
- **Worklog entries:** `worklog/YYYY-MM-DD-short-description.md` — post-session notes capturing changes, design decisions, and lessons learned
- **Cross-cutting docs:** Place in the most relevant domain, link from others
- Do NOT put docs at the `docs/` root — use the domain folders
- Keep `docs/README.md` index updated when adding new documents

### File Operations
- Prefer `cp` over rewriting large files from tokens
- Use `docs/temp/` for drafts; move to the proper domain folder when finalized
