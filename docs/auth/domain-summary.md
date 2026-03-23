# Auth Domain — Current State Summary

**Last Updated:** 2026-03-22

## Overview

The auth domain manages user identity, authentication, authorization, and user management for Nutrir. It uses ASP.NET Core Identity for user accounts and role-based access control (RBAC) for feature gating. Key features include invite-code based user creation, role hierarchy, MFA support, and timezone preferences.

## Core Entities & Data Model

### ApplicationUser Entity

**File:** `/src/Nutrir.Core/Entities/ApplicationUser.cs`

Extends `IdentityUser` with application-specific fields.

**Identity Fields (inherited from IdentityUser):**

- `Id` (string) — unique user identifier (GUID)
- `UserName` (string) — login username
- `Email` (string, nullable) — email address
- `EmailConfirmed` (bool) — whether email is verified
- `PasswordHash` (string, nullable) — hashed password
- `SecurityStamp` (string) — used for password/security changes
- `ConcurrencyStamp` (string) — for optimistic concurrency
- `PhoneNumber` (string, nullable)
- `PhoneNumberConfirmed` (bool)
- `TwoFactorEnabled` (bool) — MFA status
- `LockoutEnd` (DateTime, nullable) — account lockout expiry
- `LockoutEnabled` (bool) — whether lockout is enforced
- `AccessFailedCount` (int) — failed login attempt count

**Application Fields:**

- `FirstName` (string) — practitioner first name
- `LastName` (string) — practitioner last name
- `DisplayName` (string) — public name (used in client-facing contexts)
- `IsActive` (bool, default true) — soft-disable for practitioners on leave
- `CreatedDate` (DateTime, UTC) — account creation timestamp
- `LastLoginDate` (DateTime, nullable, UTC) — last successful login
- `TimeZoneId` (string, default "America/Toronto") — practitioner's timezone for appointment/reminder display
- `BufferTimeMinutes` (int, default 15) — default buffer time between appointments (for availability calculations)

### IdentityRole (Standard ASP.NET Core)

Nutrir uses three roles:

- **Admin** — system administration, user management, maintenance mode
- **Nutritionist** — practitioner with full access to clients, meals, progress, appointments
- **Assistant** — support staff with limited access (view clients, manage intake forms, but no write access to clinical data)

**Role Assignment:**

Roles are assigned via `UserManager.AddToRoleAsync()` after account creation. Users typically have one role; no hierarchy (no role inheritance).

### Invite Code Entity

**File:** `/src/Nutrir.Core/Entities/InviteCode.cs`

Secure one-time tokens for user registration (v1 scope: admin-only invite workflow).

**Fields:**

- `Id` (int, PK)
- `Code` (string, indexed, unique) — secure random token
- `Email` (string) — email for which invite was issued
- `Role` (string) — default role when user accepts invite
- `CreatedByUserId` (string) — admin who issued invite
- `CreatedAt` (DateTime, UTC)
- `AcceptedByUserId` (string, nullable) — user ID who used the invite
- `AcceptedAt` (DateTime, nullable, UTC) — when invite was redeemed
- `ExpiresAt` (DateTime, UTC) — invite expiry (typically 30 days)
- `IsRevoked` (bool) — whether invite was revoked by admin

## Authentication Setup

### Program.cs Configuration

**File:** `/src/Nutrir.Web/Program.cs`

Authentication is configured in three steps:

```csharp
// 1. Cascading authentication state for Blazor components
builder.Services.AddCascadingAuthenticationState();

// 2. Accessors for identity operations and redirect management
builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();

// 3. Authentication state provider for Blazor
builder.Services.AddScoped<AuthenticationStateProvider,
    IdentityRevalidatingAuthenticationStateProvider>();

// 4. Configure authentication schemes
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();  // Adds both Application and External schemes
```

**Schemes:**

- **IdentityConstants.ApplicationScheme** ("Identity.Application") — default scheme for authenticated requests
- **IdentityConstants.ExternalScheme** ("Identity.External") — for external OAuth/login callbacks

### DbContext Identity Setup

**File:** `/src/Nutrir.Infrastructure/Data/AppDbContext.cs`

```csharp
protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder);  // Configures Identity tables
    // ... additional Nutrir model configurations
}
```

**Identity Tables Created:**

- `AspNetUsers` — user accounts
- `AspNetRoles` — role definitions
- `AspNetUserRoles` — user-to-role mapping
- `AspNetUserClaims` — user claims (MFA, consent, etc.)
- `AspNetUserLogins` — external login connections (future OAuth)
- `AspNetUserTokens` — email confirmation, password reset tokens

## Authorization & Role-Based Access Control

### Role Hierarchy (Informal)

While no formal hierarchy exists in code, logical permission levels:

1. **Admin** (highest)
   - User management (create, delete, suspend accounts)
   - Role assignment
   - Maintenance mode toggle
   - System diagnostics and reports
   - Audit log access

2. **Nutritionist** (primary)
   - Full CRUD on clients, appointments, meal plans, progress
   - Consent and intake form management
   - All client-facing features
   - Can trigger AI assistant

3. **Assistant** (lowest)
   - View-only clients
   - Create intake forms and capture responses
   - View appointments (read-only)
   - Cannot create or modify clinical data

### Authorization Attributes

Blazor pages use `@attribute [Authorize(Roles = "Nutritionist,Admin")]` to restrict access.

**Example:**

```csharp
@page "/clients"
@attribute [Authorize(Roles = "Nutritionist,Admin")]

// Only Nutritionist and Admin can access this page
```

**API Endpoints:**

Minimal APIs use `.RequireAuthorization(options => options.RequireRole("Nutritionist"))` equivalent.

### Policy-Based Authorization (Future)

Currently not implemented. To support finer-grained permissions (e.g., "can only access own clients"), consider:

```csharp
services.AddAuthorizationBuilder()
    .AddPolicy("ClientOwner", policy =>
        policy.Requirements.Add(new ClientOwnerRequirement()));
```

## User Management

### IUserManagementService & UserManagementService

**File:** `/src/Nutrir.Infrastructure/Services/UserManagementService.cs`

Service layer for user CRUD operations.

#### Methods

- **CreateUserAsync(CreateUserDto, createdByUserId)** — create new user account
  - Validates email uniqueness
  - Creates user via `UserManager<ApplicationUser>`
  - Assigns role(s)
  - Sends welcome email
  - Returns `CreateUserResultDto` with user details

- **GetUserByIdAsync(userId)** — fetch user details

- **GetAllUsersAsync()** — list all users

- **UpdateUserAsync(userId, UpdateUserDto, modifiedByUserId)** — update user fields

- **SetRoleAsync(userId, role, modifiedByUserId)** — change user role

- **DeactivateUserAsync(userId, reason, deactivatedByUserId)** — soft-deactivate user (set `IsActive = false`)

- **ReactivateUserAsync(userId, reactivatedByUserId)** — reactivate deactivated user

- **ResetPasswordAsync(userId, newPassword, resetByUserId)** — force password reset (for locked-out accounts)

- **SetTimeZoneAsync(userId, timeZoneId)** — update user's timezone preference

- **SetBufferTimeAsync(userId, bufferMinutes)** — update appointment buffer time

#### Implementation Notes

- Uses `UserManager<ApplicationUser>` for all identity operations
- Audit logging on every user change
- Email confirmation currently not enforced (future hardening)
- MFA enrollment not yet UI-exposed (flagged in code but not implemented in v1)

### IInviteCodeService & InviteCodeService

**File:** `/src/Nutrir.Infrastructure/Services/InviteCodeService.cs`

Manages invite code generation and redemption.

#### Methods

- **GenerateAsync(email, role, createdByUserId)** — create new invite code
  - Generates cryptographically secure random code
  - Sets expiry (typically 30 days)
  - Sends email with invite link
  - Returns `InviteCodeListItemDto`

- **RedeemAsync(code, firstName, lastName, password)** — accept invite and create user
  - Validates code (exists, not expired, not revoked)
  - Creates user with provided password
  - Links invite to user (`AcceptedByUserId`, `AcceptedAt`)
  - Logs audit event

- **RevokeAsync(codeId, revokedByUserId)** — cancel an invite
  - Sets `IsRevoked = true`
  - Logs audit event

- **GetListAsync()** — list all invites with status

- **GetByCodeAsync(code)** — look up invite by code

#### Implementation Notes

- Code generation uses `Guid.NewGuid().ToString()[..8]` for short, memorable codes
- Codes are case-insensitive and validated with length checks
- Email sending uses `IEmailService` (SMTP)
- No rate limiting on generation (admin trust model; future hardening may add limits)

## MFA (Multi-Factor Authentication)

**Status:** Supported by Identity framework but not yet UI-exposed in Nutrir.

**Fields Present:**

- `ApplicationUser.TwoFactorEnabled` (bool)
- `AspNetUserTokens` table stores TOTP/SMS tokens

**Future Implementation Path:**

1. Add TOTP (Time-based One-Time Password) authenticator support
2. Create MFA setup page in user settings
3. Enforce MFA for admin accounts (required in v2+ per compliance)
4. Add MFA login challenge page

**Related Code:**

- `IdentityRevalidatingAuthenticationStateProvider` in `/src/Nutrir.Web/Components/Account/` handles periodic re-validation

## Timezone Management

### ITimeZoneService & TimeZoneService

**File:** `/src/Nutrir.Infrastructure/Services/TimeZoneService.cs`

Converts times between UTC (database) and user's local timezone.

#### Methods

- **InitializeAsync()** — load current user's timezone preference from database
  - Called once per authentication session
  - Caches result to avoid repeated DB hits
  - Falls back to "America/Toronto" if user has no preference

- **ToUserLocal(DateTime utcDateTime)** — convert UTC to user's local time
  - Used when displaying appointment times, reminders, etc.

- **ToUtc(DateTime localDateTime)** — convert user's local time to UTC
  - Used when accepting user input (appointment creation forms)

#### Implementation Notes

- Uses `TimeZoneInfo.ConvertTimeFromUtc()` and `TimeZoneInfo.ConvertTimeToUtc()`
- Requires `tzdata` package for IANA timezone database on non-Windows systems
- Handles missing timezone gracefully (falls back to UTC)
- Caching avoids repeated lookups during request processing

## Oauth & External Identity (Future)

**Status:** Not yet implemented in v1.

**Placeholders:**

- Identity is configured to support external schemes (`IdentityConstants.ExternalScheme`)
- `AspNetUserLogins` table is created by Identity scaffolding
- Future integrations: Google OAuth, Microsoft account, Apple sign-in

**Implementation Path:**

```csharp
// In Program.cs
builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = configuration["Google:ClientId"];
        options.ClientSecret = configuration["Google:ClientSecret"];
    });
```

## UI Pages & Components

**Current Status:** Login/register pages exist via Identity scaffolding; user management UI likely under construction.

### Login Page

**Path:** `src/Nutrir.Web/Components/Account/Pages/Login.razor`

Standard ASP.NET Core Identity scaffolded login form.

- Email input
- Password input
- "Remember me" checkbox
- Login button
- "Forgot password?" link
- "Register" link (if open registration enabled; likely disabled in v1)

### Register Page

**Path:** `src/Nutrir.Web/Components/Account/Pages/Register.razor`

User registration via invite code redemption.

- Email input (pre-filled if invite-linked)
- Password input
- Confirm password
- First name, last name inputs
- Invite code field (if invite workflow)
- Register button

### User Management Dashboard (presumed)

**Path:** `src/Nutrir.Web/Components/Pages/Admin/UserManagement.razor` (presumed)

Admin-only page for user/invite management.

- List of users with role, status, last login date
- Create new invite button
- Invite code list (pending, accepted, revoked)
- User detail/edit modal
- Deactivate/reactivate user buttons

### User Settings Page (presumed)

**Path:** `src/Nutrir.Web/Components/Pages/Account/Settings.razor` (presumed)

User profile and preferences.

- First name, last name, display name
- Email address
- Password change
- Timezone selector
- Buffer time minutes input
- MFA setup (when implemented)

## Signup/Onboarding Flow (v1)

**Step 1: Admin Issues Invite**
- Admin navigates to User Management
- Clicks "Create Invite"
- Enters new user email and role (Nutritionist or Assistant)
- System generates invite code and sends email

**Step 2: User Receives Email**
- Email contains invite code and link
- User clicks link or copies code

**Step 3: User Registers**
- Registration page shows email pre-filled
- User sets password, first/last name
- User enters invite code
- Submits registration form

**Step 4: System Creates Account**
- `InviteCodeService.RedeemAsync()` validates code
- User account created via `UserManager.CreateAsync()`
- Role assigned from invite
- Invite marked accepted
- User redirected to login or dashboard

**Future (v2): Client Invite Workflow**
- Current: Client registration is practitioner-only
- Future: Clients invited to self-register (set account, accept consent)

## Known Issues & Future Work

### High Priority (v1 Scope Completion)

1. **Email Confirmation** — users can register without confirming email
   - **Fix:** Enforce email confirmation before account activation

2. **MFA UI** — MFA is supported by Identity but no UI to enable it
   - **Fix:** Create MFA setup page in user settings (TOTP authenticator app)

3. **Password Reset Flow** — no self-service password reset UI
   - **Fix:** Create forgot-password and reset-password pages

4. **User Deactivation** — `IsActive` field exists but not wired to authorization checks
   - **Fix:** Add check in `IdentityRevalidatingAuthenticationStateProvider` to deny access for inactive users

5. **Permission/Policy-Based Auth** — only role-based (Nutritionist can do anything)
   - **Fix:** Implement claims-based policies if client data segregation needed (e.g., practitioners can only see "their" clients)

### Medium Priority (v2+)

- **OAuth Integration** — Google, Microsoft, Apple sign-in for improved UX
- **LDAP/Active Directory** — multi-location practice support
- **Service Accounts** — API keys for integrations
- **Audit Trail** — detailed log of all user actions (exists partially via `AuditLogService`)
- **Session Management** — explicit session timeout, concurrent session limits

## Database Migrations

**Base Migration:** `20250125000000_CreateIdentitySchema.cs` (auto-generated by Identity scaffolding)

**Custom Extension:** `20250125000001_AddApplicationUserFields.cs`

Adds Nutrir-specific columns:

- `FirstName` (string)
- `LastName` (string)
- `DisplayName` (string)
- `IsActive` (bool, default true)
- `CreatedDate` (datetime)
- `LastLoginDate` (datetime, nullable)
- `TimeZoneId` (string, default "America/Toronto")
- `BufferTimeMinutes` (int, default 15)

**Invite Codes Migration:** `20250125000002_AddInviteCodes.cs`

Creates `InviteCodes` table.

## Configuration & Environment Variables

**Key Secrets (in appsettings.json or .env):**

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=...; Database=nutrir; ..."
  },
  "Authentication": {
    "JwtSecret": "..."  // If JWT added in future
  },
  "Google": {
    "ClientId": "...",
    "ClientSecret": "..."  // OAuth setup (future)
  }
}
```

**Default Timezone:**

```csharp
const string DefaultTimeZoneId = "America/Toronto";
```

Practitioners can override via settings.

## Documentation & Standards

### Where to Add New Docs

All auth documentation goes in `/docs/auth/`.

**Existing documents:**

- `domain-summary.md` — this file

**Expected documents (not yet created):**

- `adr-0001-invite-code-workflow.md` — decision on invite-based registration vs. open signup
- `adr-0002-mfa-strategy.md` — MFA implementation plan
- `oauth-setup-guide.md` — instructions for Google/Microsoft OAuth setup
- `password-policy.md` — strength requirements, expiry, rotation rules

### Conventions

- All times stored as UTC in database
- User-facing times converted to local timezone via `ITimeZoneService`
- Roles are case-sensitive in code (exact string match)
- Invite codes are case-insensitive
- User IDs are GUIDs (string type in code)
- Password hashing via `UserManager` (never store plain text)

## External Dependencies

- **Clients domain** — users referenced by client's `PrimaryNutritionistId`
- **Appointments domain** — appointments reference `ApplicationUser` as nutritionist
- **Compliance domain** — audit logging of all user actions, MFA for v2+
- **ASP.NET Core Identity** — standard framework for all user management

## Queries Used Across the App

User/auth queried/used in:

1. **Login/Register** — Identity framework via `SignInManager`, `UserManager`
2. **Current User Info** — via `AuthenticationStateProvider`
3. **Nutritionist Name Resolution** — in DTOs (client/appointment/meal plan list views)
4. **Audit Logging** — `createdByUserId`, `modifiedByUserId` captured on all operations
5. **Timezone Display** — user's timezone loaded via `ITimeZoneService`
6. **User Management Pages** — admin queries for user list, invite status
7. **Appointment Availability** — `BufferTimeMinutes` used by `IAvailabilityService`

---

## Summary of Current State

**Complete:**

- `ApplicationUser` entity with Identity integration
- Three-role RBAC (Admin, Nutritionist, Assistant)
- Authentication setup in Program.cs (cookies, schemes)
- User management service with CRUD
- Invite code service with generation/redemption
- `ITimeZoneService` for UTC↔Local conversion
- Login/register pages (scaffolded)
- Cascading auth state for Blazor components

**Missing / Incomplete:**

- Email confirmation enforcement
- MFA UI (TOTP setup page)
- Self-service password reset UI
- `IsActive` check in authorization middleware
- Policy-based authorization (only role-based currently)
- User deactivation enforcement
- Audit trail comprehensive logging
- OAuth integrations

**Next Steps for Implementation:**

1. Create `/docs/auth/adr-0001-invite-code-workflow.md`
2. Implement email confirmation requirement
3. Create MFA setup page and enforce for admins (v2)
4. Implement password reset workflow
5. Add `IsActive` check to `IdentityRevalidatingAuthenticationStateProvider`
6. Design and implement policy-based authorization if data segregation needed
7. Wire user management pages into admin navigation
