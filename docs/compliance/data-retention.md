# Data Retention

> **Scope**: Retention tracking, purge workflow, and anonymization strategy for Nutrir. Implements v2 compliance requirements 9 and 10 from [requirements.md](requirements.md).
>
> **Legal basis**: See [privacy-research.md](privacy-research.md) for the full analysis of retention obligations under PIPEDA, PHIPA, HIA, and provincial frameworks.

---

## 1. Overview

Canadian privacy law requires that personal health information be retained only as long as necessary for the purpose it was collected, and securely destroyed once the retention period expires. Failing to destroy records after the retention period is itself a compliance violation.

Nutrir tracks the retention window for every client and provides a controlled workflow for anonymizing records once the retention period has passed. This ensures the practitioner meets their obligation to destroy data that is no longer needed, while maintaining an auditable record of the destruction itself.

---

## 2. Retention Periods

### Default Period

The application defaults to **7 years** from the last interaction with a client. This aligns with PIPEDA's general guidance and the minimum retention period recommended by most provincial colleges of dietitians.

### Provincial Variations

| Framework | Retention Period | Measured From |
|-----------|-----------------|---------------|
| **PIPEDA** (federal) | 7 years (general guidance) | Last contact or service |
| **PHIPA** (Ontario) | 10 years | Last entry in the record |
| **HIA** (Alberta) | 10 years | Last contact with the client |
| **College standards** | Varies (7-10 years) | Check your specific college |

### Configurable Per Client

The `RetentionYears` field on the `Client` entity allows the practitioner to override the default for individual clients. Common reasons for a longer retention period:

- **Minors**: Some provincial requirements extend retention until the client reaches the age of majority plus the standard retention period. For example, a client who was 12 years old at last contact in Ontario would need records retained for at least 10 years past their 18th birthday.
- **Provincial requirements**: A practitioner operating under PHIPA or HIA should set the default to 10 years for all clients, or override per client as needed.
- **Clinical judgment**: The practitioner may choose to retain records longer if there is an ongoing clinical reason.

### Client Entity Fields

The following fields on the `Client` entity support retention tracking:

| Field | Type | Description |
|-------|------|-------------|
| `LastInteractionDate` | `DateTime?` | UTC timestamp of the most recent qualifying interaction |
| `RetentionExpiresAt` | `DateTime?` | Calculated as `LastInteractionDate + RetentionYears` |
| `RetentionYears` | `int` | Default: 7. Configurable per client. |
| `IsPurged` | `bool` | Set to `true` after the purge workflow completes |

---

## 3. How LastInteractionDate Is Maintained

### Qualifying Interactions

`LastInteractionDate` is updated automatically whenever any of the following events occur for a client:

| Event | Trigger |
|-------|---------|
| Appointment created or updated | Any appointment linked to the client |
| Meal plan created or updated | Any meal plan assigned to the client |
| Progress entry or goal created or updated | Any progress record for the client |
| Consent granted or withdrawn | A new `ConsentEvent` is recorded |
| Health profile changed | Allergy, medication, condition, or dietary restriction added/updated/removed |
| Intake form submitted | Client intake form completed |
| Client profile updated | Changes to the client's profile fields |

### Recalculation of RetentionExpiresAt

Whenever `LastInteractionDate` is updated, `RetentionExpiresAt` is recalculated:

```
RetentionExpiresAt = LastInteractionDate + RetentionYears years
```

This means the retention window rolls forward with each interaction. A client who continues to be seen will never reach their retention expiry.

### Backfill for Existing Clients

When retention tracking is first deployed, existing clients will not have a `LastInteractionDate` set. A one-time backfill operation must populate this field by finding the most recent date from:

1. Most recent appointment date
2. Most recent progress entry date
3. Most recent meal plan date
4. Most recent consent event date
5. Client `CreatedAt` date (fallback if no other records exist)

The backfill should be implemented as a migration or startup task, and must write audit log entries documenting the backfill operation.

---

## 4. Retention Dashboard

### Location

`/admin/data-retention` -- accessible only to the practitioner (admin role).

### Views

The dashboard provides three views:

#### Expiring Soon (within 90 days)

- Lists clients whose `RetentionExpiresAt` falls within the next 90 days
- Displays: client name, last interaction date, retention expiry date, retention period (years)
- Sorted by expiry date ascending (soonest first)
- Each row links to the client detail page and provides a "Begin Purge" action

#### Expired

- Lists clients whose `RetentionExpiresAt` is in the past and `IsPurged` is `false`
- Same columns as Expiring Soon, plus the number of days past expiry
- These records require practitioner action -- either purge or extend retention

#### Purge History

- Lists clients where `IsPurged` is `true`
- Displays: anonymized client identifier, purge date, purged by, entity counts
- Data sourced from `DataPurgeAuditLog` (see section 6)

### No Background Job

The dashboard uses dynamic queries against `RetentionExpiresAt` rather than a background job that flags records. This avoids the complexity of a scheduled task and ensures the view is always current. The queries are indexed on `RetentionExpiresAt` for performance.

---

## 5. Purge Workflow

### Prerequisites

- The client's `RetentionExpiresAt` must be in the past. Purging a client with an active retention period is blocked at the application level.
- The client must not already be purged (`IsPurged` must be `false`).
- The practitioner must be authenticated and have admin access.

### Multi-Step Process

#### Step 1: Review

The practitioner selects a client from the Expired list or navigates to a client's detail page. The review screen displays:

- Client name and identifier
- Last interaction date and retention expiry date
- A summary of all data that will be affected:
  - Number of appointments
  - Number of meal plans
  - Number of progress entries/goals
  - Number of health profile items (allergies, medications, conditions, dietary restrictions)
  - Number of consent events
  - Number of related audit log entries

The practitioner reviews this summary before proceeding.

#### Step 2: Typed Confirmation

The practitioner must type the exact text `PURGE {Client Name}` (e.g., `PURGE Jane Smith`) into a confirmation field. This guards against accidental execution. The confirmation is case-sensitive and must match the client's full name exactly.

#### Step 3: Execute

Upon confirmation, the purge operation executes within a single database transaction. If any step fails, the entire operation rolls back.

### What Gets Anonymized vs Preserved

The purge workflow anonymizes data rather than hard-deleting it (see section 7 for rationale).

| Data Category | Action |
|---------------|--------|
| **Client PII** (first name, last name, email, phone, date of birth, notes) | Replaced with anonymized values (e.g., name becomes `[Purged Client {Id}]`, email/phone set to `null`) |
| **Appointment notes/details** | Free-text fields set to `null`; appointment date and type preserved for aggregate statistics |
| **Meal plan content** | Plan content and notes set to `null`; creation date and status preserved |
| **Progress notes and measurements** | Notes set to `null`; dates and numeric measurements preserved for aggregate reporting |
| **Health profile items** (allergies, medications, conditions, dietary restrictions) | Soft-deleted (`IsDeleted = true`) |
| **Consent events** | Notes and details set to `null`; event type, timestamp, and policy version preserved (legal obligation to retain proof that consent was obtained) |
| **Audit log entries** | Never modified. The existing audit trail for the client remains intact. |

### Post-Purge State

- `Client.IsPurged` is set to `true`
- The anonymized client record remains in the database, preserving referential integrity and aggregate statistics
- The client no longer appears in normal application views (filtered by `IsPurged`)
- The purge is irreversible through normal application flows

---

## 6. Audit Trail

### DataPurgeAuditLog Table

A dedicated, append-only table that records every purge operation. This table has the same immutability protection as `AuditLogEntry` -- no `UPDATE` or `DELETE` operations are permitted.

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `int` (PK) | Auto-incrementing identifier |
| `PurgedAt` | `DateTime` | UTC timestamp of the purge operation |
| `PurgedByUserId` | `string` | The user ID of the practitioner who executed the purge |
| `ClientId` | `int` | The client's database ID (preserved even after anonymization) |
| `ClientIdentifier` | `string` | The client's name at the time of purge (stored here since the client record is anonymized) |
| `PurgedEntities` | `string` (JSON) | Structured breakdown of what was purged, e.g., `{"appointments": 12, "mealPlans": 5, "progressEntries": 23, "healthProfileItems": 8, "consentEvents": 2}` |
| `Justification` | `string` | The confirmation text entered by the practitioner (e.g., `PURGE Jane Smith`) |

### Companion AuditLogEntry

In addition to the `DataPurgeAuditLog` record, a standard `AuditLogEntry` is written with:

- `Action`: `ClientDataPurged`
- `EntityType`: `Client`
- `EntityId`: The client's ID
- `Details`: JSON summary matching the `PurgedEntities` content

This ensures the purge event appears in the standard audit log stream alongside all other client events.

### Retention of Purge Records

Purge audit records must be retained for a minimum of **24 months** after the purge date, as required by PIPEDA's breach record-keeping provisions (the same standard is applied to purge records as a best practice). In practice, these records should be retained indefinitely since they are small and serve as the only evidence that data was properly destroyed.

---

## 7. Anonymization vs Hard Delete

### Decision

Nutrir uses **anonymization** (replacing PII with placeholder values and nulling free-text fields) rather than cascading hard deletes.

### Rationale

Both anonymization and hard deletion are permitted under Canadian privacy law for satisfying the destruction obligation. Anonymization was chosen for the following reasons:

1. **Preserves aggregate practice metrics**: Anonymized records still contribute to appointment counts, scheduling patterns, and practice volume statistics. Hard deletion would distort historical reports.

2. **Maintains referential integrity**: Hard-deleting a client record would require cascading deletes across appointments, meal plans, progress entries, consent events, and health profile items. Anonymization avoids the complexity and risk of cascade failures.

3. **Simpler implementation**: A single transaction that nulls/overwrites fields is less error-prone than a multi-table cascade delete that must account for every foreign key relationship.

4. **Irrecoverable through normal flows**: Once PII fields are overwritten and free-text content is nulled, the data cannot be reconstructed through the application. The anonymized record is effectively a statistical placeholder.

5. **Audit trail coherence**: Existing audit log entries reference the client ID. If the client record were hard-deleted, those audit entries would reference a nonexistent entity. Anonymization preserves the link while removing the identifying information.

### What Anonymization Does Not Do

- It does not remove the record from the database. The anonymized client record remains.
- It does not modify audit log entries. The audit trail is immutable.
- It does not satisfy a requirement for physical media destruction (relevant only if data exists outside the database, e.g., paper records).

---

## 8. DataPurgeAuditLog Schema

```csharp
public class DataPurgeAuditLog
{
    public int Id { get; set; }

    public DateTime PurgedAt { get; set; } = DateTime.UtcNow;

    public string PurgedByUserId { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public string ClientIdentifier { get; set; } = string.Empty;

    public string PurgedEntities { get; set; } = string.Empty; // JSON

    public string Justification { get; set; } = string.Empty;
}
```

**EF Core configuration requirements:**

- The table must have no `UPDATE` or `DELETE` operations permitted at the application level (enforced by the service layer, same pattern as `AuditLogEntry`)
- `PurgedAt` should be indexed for efficient querying on the Purge History view
- `ClientId` should be indexed but is not a foreign key constraint (the client record is anonymized, not deleted, so referential integrity is naturally maintained)

---

## Related Documents

- [Compliance Requirements](requirements.md) -- v1/v2 requirements (retention tracking is requirement 9, purge workflow is requirement 10)
- [Privacy Research](privacy-research.md) -- Legal analysis of retention obligations under PIPEDA, PHIPA, HIA
- [Consent Form](consent-form.md) -- Consent events that are preserved (type and timestamp) during purge
- [Data Export Spec](data-export-spec.md) -- Per-client data export (should be offered before purge as a final record)

> **Last updated**: 2026-03-22
