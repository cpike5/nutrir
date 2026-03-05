# Nutrir

**Privacy-compliant practice management for Canadian nutrition professionals.**

Nutrir is a self-hosted CRM built for a solo dietitian/nutritionist practicing in Canada. It handles client management, scheduling, meal planning, and progress tracking — designed with Canadian health-data privacy requirements (PIPEDA, PHIPA, HIA) in mind from day one.

> **Status:** Active development · Single-practitioner v1

<!-- TODO: Add a screenshot of the dashboard here once available -->
<!-- ![Dashboard](docs/screenshots/dashboard.png) -->

## Why This Exists

Most practice management tools for nutritionists are either US-centric SaaS platforms with unclear data residency, or generic CRMs that require manual compliance work. Nutrir is purpose-built for a Canadian practitioner who needs to own their data, meet provincial and federal privacy law, and not pay monthly SaaS fees for features they don't use.

## Core Features (v1)

- **Client Records** — Profiles, health history, dietary restrictions, and consent — all in one place
- **Scheduling** — Book appointments, track sessions, see your calendar at a glance
- **Meal Planning** — Create, customize, and assign meal plans per client
- **Progress Tracking** — Set goals, record measurements, and visualize progress over time
- **Search** — Find any client, appointment, or meal plan quickly
- **Admin Controls** — User management, invite codes, maintenance mode

## Compliance & Privacy

Designed around Canadian health-data privacy requirements from day one:

| Requirement | Implementation |
|-------------|---------------|
| **Consent capture** | Per-client consent records with timestamps |
| **Audit logging** | Data access and modifications logged |
| **MFA enforcement** | TOTP-based multi-factor authentication |
| **Soft-delete** | Client data is never hard-deleted |
| **Transport security** | HTTPS/HSTS enforced |
| **Secure sessions** | HttpOnly, Secure, SameSite cookies |
| **Data residency** | Self-hosted — client data stays in your database |

See [`docs/compliance/requirements.md`](docs/compliance/requirements.md) for the full v1/v2 compliance roadmap.

## Tech Stack

| Layer | Technology | Rationale |
|-------|-----------|-----------|
| **UI** | Blazor Server (.NET 9) | Single codebase, real-time updates, no JS framework overhead |
| **CSS** | Custom design system | Purpose-built tokens and components, no framework bloat |
| **API** | ASP.NET Core | Unified .NET stack, strong Identity/auth integration |
| **Domain** | Four-layer architecture (UI → App → Infrastructure → Core) | Clear separation of concerns, testable domain logic |
| **Data** | EF Core + PostgreSQL | Open-source DB, strong .NET integration, avoids vendor lock-in |
| **Auth** | ASP.NET Identity + OAuth (Google, Microsoft) + TOTP MFA | Compliance requirement: MFA enforcement |
| **Logging** | Serilog → Seq (dev), Elastic APM (prod) | Structured logging with full APM in production |
| **Hosting** | Self-hosted Linux VPS, Docker | Data residency control — no third-party SaaS required for core data storage |

## Architecture

```
┌─────────────────────────────────────┐
│           Blazor Server UI          │
│      (Custom Design System)         │
├─────────────────────────────────────┤
│         Application Layer           │
│      (Services, DTOs, Auth)         │
├─────────────────────────────────────┤
│        Infrastructure Layer         │
│    (EF Core, Repositories, etc.)    │
├─────────────────────────────────────┤
│           Core / Domain             │
│   (Entities, Interfaces, Enums)     │
└─────────────────────────────────────┘
```

## Infrastructure

- **Docker Compose** — App + PostgreSQL + Seq (dev)
- **Elastic APM** — Production logging, tracing, and APM (external cluster, not managed by this project)
- **OAuth** — Providers registered dynamically based on configuration; unconfigured providers are skipped
- **Maintenance Mode** — Admin-toggleable maintenance mode with 503 page and API endpoints

## Localization

English (en-CA) only for v1. Application structure supports future localization (resource files, culture-aware formatting).

## Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://docs.docker.com/get-docker/) and Docker Compose

### Setup

1. **Start dependencies** (PostgreSQL + Seq):

   ```bash
   docker compose up -d db seq
   ```

2. **Apply database migrations**:

   ```bash
   dotnet ef database update -s src/Nutrir.Web -p src/Nutrir.Infrastructure
   ```

3. **Run the application**:

   ```bash
   dotnet run --project src/Nutrir.Web
   ```

4. **Access the app** at `https://localhost:7084` and **Seq logs** at `http://localhost:7101` (see .env.example for default credentials).

### Full Docker Setup

To run everything in Docker (app + PostgreSQL + Seq):

```bash
cp .env.example .env   # edit with your values if needed
docker compose up -d
```

The app will be available at `http://localhost:7100`.

### Port Reference

| Port | Service |
|------|---------|
| `7084` | App (local HTTPS) |
| `7100` | App (Docker) |
| `7101` | Seq UI |
| `7102` | Seq ingestion |
| `7103` | PostgreSQL |

## Project Structure

```
src/
├── Nutrir.Core/           # Domain entities, interfaces, enums, DTOs
├── Nutrir.Infrastructure/  # EF Core, repositories, services
├── Nutrir.Web/            # Blazor Server UI, components, pages, auth
└── Nutrir.Cli/            # CLI tools for admin and maintenance tasks
docs/                       # Domain-organized documentation (see docs/README.md)
```

## Roadmap (Post-v1)

- **Client self-service portal** — Invite-code registration, clients view their own plans and progress
- **Invoicing & payments** — Session billing, receipt generation
- **Multi-language support** — French (fr-CA) and beyond; localization infrastructure already in place

## Disclaimers

Nutrir is a practice management tool for qualified nutrition professionals. It does not provide medical, health, or legal advice. Users are responsible for ensuring their own compliance with applicable privacy legislation. This software is provided under the MIT License with no warranty — see [LICENSE](LICENSE) for details.

## Documentation

See [`docs/README.md`](docs/README.md) for the full documentation index, organized by domain.
