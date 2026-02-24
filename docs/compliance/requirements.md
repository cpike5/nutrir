# Compliance Requirements

> **Scope**: Application-level requirements for Nutrir — a health/nutrition CRM for a solo practitioner in Canada storing client PHI (dietary records, meal plans, health history, progress notes) on a self-hosted Canadian VPS.
>
> **Legal background**: See [privacy-research.md](privacy-research.md) for the full analysis of PIPEDA, PHIPA, HIA, and provincial frameworks. This document covers only "what we need to build."

---

## Tiers

| Tier | Rationale |
|------|-----------|
| **v1 — Must Have** | Low effort to implement now; painful and risky to retrofit after client data exists |
| **v2 — Should Have** | Higher effort; can be deferred but must ship before any significant client volume |

---

## v1 — Must Have

### 1. Consent Capture at Client Intake

Every client record requires a documented consent event before any health data is recorded.

| Field | Details |
|-------|---------|
| `ConsentGiven` | Boolean — must be true before intake proceeds |
| `ConsentTimestamp` | UTC datetime of consent |
| `PrivacyPolicyVersion` | String — version/date of the policy the client agreed to (e.g. `"2026-02"`) |
| `ConsentPurpose` | Enum or string — what the client consented to (e.g. treatment planning) |

**Acceptance criteria:**
- [ ] Intake form includes a consent checkbox linked to the current privacy policy
- [ ] Consent record is written to the database at the moment of intake, never retroactively
- [ ] Practitioner cannot create a client record without a consent event
- [ ] Consent record is immutable after creation (only a withdrawal event can supersede it)
- [ ] Withdrawing consent creates a new `ConsentWithdrawn` event; it does not modify the original record

---

### 2. Audit Log Table

An append-only log of all access and modifications to client health data. Required to demonstrate accountability and detect/investigate breaches.

**Minimum events to capture:**

| Event Category | Examples |
|----------------|---------|
| Record access | Viewed client profile, viewed appointment, viewed meal plan |
| Record modification | Created, updated, or deleted any client-owned entity |
| Authentication | Login, logout, failed login, MFA success/failure |
| Consent | Consent given, consent withdrawn |
| Data export | Any export or download of client data |
| Deletion | Soft-delete or hard-delete of any record |

**Schema requirements:**
- `AuditLogId` (PK)
- `Timestamp` (UTC, indexed)
- `UserId` — who performed the action
- `Action` — categorized event type (enum)
- `EntityType` — which domain entity (e.g. `Client`, `Appointment`, `MealPlan`)
- `EntityId` — the affected record's ID
- `Detail` — optional free-text or JSON payload for context
- No `UPDATE` or `DELETE` permitted on this table — insert only

**Acceptance criteria:**
- [ ] All CRUD operations on client-owned entities write an audit entry
- [ ] Authentication events are logged
- [ ] No code path allows modifying or deleting audit records
- [ ] Audit logs are retained for the full data retention period (minimum 7 years)

---

### 3. Soft-Delete Pattern

Client data is never hard-deleted through normal application workflows. All deletes are logical.

| Field | Details |
|-------|---------|
| `IsDeleted` | Boolean flag on all client-owned entities |
| `DeletedAt` | Nullable UTC datetime |
| `DeletedBy` | Nullable user ID |

**Acceptance criteria:**
- [ ] All entities containing client health data have soft-delete fields
- [ ] EF Core global query filters exclude soft-deleted records from all default queries
- [ ] Hard-delete is only permitted through an explicit data purge workflow (v2)
- [ ] Every soft-delete writes an audit log entry

---

### 4. MFA on Practitioner Login

The practitioner account requires MFA. ASP.NET Identity's built-in TOTP authenticator satisfies this.

**Acceptance criteria:**
- [ ] TOTP-based authenticator app support is enabled (ASP.NET Identity scaffold)
- [ ] The practitioner's account has MFA enforced — login without a second factor is rejected
- [ ] Recovery codes are generated and displayed once at setup
- [ ] MFA events (success, failure, recovery code use) are written to the audit log

---

### 5. HTTPS, Secure Cookies, and HSTS

**Acceptance criteria:**
- [ ] HTTP requests are redirected to HTTPS at the application layer (`app.UseHttpsRedirection()`)
- [ ] HSTS is enabled with a minimum `max-age` of 1 year (`app.UseHsts()`)
- [ ] Authentication cookies are marked `Secure`, `HttpOnly`, and `SameSite=Strict`
- [ ] Anti-forgery tokens are used on all state-changing forms
- [ ] TLS certificate is valid and auto-renewing (Let's Encrypt)

---

### 6. Canadian Data Residency

All client data must be stored and processed in Canada. This satisfies the strictest provincial requirement (Alberta HIA) and is the correct default for all other frameworks.

**Acceptance criteria:**
- [ ] VPS is hosted in a Canadian data centre (confirmed with provider)
- [ ] No third-party services receive PHI (no foreign analytics, no foreign error tracking that includes health data)
- [ ] Backups are stored in Canada
- [ ] Application logs sent to Elastic do not contain PHI fields (health data, names, contact info)

---

## v2 — Should Have

### 7. Application-Level Field Encryption

Sensitive health fields are encrypted at the application layer before being written to the database. Protects against database dump exposure where full-disk encryption alone is insufficient.

**Fields to encrypt (minimum):**
- Health history / medical notes
- Progress notes
- Dietary restriction details
- Any free-text clinical field

**Requirements:**
- AES-256-GCM encryption
- Encryption keys stored outside the database (environment variable or secrets vault, not in source control)
- Key rotation strategy documented
- Encrypted fields are opaque to direct SQL queries — full-text search on these fields is not supported

---

### 8. Per-Client Data Export

Clients have a legal right to access their personal health information. The application must support producing a complete export.

**Requirements:**
- Export includes all data associated with the client: profile, appointments, meal plans, progress records, consent history, audit events for that client
- Available formats: JSON (machine-readable) and PDF (human-readable)
- Export event is written to the audit log
- Practitioner initiates the export on behalf of the client (no client portal in v1)

---

### 9. Retention Tracking

The application tracks when each client's retention period ends and surfaces records that require review.

**Requirements:**
- `LastInteractionDate` field on the `Client` entity — updated automatically when any appointment, note, or record is created
- `RetentionExpiresAt` field — calculated as `LastInteractionDate + retention period` (default 7 years; configurable per client if minors are involved)
- Dashboard indicator or scheduled job flags clients whose retention window expires within 90 days
- Flagged records require practitioner review before any purge action proceeds

---

### 10. Data Purge Workflow

A controlled hard-delete process for records that have passed their retention period.

**Requirements:**
- Only accessible to the practitioner through an explicit, multi-step workflow (not a single-click action)
- Presents a summary of all data that will be permanently deleted before confirmation
- Writes a detailed deletion record to the audit log before data is removed (what was deleted, when, under what authority)
- Irreversible — no recycle bin; the audit log entry is the only post-deletion evidence
- Cannot be triggered while a client's retention period is still active

---

### 11. Breach Response Plan

A documented plan for responding to a data breach, required by all Canadian privacy frameworks.

**Requirements:**
- Written document covering: detection, containment, assessment, notification steps, and timelines
- Identifies which regulators must be notified based on province (see [privacy-research.md](privacy-research.md))
- Specifies the 72-hour window for assessing whether breach notification is required
- Stored in this repository under `docs/compliance/breach-response-plan.md`
- Reviewed and dated annually

---

## Implementation Priority Order

For v1 features, the recommended build order based on dependencies:

1. **Soft-delete** — foundational; must exist before any client data is written
2. **Audit log table** — foundational; must exist before intake or auth flows are built
3. **Consent capture** — required as part of the client intake flow
4. **HTTPS / secure cookies** — infrastructure; configure before any deployment
5. **Canadian data residency** — infrastructure; confirm provider and log config
6. **MFA** — auth feature; can follow initial login scaffolding

---

## Related Documents

- [Privacy Research](privacy-research.md) — Legal analysis of PIPEDA, PHIPA, HIA, and provincial frameworks
- [Database & EF Core](../infrastructure/database.md) — Schema conventions, EF Core setup
- [Architecture Overview](../infrastructure/architecture.md) — Layer responsibilities

> **Last updated**: 2026-02-24
