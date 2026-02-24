# Nutrir

A compliance-first CRM for a solo nutritionist/dietitian, focused on client tracking, scheduling, meal planning, and progress monitoring.

## Overview

Built for a freelance nutritionist practicing in Canada. The initial version supports a single practitioner managing clients, with plans to later open client-facing registration via invite codes. Designed to meet Canadian privacy requirements (PIPEDA, PHIPA, HIA).

## Core Features (v1)

- **Client Management** — Profiles, contact info, health history, dietary restrictions, consent capture
- **Scheduling** — Appointment booking and session tracking
- **Meal Plans** — Create and assign meal plans to clients
- **Progress Tracking** — Goals, measurements, and progress charting over time
- **Global Search** — Search across clients, appointments, and meal plans
- **User Management** — Admin user administration with invite codes
- **Compliance** — Consent tracking, audit logging, MFA enforcement, HTTPS/HSTS, soft-delete, secure cookies

### Out of Scope (v1)

- Invoicing and payments
- Client self-registration (future: invite-code based)
- Multi-language support (future: built with localization in mind)

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **UI** | Blazor Server (.NET 9) |
| **CSS** | Custom design system + Tailwind |
| **API** | ASP.NET Core |
| **Domain** | Three-layer architecture (Core → Infrastructure → App) |
| **Data** | EF Core + PostgreSQL |
| **Auth** | ASP.NET Identity + OAuth (Google, Microsoft) + TOTP MFA |
| **Logging** | Serilog → Seq (dev), Elastic APM (prod) |
| **Hosting** | Self-hosted Linux VPS, Docker |

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

4. **Access the app** at `https://localhost:7084` and **Seq logs** at `http://localhost:7101` (admin / `SeqDev123!`).

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
└── Nutrir.Web/            # Blazor Server UI, components, pages, auth
docs/                       # Domain-organized documentation (see docs/README.md)
```

## Documentation

See [`docs/README.md`](docs/README.md) for the full documentation index, organized by domain.
