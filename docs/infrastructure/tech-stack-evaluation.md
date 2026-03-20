# Tech Stack Evaluation

> **Date**: 2026-03-20
> **Purpose**: Assess which technologies from the full .NET dev stack are appropriate additions to Nutrir.

---

## Current Stack Summary

| Layer | In Use |
|-------|--------|
| **UI** | Blazor Server (interactive SSR), Radzen Blazor, custom CSS design system with palette themes |
| **Charts** | Chart.js (via JS interop) |
| **Auth** | ASP.NET Core Identity, cookie auth, TOTP 2FA |
| **Data** | EF Core 9 + PostgreSQL 17, soft-delete pattern |
| **Docs** | QuestPDF, DocumentFormat.OpenXml |
| **Email** | MailKit + SMTP (Gmail) |
| **Observability** | Serilog → Seq + Elasticsearch, Elastic APM, custom ActivitySource telemetry |
| **Background** | `BackgroundService` (meal plan auto-archive) |
| **Real-time** | SignalR |
| **CLI** | System.CommandLine |
| **AI** | Anthropic SDK (Claude) |
| **Seeding** | Bogus |

---

## Recommendations

### Strongly Recommended — High value, low risk

#### 1. FluentValidation
**Priority: High**

Nutrir currently uses Data Annotations for validation. FluentValidation is a significant upgrade for a health data app:
- Complex cross-field rules (e.g., "end date must be after start date on meal plans", "consent timestamp must precede any health data entry")
- Testable validation logic separated from models
- Better error messages for practitioners
- Conditional validation (e.g., intake fields that depend on health profile type)

**Where**: `Nutrir.Core` or `Nutrir.Infrastructure` — validate commands/DTOs at the service boundary.

#### 2. Health Checks (`AspNetCore.HealthChecks.*`)
**Priority: High**

No health endpoint exists. For a self-hosted app handling PHI on a Canadian VPS, this is important:
- `AspNetCore.HealthChecks.NpgSql` — verify DB connectivity
- `AspNetCore.HealthChecks.UI` — optional dashboard
- Custom checks: SMTP reachability, disk space, Seq ingestion endpoint
- Docker compose `depends_on` can use the `/health` endpoint instead of raw `pg_isready`
- Useful for uptime monitoring on the VPS

**Effort**: ~1-2 hours to wire up basic checks.

#### 3. Testing Stack (xUnit + FluentAssertions + NSubstitute + Testcontainers + Respawn)
**Priority: High**

No test project exists. For an app handling PHI with compliance requirements (7-year audit retention, consent immutability), automated tests are essential:
- **xUnit + FluentAssertions + NSubstitute** — unit tests for services, validators, and domain logic
- **Testcontainers** — spin up a real PostgreSQL container for integration tests (validates EF migrations, audit log immutability, soft-delete behavior)
- **Respawn** — fast database cleanup between integration tests

**Where**: New `Nutrir.Tests.Unit` and `Nutrir.Tests.Integration` projects.

#### 4. Microsoft.FeatureManagement
**Priority: Medium**

Nutrir already has several feature-like toggles in `appsettings.json` (AI rate limits, consent form settings, maintenance mode). `Microsoft.FeatureManagement` formalizes this:
- Feature filters (percentage rollout, time windows)
- `[FeatureGate]` attribute on endpoints/pages
- Useful for safely rolling out new features to the practitioner (e.g., new AI tools, chart types)
- Clean integration with ASP.NET Core middleware

**Effort**: Small — mostly wiring existing config into the feature management pattern.

#### 5. Polly v8 (Resilience)
**Priority: Medium**

External dependencies that would benefit from retry/circuit-breaker policies:
- **Anthropic API** calls (already rate-limited, but no retry on transient failures)
- **SMTP** email sending (Gmail can be flaky)
- **Elasticsearch** sink (network failures shouldn't crash logging)

**Where**: Wrap `HttpClient` registrations with Polly resilience pipelines.

---

### Worth Considering — Moderate value, add when needed

#### 6. HybridCache (.NET 9+)
**Priority: Medium-Low**

Not needed today (single-user practitioner app), but becomes relevant if:
- Client list grows and dashboard queries slow down
- Report generation becomes expensive
- AI response caching is desired

Start with `IMemoryCache` (zero infrastructure), upgrade to `HybridCache` if Redis is ever needed. No Redis container needed for v1.

#### 7. Rate Limiting (built-in ASP.NET Core)
**Priority: Low — Already partially implemented**

Nutrir already has a `"dataExport"` rate limiter policy and AI rate limiting. The built-in middleware is already wired up. Consider extending policies to:
- Login attempts (brute-force protection — relevant for PHI security)
- API endpoints if they're ever exposed

#### 8. Quartz.NET
**Priority: Low**

The current `BackgroundService` for meal plan auto-archiving is fine for a single recurring job. Quartz.NET becomes worthwhile if you add:
- Scheduled report generation
- Data retention cleanup (7-year audit log policy)
- Scheduled email reminders for appointments
- Consent expiry checks

Until you have 3+ scheduled jobs, `BackgroundService` is simpler.

#### 9. HaveIBeenPwned Validator
**Priority: Low**

Nice security addition for the password validation pipeline. Checks passwords against known breaches. Minimal effort to add to the Identity configuration.

---

### Not Recommended for Nutrir

| Technology | Reason |
|------------|--------|
| **MudBlazor / Blazored** | Already invested in Radzen + custom design system. Switching component libraries would be a full rewrite of the UI layer. |
| **Tailwind CSS** | Custom CSS variable system with palette themes is well-established. Tailwind would conflict with the existing approach. |
| **ApexCharts** | Chart.js is working fine with custom JS interop. ApexCharts would be a lateral move. |
| **Avalonia UI** | Desktop app is out of scope. Nutrir is a web-first application. |
| **MassTransit + Kafka** | Massive overkill for a solo-practitioner CRM. No inter-service communication, no event sourcing, no microservices. A message bus adds operational complexity with no benefit. |
| **Redis** | No caching need exists today. Single practitioner, moderate data volume. `IMemoryCache` would suffice if caching is ever needed. |
| **Refit** | No external REST APIs to consume (Anthropic SDK handles its own HTTP). |
| **Scalar** | No REST API to document — Blazor Server app with no public API surface. |
| **FluentEmail** | MailKit is already integrated and working. FluentEmail would be a wrapper around what you already have. |
| **Audit.NET** | Custom audit logging is already implemented and tailored to compliance requirements (append-only, specific entity tracking, source tracking). Audit.NET would require migration and may not fit the custom schema. |
| **DbUp** | EF Core migrations are already the migration strategy. DbUp is an alternative, not a complement. |
| **MSSQL** | PostgreSQL is established. No reason to switch. |
| **Spectre.Console** | CLI tool already uses System.CommandLine. Spectre.Console is complementary for rich output but low priority. |
| **Floating UI / Sortable.js** | No current UI need. Add only when specific drag-and-drop or positioning features are required. |
| **Lucide / Hero Icons / Font Awesome** | Only if the current icon approach is insufficient. Don't add icon libraries preemptively. |
| **OpenTelemetry (standalone)** | Elastic APM already bridges to OpenTelemetry. Adding the full OTel SDK would duplicate existing instrumentation. |

---

## Suggested Implementation Order

1. **Testing stack** — Highest long-term value. Enables confident changes to a PHI-handling app.
2. **Health checks** — Quick win, important for production monitoring.
3. **FluentValidation** — Improves data integrity at service boundaries.
4. **Polly** — Resilience for external calls (Anthropic, SMTP).
5. **Feature management** — Formalize existing toggles.
6. **HaveIBeenPwned** — Quick security improvement.
7. **Caching / Quartz** — Only when complexity warrants it.
