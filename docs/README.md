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

## Architecture Decision Records

| ADR | Domain | Title |
|-----|--------|-------|
| [ADR-0001](infrastructure/adr-0001-url-structure.md) | Infrastructure | URL structure & application routing |

## Conventions

- **Specs** — Name as `feature-name.md` (e.g., `clients/intake-form.md`)
- **Architecture decisions** — Name as `adr-NNNN-title.md` within the relevant domain folder (e.g., `auth/adr-0001-oauth-provider-strategy.md`)
- **Cross-cutting concerns** — Place in the most relevant domain folder, link from others if needed
