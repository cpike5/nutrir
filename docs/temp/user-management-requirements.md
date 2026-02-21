# Requirements: User Management

## Problem Statement

The Nutrir CRM needs a user management system so the primary practitioner (owner/admin) can manage staff accounts (nutritionists, assistants) and client records. Registration must be gated by invite codes — no open registration.

## Primary Purpose

Provide role-based user management with invite-gated staff registration, client record CRUD, and a first-run seeding routine for the initial admin account.

---

## Target Users

| Role | Description | v1 Login? |
|------|-------------|-----------|
| **Owner/Admin** | Primary nutritionist. Full system access. Manages staff and clients. | Yes |
| **Nutritionist** | Additional practitioner on staff. Manages clients, can invite staff. | Yes |
| **Assistant** | Support staff. Role defined but permissions not restricted in v1. | Yes |
| **Client** | End consumer of nutrition services. Data record only in v1. | No (future) |

---

## Core Features (MVP / v1)

### 1. Role System

- Four roles: `Admin`, `Nutritionist`, `Assistant`, `Client` (client role defined for future use)
- Roles stored via ASP.NET Identity role system (`AspNetRoles`)
- Admin and Nutritionist have equivalent access in v1 (Assistant permissions deferred)
- Role assignment at invite code creation time; applied on registration

### 2. Staff Account Management

**Available to:** Admin, Nutritionist

- **User List Page** — table of all staff accounts showing name, email, role, status (active/deactivated), last login
- **Basic filtering and search** — filter by role, status; search by name/email
- **User Detail/Edit View** — view and edit staff profile, change role, deactivate
- **Actions:**
  - View/edit user profile
  - Assign or change role
  - Deactivate account (no hard delete — deactivation only)
  - Reset password
  - Force MFA enrollment

### 3. Invite Code System

**Available to:** Admin, Nutritionist

- **Generate invite code** — creates a single-use code tied to a target role
- **Configurable expiration** — default 7 days, adjustable at generation time
- **Manual sharing** — practitioner copies code and shares out-of-band (email delivery is future)
- **Invite code management UI** — view active, expired, and used codes
- **2-step registration wizard:**
  1. **Step 1:** Enter invite code → validate (exists, not expired, not used)
  2. **Step 2:** Standard registration form (name, email, password)
- Code is consumed on successful registration
- Code records who generated it and who redeemed it

### 4. Client Records (No Login)

**Available to:** Admin, Nutritionist, Assistant

- **Client data fields:**
  - First name, last name
  - Email, phone
  - Date of birth
  - Primary nutritionist assignment (FK to ApplicationUser)
  - Consent capture: checkbox + timestamp + policy version (per compliance spec)
  - Notes / health info
- **CRUD operations** — create, read, update, soft-delete
- **Soft delete** — client records are never hard-deleted (compliance requirement)
- Client has a primary nutritionist but all staff can access all clients in v1

### 5. ApplicationUser Extension

Extend the existing `ApplicationUser` entity with:

- First name
- Last name
- Display name
- IsActive flag (for deactivation)
- Created date
- Last login date

### 6. First-Run Database Seeding

- On first run, seed the database with:
  - Identity roles: `Admin`, `Nutritionist`, `Assistant`, `Client`
  - Initial admin account
- **Admin credentials configurable** via (in priority order):
  1. User secrets (`dotnet user-secrets`)
  2. Environment variables
  3. `appsettings.json` / `appsettings.{Environment}.json`
- Uses the standard .NET configuration system (Options pattern)
- Idempotent — safe to run multiple times, only seeds if roles/admin don't exist

### 7. Restyle Identity Pages

Restyle all scaffolded ASP.NET Identity pages to match the Nutrir design system:

- Login
- Register (replaced by 2-step invite wizard)
- Manage profile
- Change password / set password
- 2FA setup, enable/disable, recovery codes
- Email management
- External logins
- Forgot password / reset password
- Email confirmation
- Lockout, access denied

### 8. Audit Logging

User management actions captured in the audit log from v1:

- Staff account created
- Role changed
- Account deactivated / reactivated
- Password reset (by admin)
- MFA forced
- Invite code generated
- Invite code redeemed
- Client record created / updated / soft-deleted

---

## Future Features (Out of Scope for v1)

- Client portal with login (invite-code gated)
- Assistant role permission restrictions
- Client ownership / visibility scoping (nutritionist sees only their clients)
- Automated email delivery of invite codes
- Profile photo, bio, specialization, license number on user profiles
- Application-level field encryption for sensitive health data
- Per-client data export (JSON/PDF)
- Retention tracking and data purge workflows

---

## Tech Stack

| Layer | Technology |
|-------|-----------|
| **Frontend** | Blazor Server (.NET 9), custom design system with Tailwind |
| **Backend** | ASP.NET Identity, EF Core |
| **Database** | PostgreSQL |
| **Auth** | Cookie-based, ASP.NET Identity roles/claims |
| **Logging** | Serilog → Seq (dev) |

---

## Data Model Summary

### Entities to Create/Modify

| Entity | Layer | Action |
|--------|-------|--------|
| `ApplicationUser` | Core | Extend with FirstName, LastName, DisplayName, IsActive, CreatedDate, LastLoginDate |
| `Client` | Core | New entity — client record (no Identity account) |
| `InviteCode` | Core | New entity — code, target role, expiration, created by, redeemed by |
| `AuditLogEntry` | Core | New entity — append-only audit log |

### DTOs / View Models

- `UserListItemDto` — for user list page
- `UserDetailDto` — for user detail/edit view
- `CreateInviteCodeDto` — for invite code generation
- `InviteCodeListItemDto` — for invite code management UI
- `ClientDto` — for client CRUD
- `RegisterWithInviteDto` — for 2-step registration

### Services / Interfaces

| Interface | Layer | Purpose |
|-----------|-------|---------|
| `IUserManagementService` | Core | Staff account CRUD, role management, deactivation |
| `IInviteCodeService` | Core | Generate, validate, redeem invite codes |
| `IClientService` | Core | Client record CRUD with soft-delete |
| `IAuditLogService` | Core | Append audit log entries |

Implementations in Infrastructure layer, registered via `AddInfrastructure()`.

---

## Constraints

- **Security:** Invite-gated registration only. No open signup.
- **Compliance:** Soft-delete on client data. Consent capture. Audit logging on all management actions.
- **Localization:** English (en-CA) v1, but use resource files / avoid hardcoded UI strings.
- **Scale:** Single practice, low user count. Simple table with search is sufficient.

---

## Open Questions

1. ~~Client login scope~~ — Confirmed: no client login in v1
2. ~~Assistant permissions~~ — Confirmed: deferred to future
3. **Password policy** — use ASP.NET Identity defaults, or custom requirements? (e.g., minimum length, complexity)
4. **Session management** — should admin be able to force-logout other users?
5. **Invite code format** — short alphanumeric (e.g., `ABC-1234`), UUID, or something else?

---

## Decisions Made

| Decision | Rationale |
|----------|-----------|
| Invite-gated registration only | Security — no open signup for a health-data CRM |
| Soft-delete for clients, deactivation for staff | Compliance requires no hard-delete of client data; staff accounts use deactivation flag |
| Roles via ASP.NET Identity | Already have the infrastructure, no need for custom role system |
| Admin seeded on first run | No chicken-and-egg problem; first user is always the owner |
| Configurable admin credentials | Follows .NET conventions; secure via user-secrets in dev |
| Manual invite sharing for v1 | Simpler; email delivery added later |
| All staff see all clients in v1 | Small practice; client visibility scoping is future work |
