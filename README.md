# Elisa MTZ Nutrition

A CRM-style application for a solo nutritionist/dietician, focused on client tracking, scheduling, and meal planning.

## Overview

Built for a freelance nutritionist practicing in Canada. The initial version supports a single practitioner managing clients, with plans to later open client-facing registration via invite codes.

## Core Features (v1)

- **Client Management** — Basic client profiles, contact info, health history, dietary restrictions
- **Scheduling** — Appointment booking and session tracking
- **Meal Plans** — Create and assign meal plans to clients
- **Progress Tracking** — Track client goals and progress over time

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
| **Auth** | ASP.NET Identity + OAuth (Google, Microsoft) |
| **Logging** | Serilog → Seq (dev), Elastic (prod) |
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
- **Elastic** — Production logging and APM (external cluster, not managed by this project)
- **OAuth** — Providers registered dynamically based on configuration; unconfigured providers are skipped

## Localization

English (en-CA) only for v1. Application structure supports future localization (resource files, culture-aware formatting).

## Getting Started

_TODO: Setup instructions once project is scaffolded._
