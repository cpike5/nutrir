# Client Consent Form

## Overview

The consent form feature generates a legally compliant intake consent document covering both practice consent and PIPEDA privacy obligations for a Canadian nutrition practice. Every new client receives this form as part of onboarding. The form can be delivered digitally (checkbox during client creation) or physically (printed, signed, and scanned back into the system).

The feature satisfies the [v1 Consent Capture requirement](requirements.md#1-consent-capture-at-client-intake) by ensuring a documented consent event exists before any health data is recorded.

---

## Form Content

The consent form is structured into nine sections. All section content is provided by a template implementation and can be extended in v2 to support practitioner-editable text or multi-language variants.

### Section 1 — Nutrition Counseling Services

Describes the nature of the therapeutic relationship: what nutrition counseling involves, what the client can expect from sessions, and the general goals of the engagement. Sets expectations that the service is not a substitute for medical care.

### Section 2 — Scope of Practice

Clarifies the practitioner's regulated scope: dietary assessment, meal plan development, nutritional education, and goal-setting. Explicitly states what is outside scope (medical diagnosis, prescription, clinical treatment) and advises the client to consult a physician for medical concerns.

### Section 3 — Privacy & Data Protection (PIPEDA)

States the lawful basis for collecting personal health information under PIPEDA. Lists the categories of data collected (contact information, health history, dietary records, progress notes, appointment records). Identifies the data controller (the practitioner), the purpose of collection, and that data will not be sold or used for unrelated purposes.

### Section 4 — Third-Party Disclosure

Specifies the circumstances under which information may be shared: only with the client's explicit written consent, or as required by law (e.g. mandatory reporting obligations). Names any system-level third parties involved in hosting (e.g. the self-hosted VPS provider) and confirms Canadian data residency.

### Section 5 — Right to Withdraw Consent

Informs the client that consent is voluntary and may be withdrawn at any time in writing. Notes that withdrawal ends future data collection but does not retroactively erase records that were lawfully created; existing records are subject to the retention policy (see Section 6).

### Section 6 — Data Retention

States that health records are retained for a minimum of 7 years following the last interaction, consistent with professional and provincial standards. Records involving minors are retained until the minor reaches the age of majority plus 7 years.

### Section 7 — Access & Correction Rights

Under PIPEDA, clients have the right to request access to their personal information and to request corrections to inaccurate records. Describes how to submit such a request (in writing to the practitioner) and the expected response timeline (30 days).

### Section 8 — Electronic Records

Discloses that records are maintained in electronic format on a self-hosted, password-protected system located in Canada. Confirms that appropriate technical and organizational safeguards are in place (encrypted connections, access controls, audit logging).

### Section 9 — Signature Block

Captures:

- Client full name (printed)
- Client signature
- Date of signature
- Consent method indicator (digital checkbox or physical signature)
- Optional: practitioner witness signature line (physical forms only)

---

## Architecture

### Template Pattern

Consent form content is decoupled from the rendering/generation layer via a template interface. This allows the default content to be overridden in v2 without changing the PDF or DOCX generation code.

```
Nutrir.Core/
  Interfaces/
    IConsentFormTemplate.cs     — defines section content contract
    IConsentFormService.cs      — orchestration interface
  Models/
    ConsentFormOptions.cs       — configuration POCO
    ConsentForm.cs              — entity (FK to Client)
    ConsentSignatureMethod.cs   — enum

Nutrir.Infrastructure/
  Services/
    ConsentFormService.cs       — orchestrates generation + DB tracking
    DefaultConsentFormTemplate.cs — default section text

Nutrir.Web/
  Controllers/
    ConsentFormController.cs    — API endpoints
  wwwroot/templates/
    consent-form-template.docx  — base DOCX for merge
```

### `IConsentFormTemplate`

```csharp
public interface IConsentFormTemplate
{
    string PracticeName { get; }
    string GetSection(ConsentFormSection section);
    string GetSignatureBlockLabel();
}
```

`DefaultConsentFormTemplate` is registered as the default implementation. `ConsentFormOptions.PracticeName` overrides the practice name property at runtime via configuration.

### PDF Generation — QuestPDF

PDF output uses the [QuestPDF](https://www.questpdf.com/) fluent API. Design choices:

| Element | Value |
|---------|-------|
| Header background | Forest green (`#2D6A4F`) |
| Header text | White, practice name + "Client Consent Form" |
| Body font | Inter (embedded) |
| Section headings | Bold, 11pt, dark grey |
| Body text | Regular, 10pt |
| Page margins | 2.5 cm all sides |
| Footer | Page number + generation timestamp |

Each of the nine sections renders as a heading followed by body text. The signature block renders as a set of labelled underline fields suitable for printing even when the PDF is used for a physical signing workflow.

### DOCX Generation — OpenXml Template Merge

DOCX output is produced by merging data into `wwwroot/templates/consent-form-template.docx` using the OpenXml SDK. The template contains named content controls or placeholder tokens for:

- `{{PracticeName}}`
- `{{ClientName}}`
- `{{GeneratedDate}}`
- `{{PrivacyPolicyVersion}}`

This approach preserves the Word document's native formatting and allows the template file to be updated without code changes.

### `IConsentFormService`

```csharp
public interface IConsentFormService
{
    Task<byte[]> GeneratePdfAsync(Guid clientId, CancellationToken ct = default);
    Task<byte[]> GenerateDocxAsync(Guid clientId, CancellationToken ct = default);
    Task RecordDigitalConsentAsync(Guid clientId, string privacyPolicyVersion, CancellationToken ct = default);
    Task RecordPhysicalConsentAsync(Guid clientId, string privacyPolicyVersion, CancellationToken ct = default);
    Task UploadScannedCopyAsync(Guid clientId, Stream fileStream, string fileName, CancellationToken ct = default);
}
```

`ConsentFormService` implements this interface. It:

1. Resolves the `IConsentFormTemplate` for section content
2. Delegates to the appropriate generator (QuestPDF or OpenXml)
3. Writes or updates the `ConsentForm` entity in the database
4. Writes a `ConsentEvent` audit entry via the audit log service

### Entity: `ConsentForm`

Tracks one consent form instance per client. A new record is created at intake; it is never updated in place (consent events are the immutable record per [Compliance Requirements §1](requirements.md#1-consent-capture-at-client-intake)).

```csharp
public class ConsentForm
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public Client Client { get; set; } = null!;

    public ConsentSignatureMethod SignatureMethod { get; set; }
    public bool ConsentGiven { get; set; }
    public DateTimeOffset? ConsentTimestamp { get; set; }
    public string PrivacyPolicyVersion { get; set; } = string.Empty;

    // Physical signing workflow
    public bool ScannedCopyUploaded { get; set; }
    public string? ScannedCopyPath { get; set; }
    public DateTimeOffset? ScannedCopyUploadedAt { get; set; }

    // Audit
    public DateTimeOffset CreatedAt { get; set; }
}
```

### Enum: `ConsentSignatureMethod`

```csharp
public enum ConsentSignatureMethod
{
    Digital,   // Checkbox during client creation flow
    Physical   // Print-sign-scan workflow
}
```

---

## Configuration

Consent form behaviour is controlled via `ConsentFormOptions`, bound from `appsettings.json` under the `ConsentForm` key.

```json
{
  "ConsentForm": {
    "RequiredOnClientCreation": true,
    "PracticeName": "Nutrir Nutrition Practice",
    "ScannedCopyStoragePath": "uploads/consent-scans"
  }
}
```

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `RequiredOnClientCreation` | `bool` | `true` | If true, the consent step is mandatory during client creation. Setting to false allows skipping for edge cases. |
| `PracticeName` | `string` | `"Nutrir Nutrition Practice"` | Appears in the form header, body text, and signature block. Should match the practitioner's registered practice name. |
| `ScannedCopyStoragePath` | `string` | `"uploads/consent-scans"` | Relative path (under `wwwroot` or a configured file root) where uploaded scanned copies are stored. |

```csharp
public class ConsentFormOptions
{
    public const string SectionKey = "ConsentForm";

    public bool RequiredOnClientCreation { get; set; } = true;
    public string PracticeName { get; set; } = "Nutrir Nutrition Practice";
    public string ScannedCopyStoragePath { get; set; } = "uploads/consent-scans";
}
```

Registration in `Program.cs`:

```csharp
builder.Services.Configure<ConsentFormOptions>(
    builder.Configuration.GetSection(ConsentFormOptions.SectionKey));
```

---

## API Endpoints

All endpoints require an authenticated user with the `Admin`, `Nutritionist`, or `Assistant` role.

### Download PDF

```
GET /api/clients/{clientId}/consent-form/pdf
Authorization: Roles = Admin, Nutritionist, Assistant
```

Generates a PDF for the specified client and returns it as a file download.

**Response:** `200 OK` with `Content-Type: application/pdf` and `Content-Disposition: attachment; filename="consent-form-{clientId}.pdf"`

**Errors:**
- `404 Not Found` — client does not exist
- `403 Forbidden` — caller lacks required role

### Download DOCX

```
GET /api/clients/{clientId}/consent-form/docx
Authorization: Roles = Admin, Nutritionist, Assistant
```

Generates a DOCX from the template and returns it as a file download.

**Response:** `200 OK` with `Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document` and `Content-Disposition: attachment; filename="consent-form-{clientId}.docx"`

**Errors:**
- `404 Not Found` — client does not exist
- `403 Forbidden` — caller lacks required role

### Upload Scanned Copy

```
POST /api/clients/{clientId}/consent-form/scan
Authorization: Roles = Admin, Nutritionist, Assistant
Content-Type: multipart/form-data
```

Accepts a scanned copy of a physically signed consent form. Saves to `ScannedCopyStoragePath` and marks `ConsentForm.ScannedCopyUploaded = true`.

**Request body:** `multipart/form-data` with a single file field named `file`. Accepted types: `image/jpeg`, `image/png`, `application/pdf`. Maximum size: 10 MB.

**Response:** `200 OK`

**Errors:**
- `400 Bad Request` — missing file, unsupported type, or file exceeds size limit
- `404 Not Found` — client does not exist or has no `ConsentForm` record
- `403 Forbidden` — caller lacks required role

---

## Workflows

### Digital Consent Workflow

Used when the client completes intake electronically (the practitioner walks through the form during the appointment, or the client reviews it on-screen).

1. Practitioner initiates client creation in the UI.
2. The client creation wizard includes a consent step: the full form text is displayed, and a checkbox reads "I have reviewed this form with my client and they consent to the above."
3. On submission, `ConsentFormService.RecordDigitalConsentAsync` is called:
   - A `ConsentForm` record is created with `SignatureMethod = Digital`, `ConsentGiven = true`, and `ConsentTimestamp = UtcNow`.
   - A `ConsentGiven` audit event is written.
4. Client creation proceeds. The client record is saved with `ConsentGiven = true`.
5. The practitioner can download the PDF or DOCX at any time for their records.

If `RequiredOnClientCreation` is `true` and the practitioner does not check the consent box, the form submission is blocked with a validation error.

### Physical Consent Workflow

Used when the client signs a paper copy (e.g. at first in-person appointment).

1. Practitioner downloads the DOCX or PDF from the client creation page.
2. The document is printed and reviewed with the client, who signs the physical copy.
3. The client record can be created with `ConsentGiven = false` and `SignatureMethod = Physical` (the consent step in the wizard has an "I will collect a physical signature" option).
4. On the client detail page, a "Mark as Signed" action becomes available when `ConsentGiven = false` and `SignatureMethod = Physical`.
5. Practitioner scans the signed copy and uploads it via the "Upload Scanned Copy" button, which calls `POST /api/clients/{clientId}/consent-form/scan`.
6. On successful upload, `ConsentForm.ConsentGiven` is set to `true`, `ConsentTimestamp` is set to `UtcNow`, and a `ConsentGiven` audit event is written.

The client detail page displays a visible warning banner when a physical consent form has not yet been uploaded.

---

## Security & Authorization

- All API endpoints and UI actions are restricted to authenticated users with `Admin`, `Nutritionist`, or `Assistant` roles.
- Scanned file uploads are stored outside `wwwroot` (or in a non-browsable subdirectory) to prevent direct URL access.
- Uploaded file names are sanitized and replaced with a generated identifier; the original file name is not preserved on disk.
- File type validation uses magic bytes (not only the `Content-Type` header) to prevent MIME-type spoofing.
- The `ConsentForm` entity participates in the audit log: creation and any state change (e.g. scanned copy uploaded) are logged.

---

## v2 Extensibility

The following enhancements are deferred to v2 but the architecture is designed to accommodate them:

| Feature | Approach |
|---------|----------|
| Custom DOCX templates | Allow practitioners to upload their own `.docx` template; store path in `ConsentFormOptions` or per-practitioner settings |
| Multi-language forms | `IConsentFormTemplate` implementations per locale; selected based on client's preferred language |
| Practitioner-editable content | Store section overrides in the database; `DefaultConsentFormTemplate` falls back to hardcoded text when no override exists |
| Client portal self-service | Client receives a link, reviews and signs digitally without practitioner involvement |

---

## Related Documents

- [Compliance Requirements](requirements.md) — v1 consent capture requirement that this feature satisfies
- [Privacy Research](privacy-research.md) — PIPEDA legal analysis underpinning Sections 3–7 of the form

> **Last updated**: 2026-02-25
