# ADR-0002: Automated Appointment Reminders

**Status:** Accepted
**Date:** 2026-03-09

## Context

No-show appointments reduce practitioner efficiency. Automated email reminders sent before appointments are a proven way to reduce no-show rates.

## Decision

### Architecture

- **BackgroundService** with `PeriodicTimer(15 minutes)` polls for upcoming appointments and sends reminders.
- **AppointmentReminder** entity tracks every send attempt (success and failure).
- **Idempotency** via unique constraint `(AppointmentId, ReminderType, ScheduledFor)` — if an appointment is rescheduled, `ScheduledFor` changes, allowing new reminders.

### Reminder Windows

| Type | Window | Skip If |
|------|--------|---------|
| FortyEightHour | 24–48h before appointment | Appointment was created after the 48h mark |
| TwentyFourHour | 0–24h before appointment | Appointment was created after the 24h mark |

### Eligibility Criteria

- Appointment status: `Scheduled` or `Confirmed`
- Client has `EmailRemindersEnabled = true`
- Client has `ConsentGiven = true`
- Client has a non-null email address
- No existing reminder for the same `(AppointmentId, ReminderType, ScheduledFor)` tuple

### Compliance Constraints

- **Separate consent**: `EmailRemindersEnabled` is independent of `ConsentGiven` (opt-in, default false)
- **Consent cascade**: Withdrawing treatment consent automatically disables email reminders
- **PHI restrictions**: Emails contain only first name, date/time, and practice name — no appointment type or health details
- **Subject line**: Generic "Appointment Reminder"
- **Opt-out instructions**: Included in every email
- **Audit trail**: Every send attempt logged via `IAuditLogService`
- **Soft-delete**: Applied to `AppointmentReminder` records with query filter

### Timezone Strategy

- All dates stored in UTC
- Email body displays times in `America/Toronto` timezone (practice timezone)
- UI displays times via `ITimeZoneService.ToUserLocal()`

### Email Infrastructure

- Reuses existing `IEmailService` (MailKit/Gmail SMTP)
- `IReminderEmailBuilder` constructs PHI-safe HTML with inline CSS
- Gmail SMTP acceptable because email content contains no protected health information

### Error Handling

- Per-appointment try/catch: one failure doesn't stop processing of other appointments
- Outer try/catch per tick: the background service never crashes
- Failed reminders are recorded with `FailureReason` for visibility

## Consequences

- Practitioners can see reminder history on appointment detail pages
- Manual resend is available for failed reminders
- The 15-minute polling interval means reminders are sent within 15 minutes of becoming eligible
- No dependency on external job schedulers (Hangfire, Quartz) — uses built-in `BackgroundService`
