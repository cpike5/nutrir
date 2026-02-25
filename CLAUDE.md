# CLAUDE.md

## Environment Specifics

Use the docker compose environment for checking logs and accessing the development version of the site for debugging and testing purposes

## Build Commands

```bash
# EF Core migrations (run from repo root)
dotnet ef migrations add <Name> --project src/Nutrir.Infrastructure --startup-project src/Nutrir.Web
dotnet ef database update --project src/Nutrir.Infrastructure --startup-project src/Nutrir.Web
```

No test projects exist yet.

## Key Conventions

### Authentication & OAuth
- OAuth providers (Google, Microsoft) must be registered **dynamically** — only add a provider if its client ID and secret are configured. Never register a provider without valid credentials or it will cause runtime errors.

### Infrastructure
- Elastic cluster is external and out of scope for Docker setup — assume it exists when configuring production logging
- **Dev ports**: App `7100`, Seq UI `7101`, Seq ingestion `7102`, PostgreSQL `7103`

### Localization
- v1 is English (en-CA) only
- Structure code for future localization: use resource files, avoid hardcoded strings in UI
- Use culture-aware formatting for dates, numbers, currency

### Data & Domain
- v1 scope: Clients, Appointments/Scheduling, Meal Plans, Progress Tracking
- Invoicing/payments are out of scope
- Client registration is practitioner-only for v1 (future: invite-code based)

### Compliance & Privacy
See [`docs/compliance/requirements.md`](docs/compliance/requirements.md) for v1/v2 requirements (consent, audit logging, soft-delete, MFA, encryption, data export, retention).

### Documentation Organization
Docs are **domain-oriented** under `docs/`. Place documents in domain subfolders (`clients/`, `scheduling/`, `meal-plans/`, `progress/`, `auth/`, `design-system/`, `infrastructure/`, `compliance/`, `worklog/`, `temp/`).

- **Feature specs:** `domain/feature-name.md`
- **Architecture decisions:** `domain/adr-NNNN-title.md`
- **Worklog entries:** `worklog/YYYY-MM-DD-short-description.md`
- Do NOT put docs at the `docs/` root — use domain folders
- Keep `docs/README.md` index updated when adding new documents
