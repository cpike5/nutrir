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
| [Docker & Deployment](infrastructure/docker-and-deployment.md) | Docker Compose services, Dockerfile, environment variables, local dev setup |
| [Database & EF Core](infrastructure/database.md) | PostgreSQL setup, AppDbContext, migrations, schema, conventions |
| [Logging & Observability](infrastructure/logging.md) | Serilog configuration, Seq, sinks, log levels |
| [Maintenance Mode](infrastructure/maintenance-mode.md) | Admin-toggleable maintenance mode with 503 page, API endpoints, middleware |

## Design System Documents

| Document | Description |
|----------|-------------|
| [Blazor SSR Forms](design-system/blazor-ssr-forms.md) | SSR form patterns, the conditional `<EditForm>` gotcha, and multi-step wizard solution |
| [Data Tables](design-system/data-tables.md) | Table styling conventions — card wrapper, identity cells, badges, row hover, responsive breakpoints |

## Compliance Documents

| Document | Description |
|----------|-------------|
| [Privacy Research](compliance/privacy-research.md) | Canadian privacy law analysis — PIPEDA, PHIPA, HIA, provincial frameworks, and technical recommendations |
| [Compliance Requirements](compliance/requirements.md) | Actionable application-level requirements (v1 must-have, v2 should-have) distilled from the legal research |

## Architecture Decision Records

| ADR | Domain | Title |
|-----|--------|-------|
| [ADR-0001](infrastructure/adr-0001-url-structure.md) | Infrastructure | URL structure & application routing |

## Conventions

- **Specs** — Name as `feature-name.md` (e.g., `clients/intake-form.md`)
- **Architecture decisions** — Name as `adr-NNNN-title.md` within the relevant domain folder (e.g., `auth/adr-0001-oauth-provider-strategy.md`)
- **Cross-cutting concerns** — Place in the most relevant domain folder, link from others if needed
