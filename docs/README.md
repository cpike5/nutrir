# Documentation

Organized by domain. Each folder contains specs, designs, and decisions for that area.

## Structure

| Folder | Contents |
|--------|----------|
| `clients/` | Client management — profiles, intake, health history |
| `scheduling/` | Appointments, sessions, calendar |
| `meal-plans/` | Meal plan creation, templates, assignment |
| `progress/` | Goal tracking, measurements, progress notes |
| `auth/` | Authentication, authorization, OAuth setup, roles |
| `design-system/` | Component API docs, design tokens, style guidelines |
| `infrastructure/` | Docker, deployment, database, logging, hosting |
| `compliance/` | Privacy laws, data handling, consent, regulatory |
| `temp/` | Drafts and work-in-progress — move to proper folder when finalized |

## Infrastructure Documents

| Document | Description |
|----------|-------------|
| [Architecture Overview](infrastructure/architecture.md) | Three-layer architecture, DI patterns, middleware pipeline, UI structure, design system |
| [Architecture Diagrams](infrastructure/architecture-diagrams.md) | Mermaid diagrams — C4 context/container, domain model ER, middleware pipeline, AI flow, dependency graph |
| [Docker & Deployment](infrastructure/docker-and-deployment.md) | Docker Compose services, Dockerfile, environment variables, local dev setup |
| [Database & EF Core](infrastructure/database.md) | PostgreSQL setup, AppDbContext, migrations, schema, conventions |
| [Logging & Observability](infrastructure/logging.md) | Serilog configuration, Seq, sinks, log levels |
| [Maintenance Mode](infrastructure/maintenance-mode.md) | Admin-toggleable maintenance mode with 503 page, API endpoints, middleware |
| [CLI Tool](infrastructure/cli-tool.md) | Nutrir CLI reference — all commands, options, output format, and examples |
| [AI Assistant](infrastructure/ai-assistant-spec.md) | AI assistant panel — architecture, 38 tools, confirmation flow, streaming, markdown rendering, persistence, rate limiting, usage tracking |
| [Seed Data Generator](infrastructure/seed-data-generator.md) | Dynamic development seed data — Bogus-powered profile-driven generator, configuration, food database, extending |
| [Real-Time Notifications](infrastructure/real-time-notifications.md) | In-process event bus architecture, notification payload, dispatch points, consumer pages, integration pattern |

## Design System Documents

| Document | Description |
|----------|-------------|
| [Blazor SSR Forms](design-system/blazor-ssr-forms.md) | SSR form patterns, the conditional `<EditForm>` gotcha, and multi-step wizard solution |
| [DataGrid Component](design-system/datagrid.md) | Reusable `DataGrid<TItem>` — server-side pagination, 3-state sorting, responsive hiding, skeleton loading, real-time banner |
| [Data Tables](design-system/data-tables.md) | Table styling conventions — card wrapper, identity cells, badges, row hover, responsive breakpoints |
| [Global Search](design-system/global-search.md) | UX spec for global search — dropdown anatomy, all states, keyboard nav, ARIA, CSS classes, Blazor notes |

## Compliance Documents

| Document | Description |
|----------|-------------|
| [Privacy Research](compliance/privacy-research.md) | Canadian privacy law analysis — PIPEDA, PHIPA, HIA, provincial frameworks, and technical recommendations |
| [Compliance Requirements](compliance/requirements.md) | Actionable application-level requirements (v1 must-have, v2 should-have) distilled from the legal research |
| [Consent Form](compliance/consent-form.md) | Nine-section PIPEDA-compliant intake consent form — PDF/DOCX generation, digital and physical signing workflows, `ConsentForm` entity |
| [AI Conversation Data Policy](compliance/ai-conversation-data-policy.md) | Retention, audit, and privacy policy for AI assistant conversation data containing PHI |

## Scheduling Documents

| Document | Description |
|----------|-------------|
| [Calendar View](scheduling/calendar-view.md) | PoC spec — Radzen Blazor Scheduler integration for appointment calendar view |
| [Domain Summary](scheduling/domain-summary.md) | Current state of the scheduling domain — entities, services, gaps |

## Architecture Decision Records

| ADR | Domain | Title |
|-----|--------|-------|
| [ADR-0001](infrastructure/adr-0001-url-structure.md) | Infrastructure | URL structure & application routing |

## Worklog

| Entry | Description |
|-------|-------------|
| [2026-02-23 Client Table Redesign](worklog/2026-02-23-client-table-redesign.md) | Client list table styling overhaul |
| [2026-02-23 Client Detail Redesign](worklog/2026-02-23-client-detail-redesign.md) | Client detail page redesign |
| [2026-02-23 Appointment Pages Redesign](worklog/2026-02-23-appointment-pages-redesign.md) | Appointment pages styling overhaul |

## Conventions

- **Specs** — Name as `feature-name.md` (e.g., `clients/intake-form.md`)
- **Architecture decisions** — Name as `adr-NNNN-title.md` within the relevant domain folder (e.g., `auth/adr-0001-oauth-provider-strategy.md`)
- **Cross-cutting concerns** — Place in the most relevant domain folder, link from others if needed
