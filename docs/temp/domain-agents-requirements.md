# Requirements: Nutrir Domain Agents

## Problem Statement
Create domain expert agents that serve as the knowledge authorities for each business and cross-cutting domain in Nutrir, providing review input, maintaining documentation, answering domain questions, and guiding implementation.

## Domains (7 total)

### Business Domains
1. **Clients** — Client management, registration, profiles (`docs/clients/`)
2. **Scheduling** — Appointments, calendar, availability (`docs/scheduling/`)
3. **Meal Plans** — Meal plans, days, slots, items (`docs/meal-plans/`)
4. **Progress** — Goals, entries, measurements, charts (`docs/progress/`)

### Cross-Cutting Domains
5. **Auth/Identity** — Authentication, OAuth, user management (`docs/auth/`)
6. **Compliance/Privacy** — Consent, audit logging, data retention, privacy (`docs/compliance/`)
7. **Design System** — UI patterns, component conventions, styling (`docs/design-system/`)

## Responsibilities (all agents)
- **Review & input** on work touching their domain
- **Documentation maintenance** — full ownership of their `docs/domain/` folder
- **Requirements expertise** — answer questions about business logic and edge cases
- **Implementation guidance** — suggest patterns and review for domain correctness

## Access Level
- **Read code + write docs only** — can edit files in their `docs/` domain folder, but code changes go through technical agents
- Cross-cutting agents scoped to their docs folder (no broader codebase awareness)

## Next Steps
1. Create agent `.md` files in `.claude/agents/`
2. Each agent gets a system prompt with domain context, doc ownership rules, and responsibilities
3. Populate with existing domain knowledge from docs and codebase
