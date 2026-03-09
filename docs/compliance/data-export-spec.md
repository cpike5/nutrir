# Per-Client Data Export — Specification

## Overview

Clients have a legal right to access all personal information and personal health information held about them under PIPEDA Principle 4.9 (Individual Access). This feature satisfies [Compliance Requirement §8 — Per-Client Data Export](requirements.md#8-per-client-data-export) by enabling a practitioner to produce a complete, formatted export of every record associated with a client.

The export is initiated by the practitioner on behalf of the client. No client-facing portal is required in v1. The feature is a v2 requirement but is specified here in full to guide implementation.

---

## Export Formats

| Format | Description | Use |
|--------|-------------|-----|
| `json` | Machine-readable structured document | Technical verification, data portability, system transfer |
| `pdf` | Human-readable formatted report | Providing to the client, printing, archiving |

Both formats carry identical data. Redaction rules apply equally to both. PDF omits internal database IDs for readability; JSON includes them for structural reference.

---

## API Endpoint

```
GET /api/clients/{clientId:int}/export?format=pdf|json
```

### Authorization

- Roles: `Admin`, `Nutritionist` only
- The `Assistant` role does not have access to data exports
- Unauthenticated requests: `401 Unauthorized`
- Insufficient role: `403 Forbidden`

### Query Parameters

| Parameter | Required | Values | Default |
|-----------|----------|--------|---------|
| `format` | No | `pdf`, `json` | `json` |

### Responses

| Status | Condition |
|--------|-----------|
| `200 OK` | Export generated successfully |
| `400 Bad Request` | Unknown `format` value |
| `401 Unauthorized` | Not authenticated |
| `403 Forbidden` | Authenticated but lacks required role |
| `404 Not Found` | No client with the given `clientId` |
| `429 Too Many Requests` | Rate limit exceeded |

### Response Headers (200 OK)

```
Content-Type: application/json            (JSON format)
Content-Type: application/pdf             (PDF format)
Content-Disposition: attachment; filename="client-{clientId}-export-{date}.json"
Content-Disposition: attachment; filename="client-{clientId}-export-{date}.pdf"
Cache-Control: no-store
```

The export is generated in-memory and streamed directly to the response. No file is written to disk.

### Rate Limiting

- 5 export requests per 15-minute window, per authenticated user
- Applies across both formats combined
- Exceeding the limit returns `429 Too Many Requests` with a `Retry-After` header

---

## Data Scope

### Included Entities

All records belonging to the client are included, regardless of soft-delete status. Global query filters must be bypassed using `.IgnoreQueryFilters()` to satisfy PIPEDA's requirement for complete data access.

| Section | Source Entities |
|---------|----------------|
| Client Profile | `Client` |
| Health Profile | `ClientAllergy`, `ClientMedication`, `ClientCondition`, `ClientDietaryRestriction` |
| Appointments | `Appointment` |
| Meal Plans | `MealPlan`, `MealPlanDay`, `MealSlot`, `MealItem` |
| Progress Goals | `ProgressGoal` |
| Progress Entries | `ProgressEntry`, `ProgressMeasurement` |
| Intake Forms | `IntakeForm`, `IntakeFormResponse` |
| Consent History | `ConsentEvent`, `ConsentForm` |
| Audit Log | `AuditLogEntry` (scoped — see below) |

### Audit Log Scoping

The audit log is not filtered by `ClientId` directly because most entries reference sub-entity IDs (appointment IDs, meal plan IDs, etc.). A two-pass query is required:

**Pass 1 — Direct client entries:**
```
WHERE EntityType = 'Client' AND EntityId = '{clientId}'
```

**Pass 2 — Sub-entity entries:**

Collect IDs from all loaded sub-entities:
- Appointment IDs → `WHERE EntityType = 'Appointment' AND EntityId IN (...)`
- MealPlan IDs → `WHERE EntityType = 'MealPlan' AND EntityId IN (...)`
- ProgressGoal IDs → `WHERE EntityType = 'ProgressGoal' AND EntityId IN (...)`
- ProgressEntry IDs → `WHERE EntityType = 'ProgressEntry' AND EntityId IN (...)`
- IntakeForm IDs → `WHERE EntityType = 'IntakeForm' AND EntityId IN (...)`

Results from both passes are merged and sorted by `Timestamp` ascending.

---

## Redaction Rules

Practitioner identity fields are replaced with display names (`FirstName + " " + LastName` resolved via the `ApplicationUser` table). Internal user IDs, email addresses, phone numbers, and IP addresses belonging to the practitioner are never included in the export.

| Field | Action |
|-------|--------|
| `Client.PrimaryNutritionistId` | Replaced by `primaryNutritionistName` display string |
| `Appointment.NutritionistId` | Replaced by `nutritionistName` display string |
| `MealPlan.CreatedByUserId` | Replaced by `createdByName` display string |
| `ProgressGoal.CreatedByUserId` | Replaced by `createdByName` display string |
| `ProgressEntry.CreatedByUserId` | Replaced by `createdByName` display string |
| `ConsentEvent.RecordedByUserId` | Replaced by `recordedByName` display string |
| `ConsentForm.GeneratedByUserId` | Replaced by `generatedByName` display string |
| `ConsentForm.SignedByUserId` | Replaced by `signedByName` display string (null if unsigned) |
| `IntakeForm.ReviewedByUserId` | Replaced by `reviewedByName` display string |
| `AuditLogEntry.UserId` | Replaced by `performedByName` display string |
| `ConsentForm.ScannedCopyPath` | Excluded (internal server file path) |
| `IntakeForm.Token` | Excluded (security token, not personal data) |
| `AuditLogEntry.IpAddress` | Excluded (practitioner's IP address, not the client's data) |
| `Client.DeletedBy`, `*.DeletedBy` | Replaced by display name where available; omitted if null |

Soft-deleted records are included with `isDeleted: true` and a populated `deletedAt` timestamp. The `deletedBy` field is present only as a display name, not a raw user ID.

---

## JSON Format Specification

### Envelope

```json
{
  "exportMetadata": {
    "exportDate": "2026-03-09T14:22:00Z",
    "exportVersion": "1.0",
    "exportFormat": "json",
    "clientId": 42,
    "generatedByName": "Jane Smith",
    "pipedaNotice": "This export contains all personal information and personal health information held about this client, as required under PIPEDA Principle 4.9 (Individual Access)."
  },
  "clientProfile": { ... },
  "healthProfile": { ... },
  "appointments": [ ... ],
  "mealPlans": [ ... ],
  "progressGoals": [ ... ],
  "progressEntries": [ ... ],
  "intakeForms": [ ... ],
  "consentHistory": { ... },
  "auditLog": [ ... ]
}
```

### `clientProfile`

Maps directly from the `Client` entity. `primaryNutritionistName` replaces `PrimaryNutritionistId`.

```json
{
  "id": 42,
  "firstName": "Alice",
  "lastName": "Dupont",
  "email": "alice@example.com",
  "phone": "+1-613-555-0100",
  "dateOfBirth": "1985-04-12",
  "notes": "Referred by Dr. Martin.",
  "consentGiven": true,
  "consentTimestamp": "2026-01-10T09:15:00Z",
  "consentPolicyVersion": "1.0",
  "isDeleted": false,
  "createdAt": "2026-01-10T09:15:00Z",
  "updatedAt": "2026-02-01T11:30:00Z",
  "deletedAt": null,
  "primaryNutritionistName": "Jane Smith"
}
```

### `healthProfile`

Groups the four health sub-entities. All include soft-delete fields to satisfy PIPEDA completeness.

```json
{
  "allergies": [
    {
      "id": 5,
      "name": "Peanut",
      "severity": "Severe",
      "allergyType": "Food",
      "isDeleted": false,
      "deletedAt": null
    }
  ],
  "medications": [
    {
      "id": 3,
      "name": "Metformin",
      "dosage": "500mg",
      "frequency": "Twice daily",
      "prescribedFor": "Type 2 Diabetes",
      "isDeleted": false,
      "deletedAt": null
    }
  ],
  "conditions": [
    {
      "id": 7,
      "name": "Type 2 Diabetes",
      "code": "E11",
      "diagnosisDate": "2018-06-01",
      "status": "Active",
      "notes": "Well-controlled on current medication.",
      "isDeleted": false,
      "deletedAt": null
    }
  ],
  "dietaryRestrictions": [
    {
      "id": 2,
      "restrictionType": "GlutenFree",
      "notes": "Celiac disease confirmed.",
      "isDeleted": false,
      "deletedAt": null
    }
  ]
}
```

### `appointments`

Each entry maps from `Appointment`. `nutritionistName` replaces `NutritionistId`. The computed `EndTime` property is not emitted; `startTime` + `durationMinutes` provides the same information.

```json
[
  {
    "id": 101,
    "type": "Initial",
    "status": "Completed",
    "startTime": "2026-01-15T10:00:00Z",
    "durationMinutes": 60,
    "location": "InPerson",
    "locationNotes": "Main clinic, room 3",
    "notes": "Client presented with fatigue and weight gain.",
    "cancellationReason": null,
    "cancelledAt": null,
    "nutritionistName": "Jane Smith",
    "isDeleted": false,
    "deletedAt": null
  }
]
```

### `mealPlans`

Nested structure: plan → days → slots → items. `createdByName` replaces `CreatedByUserId`.

```json
[
  {
    "id": 12,
    "title": "Anti-inflammatory Phase 1",
    "description": "Four-week elimination and reintroduction protocol.",
    "status": "Active",
    "startDate": "2026-02-01",
    "endDate": "2026-02-28",
    "calorieTarget": 1800,
    "proteinTargetG": 120,
    "carbsTargetG": 200,
    "fatTargetG": 60,
    "notes": null,
    "instructions": "Avoid all processed foods during this phase.",
    "createdByName": "Jane Smith",
    "isDeleted": false,
    "deletedAt": null,
    "days": [
      {
        "id": 50,
        "dayNumber": 1,
        "label": "Monday",
        "notes": null,
        "mealSlots": [
          {
            "id": 200,
            "mealType": "Breakfast",
            "customName": null,
            "sortOrder": 0,
            "notes": null,
            "items": [
              {
                "id": 800,
                "foodName": "Rolled Oats",
                "quantity": 80,
                "unit": "g",
                "caloriesKcal": 302,
                "proteinG": 10.5,
                "carbsG": 54,
                "fatG": 5.1,
                "notes": null
              }
            ]
          }
        ]
      }
    ]
  }
]
```

### `progressGoals`

`createdByName` replaces `CreatedByUserId`.

```json
[
  {
    "id": 9,
    "title": "Reach healthy BMI",
    "description": null,
    "goalType": "WeightLoss",
    "targetValue": 72.0,
    "targetUnit": "kg",
    "targetDate": "2026-06-30",
    "status": "InProgress",
    "createdByName": "Jane Smith",
    "isDeleted": false,
    "deletedAt": null
  }
]
```

### `progressEntries`

Each entry includes its `measurements` inline. `createdByName` replaces `CreatedByUserId`. `CustomMetricName` is included for custom metrics.

```json
[
  {
    "id": 45,
    "entryDate": "2026-02-15",
    "notes": "Energy levels improving.",
    "createdByName": "Jane Smith",
    "isDeleted": false,
    "deletedAt": null,
    "measurements": [
      {
        "id": 90,
        "metricType": "Weight",
        "customMetricName": null,
        "value": 79.5,
        "unit": "kg"
      }
    ]
  }
]
```

### `intakeForms`

`Token` is excluded. `reviewedByName` replaces `ReviewedByUserId`. Responses are included inline.

```json
[
  {
    "id": 3,
    "status": "Reviewed",
    "clientEmail": "alice@example.com",
    "createdAt": "2026-01-08T16:00:00Z",
    "expiresAt": "2026-01-15T16:00:00Z",
    "submittedAt": "2026-01-09T10:22:00Z",
    "reviewedAt": "2026-01-10T09:00:00Z",
    "reviewedByName": "Jane Smith",
    "isDeleted": false,
    "deletedAt": null,
    "responses": [
      {
        "id": 201,
        "sectionKey": "personal_info",
        "fieldKey": "first_name",
        "value": "Alice"
      },
      {
        "id": 202,
        "sectionKey": "medical_history",
        "fieldKey": "allergies",
        "value": "[{\"name\":\"Peanut\",\"severity\":\"Severe\"}]"
      }
    ]
  }
]
```

### `consentHistory`

Contains two arrays: `events` (from `ConsentEvent`) and `forms` (from `ConsentForm`). Both use display name substitution. `ScannedCopyPath` is excluded from `forms`.

```json
{
  "events": [
    {
      "id": 1,
      "eventType": "ConsentGiven",
      "consentPurpose": "Treatment planning and nutritional counseling",
      "policyVersion": "1.0",
      "timestamp": "2026-01-10T09:15:00Z",
      "recordedByName": "Jane Smith",
      "notes": null
    }
  ],
  "forms": [
    {
      "id": 1,
      "formVersion": "1.0",
      "generatedAt": "2026-01-10T09:10:00Z",
      "generatedByName": "Jane Smith",
      "signatureMethod": "Digital",
      "isSigned": true,
      "signedAt": "2026-01-10T09:15:00Z",
      "signedByName": "Jane Smith",
      "notes": null,
      "createdAt": "2026-01-10T09:10:00Z"
    }
  ]
}
```

### `auditLog`

`UserId` is replaced by `performedByName`. `IpAddress` is excluded. Entries are ordered by `Timestamp` ascending.

```json
[
  {
    "id": 1001,
    "timestamp": "2026-01-10T09:15:00Z",
    "performedByName": "Jane Smith",
    "action": "ClientCreated",
    "entityType": "Client",
    "entityId": "42",
    "details": null,
    "source": "Web"
  },
  {
    "id": 1045,
    "timestamp": "2026-03-09T14:22:00Z",
    "performedByName": "Jane Smith",
    "action": "ClientDataExported",
    "entityType": "Client",
    "entityId": "42",
    "details": "format=json",
    "source": "Web"
  }
]
```

---

## PDF Format Specification

The PDF is generated using QuestPDF, consistent with the existing meal plan PDF export pattern. The export service resolves the client and all related data using the same queries as the JSON export, then passes it to a dedicated PDF document class.

### Page Setup

| Property | Value |
|----------|-------|
| Page size | US Letter (8.5 × 11 in) |
| Horizontal margin | 60 pt |
| Vertical margin | 50 pt |
| Base font | System default (QuestPDF) |
| Base font size | 10 pt |

### Color Palette

Consistent with existing PDF exports.

| Token | Hex | Usage |
|-------|-----|-------|
| Primary | `#2d6a4f` | Section headings, borders |
| Text | `#2a2d2b` | Body text |
| Muted | `#636865` | Labels, footer, secondary values |
| Accent | `#e8f5e9` | Section header backgrounds |

### Page Footer (every page)

Every page carries the same footer:

```
Confidential — Generated [export date]          Page N of M
```

Font: 8 pt, muted color. Separated from content by a 1 pt `#e0e0e0` top border.

### Sections (in order)

#### 1. Cover Page

- Practice name and "Client Data Export" title (centered, large)
- Client full name
- Export date (ISO 8601)
- PIPEDA notice block:

> "This document contains all personal information and personal health information held about this client, as required under PIPEDA Principle 4.9 (Individual Access). This record is confidential and intended only for the named client and their authorized practitioner."

- Page break after cover

#### 2. Client Profile

Two-column label/value table:

| Label | Value |
|-------|-------|
| Name | FirstName LastName |
| Email | ... |
| Phone | ... |
| Date of Birth | ... |
| Consent Given | Yes / No |
| Consent Date | ... |
| Policy Version | ... |
| Primary Practitioner | ... |
| Record Created | ... |
| Record Updated | ... |
| Deleted | Yes / No (with date if deleted) |

Notes field rendered as a paragraph below the table if non-empty.

#### 3. Health Profile

Four subsections, each with a bold heading:

- **Allergies** — table: Name | Severity | Type | Deleted
- **Medications** — table: Name | Dosage | Frequency | Prescribed For | Deleted
- **Conditions** — table: Name | Code | Diagnosis Date | Status | Notes | Deleted
- **Dietary Restrictions** — table: Restriction Type | Notes | Deleted

Soft-deleted rows are included. The "Deleted" column shows the date if deleted, or "—" if active.

#### 4. Appointments

Single table across all appointments, sorted by `StartTime` ascending:

| Date & Time | Type | Status | Duration | Location | Practitioner | Notes |
|-------------|------|--------|----------|----------|--------------|-------|

Cancelled appointments include `CancellationReason` in the Notes column.

#### 5. Meal Plans

One subsection per meal plan, separated by a horizontal rule. Each subsection shows:

- Plan title, status, date range, practitioner name
- Macro targets (if set)
- Description, instructions (if non-empty)
- Nested structure: day → slot → item table (columns: Food | Qty | Cal | P | C | F)

Follows the layout conventions defined in [Meal Plan PDF Export Layout](../meal-plans/pdf-export-layout.md).

#### 6. Progress

Two subsections:

**Goals** — table sorted by `CreatedAt`:

| Title | Type | Target | Unit | Target Date | Status | Created By |
|-------|------|--------|------|-------------|--------|------------|

**Entries** — one row per entry, sorted by `EntryDate` ascending. Each entry includes:
- Entry date, created-by name, notes
- Measurements listed inline below the row (Metric Type | Custom Name | Value | Unit)

#### 7. Intake Forms

One subsection per form, sorted by `CreatedAt`. Each subsection shows:

- Form status, submitted date, reviewed date, reviewed-by name
- Response table: Section | Field | Value

#### 8. Consent History

Two subsections:

**Consent Events** — table sorted by `Timestamp`:

| Date | Event Type | Purpose | Policy Version | Recorded By | Notes |
|------|------------|---------|----------------|-------------|-------|

**Consent Forms** — table sorted by `GeneratedAt`:

| Generated | Version | Signature Method | Signed | Signed Date | Generated By | Notes |
|-----------|---------|-----------------|--------|-------------|-------------|-------|

#### 9. Audit Log

Table sorted by `Timestamp` ascending:

| Timestamp | Action | Entity Type | Entity ID | Performed By | Details | Source |
|-----------|--------|-------------|-----------|-------------|---------|--------|

---

## Architecture

### File Layout

```
Nutrir.Core/
  Interfaces/
    IClientDataExportService.cs    — service contract
  DTOs/
    ClientExportDto.cs             — top-level JSON envelope and nested DTOs

Nutrir.Infrastructure/
  Services/
    ClientDataExportService.cs     — data assembly, redaction, JSON serialisation
  PdfDocuments/
    ClientExportPdfDocument.cs     — QuestPDF document class

Nutrir.Web/
  Controllers/
    ClientDataExportController.cs  — GET /api/clients/{clientId}/export
```

### `IClientDataExportService`

```csharp
public interface IClientDataExportService
{
    Task<ClientExportDto> BuildExportAsync(int clientId, string requestingUserId, CancellationToken ct = default);
    Task<byte[]> GenerateJsonAsync(int clientId, string requestingUserId, CancellationToken ct = default);
    Task<byte[]> GeneratePdfAsync(int clientId, string requestingUserId, CancellationToken ct = default);
}
```

`BuildExportAsync` loads all data (with `.IgnoreQueryFilters()`), resolves display names for all redacted user ID fields, and returns the populated `ClientExportDto`. `GenerateJsonAsync` calls `BuildExportAsync` then serialises with `System.Text.Json`. `GeneratePdfAsync` calls `BuildExportAsync` then passes the DTO to `ClientExportPdfDocument`.

### EF Core Query Strategy

Each section is loaded with a separate query to avoid unbounded `Include` chains. The service uses `IDbContextFactory<AppDbContext>` to create a short-lived `DbContext` per export request, consistent with the Blazor Server concurrency pattern described in CLAUDE.md.

```csharp
await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
var client = await db.Clients
    .IgnoreQueryFilters()
    .FirstOrDefaultAsync(c => c.Id == clientId, ct);
```

`.IgnoreQueryFilters()` must be applied to every query that retrieves client-owned entities, including sub-entities such as `ClientAllergy`, `Appointment`, `MealPlan`, `ProgressEntry`, etc.

### Display Name Resolution

Before building the DTO, the service collects all distinct practitioner user IDs referenced in the loaded data and fetches them in a single query:

```csharp
var userIds = new HashSet<string> { client.PrimaryNutritionistId, ... };
var users = await db.Users
    .Where(u => userIds.Contains(u.Id))
    .Select(u => new { u.Id, u.FirstName, u.LastName })
    .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", ct);
```

This dictionary is passed through to all DTO mapping methods. If a user ID cannot be resolved (e.g. the account was deleted), the display name falls back to `"[Unknown]"`.

### JSON Serialisation

- `System.Text.Json` with `JsonSerializerOptions.WriteIndented = true`
- All `DateTime` and `DateOnly` values serialised as ISO 8601 strings
- `null` values included (not omitted) so the receiver can distinguish a missing field from an empty one
- Enums serialised as their string names (e.g. `"Active"`, `"Severe"`) not integer values

---

## Audit Event

Every successful export — regardless of format — writes a `ClientDataExported` audit log entry via `IAuditLogService.LogAsync`:

| Field | Value |
|-------|-------|
| `userId` | ID of the authenticated practitioner |
| `action` | `"ClientDataExported"` |
| `entityType` | `"Client"` |
| `entityId` | `clientId.ToString()` |
| `details` | `"format=json"` or `"format=pdf"` |

The audit entry is written after the export byte array is assembled but before the response is sent. A failure to write the audit entry should not suppress the download; log the audit write failure separately.

---

## Security Considerations

- HTTPS is enforced application-wide. No additional per-endpoint configuration is needed.
- `Cache-Control: no-store` prevents any proxy or browser cache from storing the export.
- `Content-Disposition: attachment` prevents the browser from rendering the file inline.
- The export is generated fully in memory. No file is written to disk at any point during or after generation.
- The `IntakeForm.Token` field is excluded as it is a security credential, not personal data.
- `AuditLogEntry.IpAddress` contains the practitioner's IP, not the client's data; it is excluded.
- `ConsentForm.ScannedCopyPath` is an internal server filesystem path; it is excluded.

---

## Acceptance Criteria

- [ ] `GET /api/clients/{clientId}/export?format=json` returns a JSON file matching the envelope structure
- [ ] `GET /api/clients/{clientId}/export?format=pdf` returns a PDF matching the section order
- [ ] Soft-deleted records are present in both formats (`isDeleted: true`)
- [ ] No raw practitioner user ID appears anywhere in the JSON or PDF output
- [ ] `IntakeForm.Token`, `ConsentForm.ScannedCopyPath`, and `AuditLogEntry.IpAddress` are absent from output
- [ ] A `ClientDataExported` audit entry is written for every successful export
- [ ] Requests by users with the `Assistant` role return `403 Forbidden`
- [ ] More than 5 requests within 15 minutes from the same user return `429 Too Many Requests`
- [ ] `Cache-Control: no-store` is present on all successful responses
- [ ] A `404` is returned for a client ID that does not exist (including soft-deleted clients — PIPEDA access requests apply to deleted records too)

---

## Related Documents

- [Compliance Requirements](requirements.md) — §8 that this spec implements
- [Consent Form](consent-form.md) — `ConsentForm` and `ConsentEvent` entity definitions
- [Intake Form Design](../clients/intake-form-design.md) — `IntakeForm` and `IntakeFormResponse` entity definitions
- [Health Profile](../clients/health-profile.md) — `ClientAllergy`, `ClientMedication`, `ClientCondition`, `ClientDietaryRestriction` entities
- [Meal Plan PDF Export Layout](../meal-plans/pdf-export-layout.md) — QuestPDF layout conventions reused in §5
- [Database & EF Core](../infrastructure/database.md) — Soft-delete global query filters

> **Last updated**: 2026-03-09
