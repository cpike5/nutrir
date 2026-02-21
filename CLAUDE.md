# Elisa MTZ Nutrition — Project Instructions

## Project Overview

CRM application for a solo nutritionist/dietician. Client tracking, scheduling, meal plans, and progress tracking.

## Tech Stack

- **Framework:** Blazor Server, .NET 9, C#
- **Database:** PostgreSQL with EF Core
- **Auth:** ASP.NET Identity + Google/Microsoft OAuth
- **CSS:** Custom design system with Tailwind
- **Logging:** Serilog → Seq (dev), Elastic (prod, external)
- **Hosting:** Docker on self-hosted Linux VPS

## Architecture

Three-layer architecture:
- **Core** — Domain entities, interfaces, enums. No dependencies on other layers.
- **Infrastructure** — EF Core DbContext, repositories, external service integrations. References Core.
- **Application (UI/API)** — Blazor Server project, services, DTOs, auth configuration. References Core and Infrastructure.

## Key Conventions

### Authentication & OAuth
- Use ASP.NET Identity for user management
- OAuth providers (Google, Microsoft) must be registered **dynamically** — only add a provider if its client ID and secret are configured. Never register a provider without valid credentials or it will cause runtime errors.

### Infrastructure
- Docker Compose includes: app, PostgreSQL, Seq
- Elastic cluster is external and out of scope for Docker setup — assume it exists when configuring production logging
- Seq is the primary dev logging sink; optionally used in prod alongside Elastic

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
└── temp/             # Drafts — move to proper folder when done
```

- **Feature specs:** `domain/feature-name.md`
- **Architecture decisions:** `domain/adr-NNNN-title.md`
- **Cross-cutting docs:** Place in the most relevant domain, link from others
- Do NOT put docs at the `docs/` root — use the domain folders
- Keep `docs/README.md` index updated when adding new documents

### File Operations
- Prefer `cp` over rewriting large files from tokens
- Use `docs/temp/` for drafts; move to the proper domain folder when finalized
