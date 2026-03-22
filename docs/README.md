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
| `reports/` | Practice reports, analytics, metrics |
| `temp/` | Drafts and work-in-progress — move to proper folder when finalized |

## Infrastructure Documents

| Document | Description |
|----------|-------------|
| [Architecture Overview](infrastructure/architecture.md) | Three-layer architecture, DI patterns, middleware pipeline, UI structure, design system |
| [Architecture Diagrams](infrastructure/architecture-diagrams.md) | Mermaid diagrams — C4 context/container, domain model ER, middleware pipeline, AI flow, dependency graph |
| [Docker & Deployment](infrastructure/docker-and-deployment.md) | Docker Compose services, Dockerfile, environment variables, local dev setup, production deployment, CI/CD pipeline |
| [Database & EF Core](infrastructure/database.md) | PostgreSQL setup, AppDbContext, migrations, schema, conventions |
| [Logging & Observability](infrastructure/logging.md) | Serilog configuration, Seq, sinks, log levels |
| [Maintenance Mode](infrastructure/maintenance-mode.md) | Admin-toggleable maintenance mode with 503 page, API endpoints, middleware |
| [CLI Tool](infrastructure/cli-tool.md) | Nutrir CLI reference — all commands, options, output format, and examples |
| [AI Assistant](infrastructure/ai-assistant-spec.md) | AI assistant panel — architecture, 38 tools, confirmation flow, streaming, markdown rendering, persistence, rate limiting, usage tracking |
| [Seed Data Generator](infrastructure/seed-data-generator.md) | Dynamic development seed data — Bogus-powered profile-driven generator, configuration, food database, extending |
| [Real-Time Notifications](infrastructure/real-time-notifications.md) | In-process event bus architecture, notification payload, dispatch points, consumer pages, integration pattern |
| [Email Service](infrastructure/email-service.md) | MailKit/Gmail SMTP implementation — architecture, SmtpOptions, DI registration, usage examples, future considerations |
| [Gmail SMTP Setup](infrastructure/gmail-smtp-setup.md) | Practitioner setup guide — App Password, SPF/DKIM/DMARC DNS records, .env configuration, sending limits, DMARC progression |
| [Kibana Dashboard API Learnings](infrastructure/kibana-api-learnings.md) | Practical findings from building dashboards via the Kibana Saved Objects API — critical missing fields, formula limitations, field mapping gotchas, debugging strategy |

## Design System Documents

| Document | Description |
|----------|-------------|
| [Blazor SSR Forms](design-system/blazor-ssr-forms.md) | SSR form patterns, the conditional `<EditForm>` gotcha, and multi-step wizard solution |
| [DataGrid Component](design-system/datagrid.md) | Reusable `DataGrid<TItem>` — server-side pagination, 3-state sorting, responsive hiding, skeleton loading, real-time banner |
| [Data Tables](design-system/data-tables.md) | Table styling conventions — card wrapper, identity cells, badges, row hover, responsive breakpoints |
| [Global Search](design-system/global-search.md) | UX spec for global search — dropdown anatomy, all states, keyboard nav, ARIA, CSS classes, Blazor notes |

## Clients Documents

| Document | Description |
|----------|-------------|
| [Health Profile](clients/health-profile.md) | Client health profile data model — allergies, medications, conditions, dietary restrictions (ERD, enums, design decisions) |
| [Intake Form Design](clients/intake-form-design.md) | Pre-appointment digital intake form — entities, workflow, token strategy, field mapping, edge cases |

## Meal Plans Documents

| Document | Description |
|----------|-------------|
| [PDF Export Layout](meal-plans/pdf-export-layout.md) | PDF export layout spec — page setup, header/content/footer structure, color palette, table columns |

## Compliance Documents

| Document | Description |
|----------|-------------|
| [Privacy Research](compliance/privacy-research.md) | Canadian privacy law analysis — PIPEDA, PHIPA, HIA, provincial frameworks, and technical recommendations |
| [Compliance Requirements](compliance/requirements.md) | Actionable application-level requirements (v1 must-have, v2 should-have) distilled from the legal research |
| [Consent Form](compliance/consent-form.md) | Nine-section PIPEDA-compliant intake consent form — PDF/DOCX generation, digital and physical signing workflows, `ConsentForm` entity |
| [AI Conversation Data Policy](compliance/ai-conversation-data-policy.md) | Retention, audit, and privacy policy for AI assistant conversation data containing PHI |
| [Data Export Spec](compliance/data-export-spec.md) | PIPEDA-compliant per-client data export — JSON envelope, PDF sections, redaction rules, API endpoint, audit event |

## Scheduling Documents

| Document | Description |
|----------|-------------|
| [Calendar View](scheduling/calendar-view.md) | PoC spec — Radzen Blazor Scheduler integration for appointment calendar view |
| [Domain Summary](scheduling/domain-summary.md) | Current state of the scheduling domain — entities, services, gaps |

## Reports Documents

| Document | Description |
|----------|-------------|
| [Practice Reports Spec](reports/practice-reports-spec.md) | Monthly practice reports — metrics, trend data, period options, UI layout |

## Product Documents

| Document | Description |
|----------|-------------|
| [Feature Discovery Roundtable](product/2026-03-09-roundtable-feature-discovery.md) | Simulated stakeholder roundtable — pain points, quick wins, major enhancements, workflow gaps |

## Architecture Decision Records

| ADR | Domain | Title |
|-----|--------|-------|
| [ADR-0001](infrastructure/adr-0001-url-structure.md) | Infrastructure | URL structure & application routing |
| [ADR-0001](scheduling/adr-0001-calendar-component.md) | Scheduling | Calendar component selection |
| [ADR-0002](infrastructure/adr-0002-ai-tool-role-aware-filtering.md) | Infrastructure | Role-aware filtering in AI tool handlers |

## Worklog

| Entry | Description |
|-------|-------------|
| [2026-02-23 Client Table Redesign](worklog/2026-02-23-client-table-redesign.md) | Client list table styling overhaul |
| [2026-02-23 Client Detail Redesign](worklog/2026-02-23-client-detail-redesign.md) | Client detail page redesign |
| [2026-02-23 Appointment Pages Redesign](worklog/2026-02-23-appointment-pages-redesign.md) | Appointment pages styling overhaul |
| [2026-03-07 AI Assistant Appointment Filtering](worklog/2026-03-07-ai-assistant-appointment-filtering.md) | Role-aware appointment filtering for AI assistant (#183, #184) |
| [2026-03-10 Tier 1 Implementation Plan](worklog/2026-03-10-tier1-implementation-plan.md) | Implementation plan for Milestone 12 Tier 1 issues (#224, #233, #234, #232) |

## Conventions

- **Specs** — Name as `feature-name.md` (e.g., `clients/intake-form.md`)
- **Architecture decisions** — Name as `adr-NNNN-title.md` within the relevant domain folder (e.g., `auth/adr-0001-oauth-provider-strategy.md`)
- **Cross-cutting concerns** — Place in the most relevant domain folder, link from others if needed
