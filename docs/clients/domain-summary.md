# Clients Domain — Current State Summary

**Last Updated:** 2026-03-22

## Overview

The clients domain manages patient profiles, health history, consent, and intake forms for a solo nutrition practitioner in Canada. It is the central domain in Nutrir, serving as the foundation for appointments, meal plans, progress tracking, and compliance obligations.

## Core Entities & Data Model

### Client Entity

**File:** `/src/Nutrir.Core/Entities/Client.cs`

The `Client` entity represents a patient profile with personal information, consent status, and data retention tracking.

**Fields:**

- `Id` (int, PK) — unique client identifier
- `FirstName` (string) — client first name
- `LastName` (string) — client last name
- `Email` (string, nullable) — client email address
- `Phone` (string, nullable) — client phone number
- `DateOfBirth` (DateOnly, nullable) — client date of birth
- `PrimaryNutritionistId` (string) — foreign key to `ApplicationUser.Id` (nutritionist assigning the plan)
- `ConsentGiven` (bool) — whether client has given formal consent
- `ConsentTimestamp` (DateTime, nullable, UTC) — when consent was granted
- `ConsentPolicyVersion` (string, nullable) — version of privacy policy accepted at consent time
- `Notes` (string, nullable) — practitioner notes about the client
- `EmailRemindersEnabled` (bool) — whether client receives email appointment reminders

**Data Retention Tracking:**

- `LastInteractionDate` (DateTime, nullable) — date of last recorded activity (appointment, meal plan, progress entry, etc.)
- `RetentionExpiresAt` (DateTime, nullable) — when client data will be purged (calculated as `LastInteractionDate + RetentionYears`)
- `RetentionYears` (int, default 7) — Canadian default retention period (PIPEDA compliance)
- `IsPurged` (bool) — whether data has been purged (soft-deleted with anonymized PII)

**Soft-Delete Tracking:**

- `IsDeleted` (bool) — soft-delete flag
- `DeletedAt` (DateTime, nullable, UTC) — when deleted
- `DeletedBy` (string, nullable) — user ID of who deleted

**Audit Timestamps:**

- `CreatedAt` (DateTime, UTC) — when client record was created
- `UpdatedAt` (DateTime, nullable, UTC) — last modification time

**Navigation Properties:**

- `ConsentEvents` (List<ConsentEvent>) — history of consent grants/revocations for audit trail
- `Allergies` (List<ClientAllergy>) — food/drug/environmental allergies
- `Medications` (List<ClientMedication>) — current medications with dosage/frequency
- `Conditions` (List<ClientCondition>) — medical conditions and their status
- `DietaryRestrictions` (List<ClientDietaryRestriction>) — dietary restrictions (vegan, gluten-free, etc.)

### Health Profile Child Entities

#### ClientAllergy

**File:** `/src/Nutrir.Core/Entities/ClientAllergy.cs`

Records a single allergy with severity classification.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK) — parent client
- `Name` (string) — allergy name (e.g., "Peanuts", "Penicillin")
- `Severity` (AllergySeverity enum) — Mild, Moderate, or Severe
- `AllergyType` (AllergyType enum) — Food, Drug, Environmental, or Other
- **Soft-delete:** IsDeleted, DeletedAt, DeletedBy
- **Timestamps:** CreatedAt, UpdatedAt

#### ClientMedication

**File:** `/src/Nutrir.Core/Entities/ClientMedication.cs`

Records a current medication with usage details.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK) — parent client
- `Name` (string) — medication name
- `Dosage` (string, nullable) — dosage (e.g., "500mg")
- `Frequency` (string, nullable) — frequency (e.g., "Once daily")
- `PrescribedFor` (string, nullable) — indication (e.g., "Type 2 Diabetes")
- **Soft-delete:** IsDeleted, DeletedAt, DeletedBy
- **Timestamps:** CreatedAt, UpdatedAt

#### ClientCondition

**File:** `/src/Nutrir.Core/Entities/ClientCondition.cs`

Records a diagnosed medical condition with status tracking.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK) — parent client
- `Name` (string) — condition name (e.g., "Type 2 Diabetes")
- `Code` (string, nullable) — ICD-10 or other clinical code
- `DiagnosisDate` (DateOnly, nullable) — when diagnosed
- `Status` (ConditionStatus enum) — Active, Managed, or Resolved
- `Notes` (string, nullable) — additional clinical notes
- **Soft-delete:** IsDeleted, DeletedAt, DeletedBy
- **Timestamps:** CreatedAt, UpdatedAt

#### ClientDietaryRestriction

**File:** `/src/Nutrir.Core/Entities/ClientDietaryRestriction.cs`

Records dietary or religious restrictions.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK) — parent client
- `RestrictionType` (DietaryRestrictionType enum) — Vegetarian, Vegan, GlutenFree, DairyFree, Kosher, Halal, LowSodium, Ketogenic, NutFree, Other
- `Notes` (string, nullable) — additional context
- **Soft-delete:** IsDeleted, DeletedAt, DeletedBy
- **Timestamps:** CreatedAt, UpdatedAt

### Consent & Audit Entities

#### ConsentEvent

**File:** `/src/Nutrir.Core/Entities/ConsentEvent.cs`

Records each consent grant or revocation for compliance audit trail.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK) — client whose consent changed
- `EventType` (ConsentEventType enum) — ConsentGranted or ConsentRevoked
- `ConsentPurpose` (string) — why consent was required (e.g., "Treatment and care")
- `PolicyVersion` (string) — version of privacy policy at time of consent
- `Timestamp` (DateTime, UTC) — when event occurred
- `RecordedByUserId` (string) — who recorded the event
- `Notes` (string, nullable) — additional context

#### ConsentForm

**File:** `/src/Nutrir.Core/Entities/ConsentForm.cs`

Tracks digital/physical consent form generation and signing.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK)
- `FormVersion` (string) — form template version
- `GeneratedAt` (DateTime, UTC)
- `GeneratedByUserId` (string) — practitioner who created form
- `SignatureMethod` (ConsentSignatureMethod enum) — Digital or Physical
- `IsSigned` (bool)
- `SignedAt` (DateTime, nullable, UTC)
- `SignedByUserId` (string, nullable) — who signed (may differ from client if authorized representative)
- `ScannedCopyPath` (string, nullable) — path to scanned physical form
- `Notes` (string, nullable)
- `CreatedAt` (DateTime, UTC)

### Intake Form Entities

#### IntakeForm

**File:** `/src/Nutrir.Core/Entities/IntakeForm.cs`

Pre-appointment digital intake form with completion tracking.

**Fields:**

- `Id` (int, PK)
- `ClientId` (int, FK)
- `GeneratedByUserId` (string) — practitioner who created the form
- `Status` (IntakeFormStatus enum) — NotStarted, InProgress, Completed
- `AccessToken` (string, indexed) — secure URL token for client access
- `AccessTokenExpiry` (DateTime, UTC) — when token expires (24-48 hours typical)
- `CompletedAt` (DateTime, nullable, UTC)
- `IsDeleted` (bool)
- `CreatedAt` (DateTime, UTC)
- `UpdatedAt` (DateTime, nullable, UTC)

**Navigation:**

- `IntakeFormResponses` (List<IntakeFormResponse>) — answers to each form question

#### IntakeFormResponse

**File:** `/src/Nutrir.Core/Entities/IntakeFormResponse.cs`

A single question-answer pair in an intake form.

**Fields:**

- `Id` (int, PK)
- `IntakeFormId` (int, FK)
- `QuestionKey` (string) — identifier for the form field (e.g., "height", "weight", "allergies")
- `QuestionLabel` (string) — display text
- `ResponseValue` (string, nullable) — client's answer (JSON-serialized if complex)
- `ResponseType` (string) — data type (text, number, date, multiselect, etc.)

## Data Transfer Objects (DTOs)

All DTOs located in `/src/Nutrir.Core/DTOs/`.

### ClientDto

Read model returned by all queries.

```csharp
record ClientDto(
    int Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth,
    string PrimaryNutritionistId,
    string? PrimaryNutritionistName,
    bool ConsentGiven,
    DateTime? ConsentTimestamp,
    string? ConsentPolicyVersion,
    string? Notes,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt,
    DateTime? LastAppointmentDate = null,
    bool EmailRemindersEnabled = false);
```

- Flattens nutritionist name from `ApplicationUser` for UI convenience
- Includes last appointment date computed from appointments table
- Used in client list and detail pages

### ClientHealthProfileDto

**File:** `/src/Nutrir.Core/DTOs/ClientHealthProfileDto.cs`

Comprehensive health snapshot for display on client detail page.

Contains:

- Client basic info (FirstName, LastName, DateOfBirth, etc.)
- All allergies with severity and type
- All medications with dosage/frequency
- All conditions with status
- All dietary restrictions

### IntakeFormDto

Input for intake form creation and response capture.

## Service Layer

### IClientService & ClientService

**File:** `/src/Nutrir.Infrastructure/Services/ClientService.cs`

#### Core Methods

- **CreateAsync(ClientDto, userId)** — create new client record with consent verification, logs audit event, returns DTO
- **GetByIdAsync(id)** — fetch single client by ID with nutritionist name resolved
- **GetListAsync(searchTerm?)** — pageable query with optional multi-term search (first/last name, email), ordered by last name
- **GetPagedAsync(ClientListQuery)** — paginated query with sorting and filtering
- **UpdateAsync(int, ClientDto, userId)** — update client fields, audit log
- **SoftDeleteAsync(int, userId)** — soft-delete with IsDeleted=true, audit log
- **RestoreAsync(int, userId)** — restore soft-deleted client

#### Health Profile Methods

- **AddAllergyAsync(int clientId, ClientAllergy, userId)** — add allergy to client
- **UpdateAllergyAsync(int allergyId, ClientAllergy, userId)** — update allergy severity/type
- **RemoveAllergyAsync(int allergyId, userId)** — soft-delete allergy
- Similar methods for medications, conditions, dietary restrictions

#### Consent Methods

- Delegated to `IConsentService` — see below

#### Implementation Notes

- Direct `AppDbContext` access for single-entity operations
- `IDbContextFactory<AppDbContext>` for paged queries (Blazor Server concurrency)
- Audit logging on every CRUD operation
- Real-time dispatch to `INotificationDispatcher` for UI updates
- Consent verification enforced on creation: `ConsentGiven` must be true
- Last appointment date is computed from `Appointments` table in `GetListAsync()`

### IConsentService & ConsentService

**File:** `/src/Nutrir.Infrastructure/Services/ConsentService.cs`

Manages consent events and verification.

#### Core Methods

- **GrantConsentAsync(clientId, purpose, policyVersion, userId)** — record consent grant, update `Client.ConsentGiven/ConsentTimestamp/ConsentPolicyVersion`, create `ConsentEvent`
- **RevokeConsentAsync(clientId, reason, userId)** — revoke consent, flag client, create `ConsentEvent`
- **HasConsentAsync(clientId)** — check if client has given consent (before creating appointments, meal plans, etc.)
- **GetConsentAuditAsync(clientId)** — retrieve all `ConsentEvent` records for client

#### Implementation Notes

- All consent changes logged to audit trail
- `ConsentEvent` records are immutable (never deleted) for compliance
- Revoked clients cannot have new appointments or meal plans created

### IClientHealthProfileService & ClientHealthProfileService

**File:** `/src/Nutrir.Infrastructure/Services/ClientHealthProfileService.cs`

Aggregates health profile data for display.

#### Core Methods

- **GetProfileAsync(clientId)** — fetch complete health profile (allergies, medications, conditions, restrictions)
- **UpdateHealthProfileAsync(clientId, profileDto, userId)** — bulk update all health profile fields
- **SyncFromIntakeFormAsync(intakeFormId, clientId, userId)** — populate health profile from intake form responses

## UI Pages & Components

**Current Status:** Client list and detail pages exist and are fully routed.

### ClientList.razor

**Path:** `src/Nutrir.Web/Components/Pages/Clients/ClientList.razor`

- Card-based table layout with avatar circles (initials)
- Search bar with multi-term support (first name, last name, email)
- Columns: Name, Email, Phone, Last Appointment, Actions
- Real-time updates via `INotificationDispatcher` when clients are modified
- Pagination via `DataGrid<ClientDto>` component
- Row hover effects and staggered fade-in animations

### ClientDetail.razor

**Path:** `src/Nutrir.Web/Components/Pages/Clients/ClientDetail.razor`

Multiple collapsible sections:

1. **Client Info** — name, email, phone, date of birth, notes
2. **Consent** — consent status, timestamp, policy version; history link
3. **Health Profile** — allergies (severity/type), medications (dosage/frequency), conditions (status), dietary restrictions
4. **Upcoming Appointments** — next 5 appointments for this client (from `IAppointmentService.GetUpcomingByClientAsync()`)
5. **Active Meal Plans** — current/active meal plans assigned to client

## Consent Flow

1. **Practitioner initiates client creation** — Client registration is practitioner-only (v1 scope)
2. **ClientDto passed with ConsentGiven=true** — practitioner verifies consent before submission
3. **`ClientService.CreateAsync()` enforces** `ConsentGiven` validation
4. **`ConsentService.GrantConsentAsync()` called** — records consent event, updates client fields
5. **`ConsentEvent` created immutably** — audit trail locked for PIPEDA compliance
6. **Subsequent operations check consent** — appointments, meal plans, progress entries verify `ConsentGiven`

## Health Profile Management

### Adding/Updating Health Records

All health profile operations follow the same pattern:

1. **Service method called** with entity details (e.g., allergy name, severity, type)
2. **Entity created/updated** in database
3. **Audit log recorded** with user ID, action, and details
4. **`INotificationDispatcher` notified** for real-time client detail page refresh
5. **Soft-delete on removal** — never hard-delete for data integrity

### Allergen Checking

When a meal plan is created or updated, `IAllergenCheckService.CheckAsync()` compares meal items against client allergies:

- Matches by category (e.g., "Peanuts" → AllergenCategory.TreeNuts)
- Returns `List<AllergenWarningDto>` with severity and meal location
- Practitioner can override warnings with notes
- See `docs/meal-plans/domain-summary.md` for allergen system details

## Soft-Delete & Data Retention

### Soft-Delete Pattern

All client and health profile entities follow the soft-delete pattern:

- `IsDeleted` flag set to true on deletion
- `DeletedAt` timestamp recorded (UTC)
- `DeletedBy` user ID recorded for audit
- Records excluded from most queries via global query filter in `AppDbContext`
- Explicitly retrievable via `.IgnoreQueryFilters()` for admin/audit purposes

### Data Retention & Purging

Canadian PIPEDA requires:

- Clients inactive > 7 years → data purged (anonymized or deleted)
- `LastInteractionDate` updated on every operation (appointment, meal plan, progress entry)
- `RetentionExpiresAt` calculated as `LastInteractionDate + RetentionYears`
- Background job (`DataPurgeService`) runs periodically to purge expired records

See `docs/compliance/data-retention.md` for full details.

## AI Assistant Integration

Four write tools and three read tools expose client functionality to the AI assistant.

**Read Tools:**

- `list_clients` — query with optional search term and pagination
- `get_client` — fetch single client with health profile
- `search_clients` — search by name, email, phone

**Write Tools:**

- `create_client` — create new client (requires consent verification)
- `update_client` — update client fields and health profile
- `add_client_allergy` — add allergy with severity/type
- `update_client_condition` — update condition status

All write tools require user confirmation (Standard tier) before execution. Audit source tagged as `"ai_assistant"`.

**Tool Definitions:** `/src/Nutrir.Infrastructure/Services/AiToolExecutor.cs`

## CLI Tool

Comprehensive CLI command suite for client management.

**File:** `/src/Nutrir.Cli/Commands/ClientCommands.cs`

**Commands:**

- `nutrir clients list [--search "term"] [--page N]` — list clients
- `nutrir clients get ID` — fetch client details with health profile
- `nutrir clients create --first "John" --last "Doe" --email "john@example.com" [--phone "555-1234"] [--notes "..."]` — create client
- `nutrir clients update ID [--first "..."] [--last "..."] [--email "..."]` — update client
- `nutrir clients delete ID` — soft-delete client
- `nutrir clients add-allergy ID --name "Peanuts" --severity Moderate --type Food` — add allergy
- `nutrir clients add-medication ID --name "Metformin" [--dosage "500mg"] [--frequency "Daily"]` — add medication

All commands support `--format json|table` and `--connection-string` overrides.

## Known Issues & Future Work

### High Priority (v1 Scope Completion)

1. **Intake Form Completion** — `IntakeForm` and `IntakeFormResponse` entities exist but no UI to auto-populate health profile from responses
   - **Fix:** Implement `ClientHealthProfileService.SyncFromIntakeFormAsync()` and intake form completion handler

2. **Health Profile UI** — no dedicated page to view/edit complete health profile (only visible nested in client detail)
   - **Fix:** Create `ClientHealthProfile.razor` page with add/edit/delete UI for each health profile section

3. **Consent History UI** — no page to display `ConsentEvent` audit trail
   - **Fix:** Create `ClientConsentHistory.razor` modal or page

### Medium Priority (v2+)

- **Client deduplication** — no duplicate detection when creating similar clients
- **Import/export** — bulk client import from CSV (with consent validation)
- **Client segments** — tag clients by condition, goal, dietary restriction for AI filtering
- **Health history timeline** — visual timeline of condition/medication/allergy changes

## Database Migrations

**Base Migration:** `20260125003421_AddClients.cs`

Creates `Clients` table and all health profile tables with:

- Clustered PK on `Id`
- FK to `AspNetUsers(PrimaryNutritionistId)` — cascading delete
- Indexes on `FirstName`, `LastName`, `Email` for search performance
- Soft-delete indexes on `IsDeleted`

**Child Tables:**

- `ClientAllergies` — FK to Clients(ClientId), indexed on ClientId
- `ClientMedications` — FK to Clients(ClientId), indexed on ClientId
- `ClientConditions` — FK to Clients(ClientId), indexed on ClientId
- `ClientDietaryRestrictions` — FK to Clients(ClientId), indexed on ClientId
- `ConsentEvents` — FK to Clients(ClientId), indexed on ClientId and Timestamp
- `ConsentForms` — FK to Clients(ClientId), indexed on ClientId and SignatureMethod
- `IntakeForms` — FK to Clients(ClientId), indexed on AccessToken (unique)

## Documentation & Standards

### Where to Add New Docs

All clients documentation goes in `/docs/clients/`.

**Existing documents:**

- `domain-summary.md` — this file
- `health-profile.md` — allergies, medications, conditions, dietary restrictions (ERD, enums, design decisions)
- `intake-form-design.md` — pre-appointment digital intake form spec (entities, workflow, token strategy, field mapping)

**Expected documents (not yet created):**

- `adr-0001-consent-verification-strategy.md` — decision on when/how to enforce consent checks
- `consent-audit-design.md` — spec for consent history UI and audit export

### Conventions

- All times in code are UTC (`DateTime.UtcNow`)
- DTOs denormalize nutritionist names for UI convenience
- Soft-delete flags included in DTOs for admin visibility
- Audit logs capture entity type `"Client"` and ID as string
- Health profile child entities inherit soft-delete pattern from parent

## External Dependencies

- **Auth domain** — `PrimaryNutritionistId` must be valid `ApplicationUser`
- **Appointments domain** — appointments depend on clients with valid `ConsentGiven`
- **Meal Plans domain** — allergen checking reads `ClientAllergy` records
- **Progress domain** — progress entries are scoped to clients
- **Compliance domain** — consent, audit logging, data retention, and soft-delete patterns follow compliance standards

## Queries Used Across the App

From codebase search, clients are queried/displayed in:

1. **Client List Page** — all clients with search and pagination
2. **Client Detail Page** — single client with health profile and upcoming appointments
3. **Appointment Pages** — client dropdown selector for appointment creation
4. **Meal Plan Pages** — client dropdown selector and allergen checking
5. **Progress Pages** — client dropdown selector
6. **Dashboard** — recent clients, active clients count
7. **Search Results** — clients included in global search
8. **AI Assistant Tools** — list/get/search/create/update operations

---

## Summary of Current State

**Complete:**

- Core client entity model (Client.cs)
- Health profile child entities (allergies, medications, conditions, dietary restrictions)
- Consent and audit entities (ConsentEvent, ConsentForm)
- Intake form entities (IntakeForm, IntakeFormResponse)
- Full service layer with CRUD, health profile management, and consent operations
- DTOs for all operations and views
- CLI tool with comprehensive commands
- AI assistant integration (read + write tools)
- Client list and detail Blazor pages with modern card-based design
- Real-time updates via `INotificationDispatcher`

**Missing / Incomplete:**

- Intake form completion UI and auto-sync to health profile
- Dedicated health profile edit page
- Consent history/audit trail display
- Client deduplication detection
- Health history timeline

**Next Steps for Implementation:**

1. Create `/docs/clients/adr-0001-consent-verification-strategy.md`
2. Implement intake form completion handler (`SyncFromIntakeFormAsync`)
3. Create `ClientHealthProfile.razor` edit page
4. Create `ClientConsentHistory.razor` modal
5. Wire pages into main navigation if not already done
