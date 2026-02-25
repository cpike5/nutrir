# Nutrir CLI Tool

## Overview

`nutrir` is a local command-line tool for programmatic CRUD operations across all Nutrir domains. It reuses the same service layer as the Blazor web application — the same business rules, validation, and audit logging apply to all CLI operations.

Primary use cases:
- Agent-driven automation (AI assistant backend operations)
- Bulk data operations and scripting
- Development and testing workflows
- Administrative tasks without a browser

## Installation

Run directly via `dotnet run`:

```bash
dotnet run --project src/Nutrir.Cli -- <command> [options]
```

Or build the binary and run it directly:

```bash
dotnet publish src/Nutrir.Cli -o ./bin/cli
./bin/cli/nutrir <command> [options]
```

## Global Options

These options apply to all commands:

| Option | Default | Description |
|--------|---------|-------------|
| `--user-id` | `NUTRIR_USER_ID` env var | User ID for audit trail. Required for all mutation commands (create, update, delete). |
| `--format` | `json` | Output format: `json` or `table` |
| `--source` | `cli` | Action source tag written to the audit trail |
| `--connection-string` | From `appsettings.json` | Override the database connection string |

Set `NUTRIR_USER_ID` in your environment to avoid passing `--user-id` on every command:

```bash
export NUTRIR_USER_ID="3fa85f64-5717-4562-b3fc-2c963f66afa6"
```

## Output Format

All commands return a JSON envelope:

```json
{
  "success": true,
  "data": { ... },
  "error": null
}
```

On failure:

```json
{
  "success": false,
  "data": null,
  "error": "Validation failed: Email address is already in use."
}
```

### Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success |
| `1` | Not found or validation error |
| `2` | Infrastructure error (DB unavailable, connection failure, etc.) |

## Command Reference

### `clients`

Manage client profiles.

```bash
# List all clients, optionally filtered by search term
nutrir clients list [--search <term>]

# Get a single client by ID
nutrir clients get <id>

# Create a new client
nutrir clients create \
  --first-name <value> \
  --last-name <value> \
  [--email <value>] \
  [--phone <value>] \
  [--dob <YYYY-MM-DD>] \
  [--nutritionist-id <guid>] \
  [--consent] \
  [--notes <value>]

# Update an existing client
nutrir clients update <id> \
  [--first-name <value>] \
  [--last-name <value>] \
  [--email <value>] \
  [--phone <value>] \
  [--dob <YYYY-MM-DD>] \
  [--notes <value>]

# Delete a client (soft delete)
nutrir clients delete <id>
```

---

### `appointments`

Manage client appointments and sessions.

```bash
# List appointments with optional filters
nutrir appointments list \
  [--client-id <id>] \
  [--from <ISO8601>] \
  [--to <ISO8601>] \
  [--status <status>]

# Get a single appointment
nutrir appointments get <id>

# Create a new appointment
nutrir appointments create \
  --client-id <id> \
  --type <type> \
  --start <ISO8601> \
  --duration <minutes> \
  --location <location> \
  [--url <value>] \
  [--notes <value>]

# Cancel an appointment
nutrir appointments cancel <id> [--reason <value>]

# Delete an appointment
nutrir appointments delete <id>
```

**Appointment Types**: `InitialConsultation`, `FollowUp`, `CheckIn`

**Appointment Status**: `Scheduled`, `Confirmed`, `Completed`, `NoShow`, `LateCancellation`, `Cancelled`

**Location**: `InPerson`, `Virtual`, `Phone`

---

### `meal-plans`

Manage client meal plans.

```bash
# List meal plans with optional filters
nutrir meal-plans list [--client-id <id>] [--status <status>]

# Get a single meal plan
nutrir meal-plans get <id>

# Create a new meal plan
nutrir meal-plans create \
  --client-id <id> \
  --title <value> \
  --days <number> \
  [--description <value>] \
  [--calories <number>] \
  [--protein <grams>] \
  [--carbs <grams>] \
  [--fat <grams>] \
  [--notes <value>] \
  [--instructions <value>]

# Add structured meal content from a JSON file
nutrir meal-plans add-content <id> --from-json <file>

# Change plan status
nutrir meal-plans activate <id>
nutrir meal-plans archive <id>

# Duplicate an existing plan
nutrir meal-plans duplicate <id>

# Delete a plan
nutrir meal-plans delete <id>
```

**Meal Plan Status**: `Draft`, `Active`, `Archived`

---

### `goals`

Manage client health goals.

```bash
# List goals for a client
nutrir goals list --client-id <id>

# Get a single goal
nutrir goals get <id>

# Create a new goal
nutrir goals create \
  --client-id <id> \
  --title <value> \
  --type <type> \
  [--target-value <number>] \
  [--target-unit <value>] \
  [--target-date <YYYY-MM-DD>] \
  [--description <value>]

# Update a goal
nutrir goals update <id> \
  [--title <value>] \
  [--description <value>] \
  [--target-value <number>] \
  [--target-unit <value>] \
  [--target-date <YYYY-MM-DD>] \
  [--type <type>]

# Change goal status
nutrir goals achieve <id>
nutrir goals abandon <id>

# Delete a goal
nutrir goals delete <id>
```

**Goal Types**: `Weight`, `BodyComposition`, `Dietary`, `Custom`

**Goal Status**: `Active`, `Achieved`, `Abandoned`

---

### `progress`

Record and retrieve client progress measurements.

```bash
# List progress entries for a client
nutrir progress list --client-id <id>

# Get a single progress entry
nutrir progress get <id>

# Create a progress entry
nutrir progress create \
  --client-id <id> \
  --date <YYYY-MM-DD> \
  --metrics '<json>' \
  [--notes <value>]

# Delete a progress entry
nutrir progress delete <id>
```

**Metrics JSON format** (passed as a JSON array string):

```json
[
  { "type": "Weight", "value": 80, "unit": "kg" },
  { "type": "WaistCircumference", "value": 88, "unit": "cm" }
]
```

**Metric Types**: `Weight`, `BodyFatPercentage`, `WaistCircumference`, `HipCircumference`, `BMI`, `BloodPressureSystolic`, `BloodPressureDiastolic`, `RestingHeartRate`, `Custom`

---

### `users`

Manage practitioner and admin user accounts.

```bash
# List users with optional filters
nutrir users list [--search <term>] [--role <role>] [--active <bool>]

# Get a single user
nutrir users get <id>

# Create a user
nutrir users create \
  --first-name <value> \
  --last-name <value> \
  --email <value> \
  --role <role> \
  [--password <value>]

# Update user profile
nutrir users update <id> \
  [--first-name <value>] \
  [--last-name <value>] \
  [--display-name <value>] \
  [--email <value>]

# Change a user's role
nutrir users change-role <id> --role <role>

# Activate / deactivate a user account
nutrir users deactivate <id>
nutrir users reactivate <id>

# Reset a user's password
nutrir users reset-password <id> [--password <value>]
```

**Roles**: `Admin`, `Nutritionist`

**User IDs**: String GUIDs (e.g., `3fa85f64-5717-4562-b3fc-2c963f66afa6`)

---

### `search`

Global full-text search across all entities.

```bash
nutrir search <query>
```

Returns matched clients, appointments, meal plans, and goals in a unified result set.

---

### `dashboard`

Retrieve the practitioner dashboard summary (upcoming appointments, recent clients, active plans).

```bash
nutrir dashboard
```

---

### `audit`

View the audit trail of recorded actions.

```bash
# List recent audit entries (defaults to last 50)
nutrir audit list [--count <number>]
```

## Examples

Complete workflow for onboarding a new client:

```bash
# Create a client
nutrir clients create \
  --first-name John \
  --last-name Smith \
  --consent \
  --notes "Celiac disease, lactose intolerant"

# Schedule an initial consultation
nutrir appointments create \
  --client-id 1 \
  --type InitialConsultation \
  --start "2026-02-25T14:00" \
  --duration 60 \
  --location InPerson

# Create a meal plan
nutrir meal-plans create \
  --client-id 1 \
  --title "Celiac/LI Plan" \
  --days 7 \
  --calories 2000

# Set a dietary goal
nutrir goals create \
  --client-id 1 \
  --title "Meal plan adherence" \
  --type Dietary

# Create a nutritionist user account
nutrir users create \
  --first-name Stephanie \
  --last-name Derpington \
  --email stephanie@derp.com \
  --role Nutritionist
```

## Audit Trail

All mutation commands (create, update, delete, cancel, activate, etc.) are automatically logged to the audit trail. Each entry records:

- The user ID (`--user-id` or `NUTRIR_USER_ID`)
- The action performed and the affected entity
- A source tag (`--source`, defaults to `cli`)
- Timestamp

The `--source` flag can be used to distinguish automated agent actions from manual CLI use (e.g., `--source "ai-assistant"` vs the default `cli`). Use `nutrir audit list` to review recent entries.
