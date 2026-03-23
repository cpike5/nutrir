# ADR-0003: Timezone Handling Strategy

**Status:** Accepted
**Date:** 2026-03-22
**Domain:** Scheduling
**Participants:** Architecture team

## Context

Nutrir serves nutrition practitioners and clients in Canada, which spans multiple time zones (EST/EDT, CST/CDT, MST/MDT, PST/PDT). Appointments must be scheduled and displayed correctly in each user's local time, while remaining unambiguous in the database.

### Key Requirements

1. **Database consistency** — all times stored in UTC to avoid ambiguity (especially around DST transitions)
2. **User experience** — practitioners and clients must see appointment times in their local timezone
3. **Reminders and notifications** — must be sent at the correct local time
4. **Availability slots** — availability windows defined in local time must be queryable as UTC ranges
5. **Timezone support** — Canada uses 5 main time zones (Atlantic, Eastern, Central, Mountain, Pacific)

### Challenges

- **DST transitions** — Canada observes daylight saving time (except Saskatchewan, parts of BC/AB)
- **User mobility** — practitioners travel; timezone preference can change
- **Notification timing** — reminders must fire at local time, not UTC
- **Ambiguous times** — during DST transitions, some local times occur twice

## Decision

**Store all appointment times in UTC in the database. Convert to/from user's local timezone only for display and input.**

### Implementation Strategy

#### 1. UTC Storage

All `Appointment.StartTime` fields are stored in UTC with `DateTimeKind.Utc`. This guarantees:

- No DST ambiguity
- Correct sorting and range queries
- Easy comparison across time zones
- Correct behavior around DST transitions

#### 2. User Timezone Preference

Each `ApplicationUser` has a `TimeZoneId` field:

```csharp
public string TimeZoneId { get; set; } = "America/Toronto";  // IANA timezone ID
```

- Stored as IANA timezone identifier (e.g., "America/Toronto", "America/Vancouver")
- Defaults to "America/Toronto" (Eastern Time, most populous Canadian region)
- Practitioners can override in settings
- Uses system `TimeZoneInfo` database (requires `tzdata` on Linux systems)

#### 3. Timezone Service

`ITimeZoneService` handles all UTC ↔ Local conversions:

```csharp
public interface ITimeZoneService
{
    Task InitializeAsync();  // Load user's timezone preference once at login
    DateTime UserNow { get; }  // Current time in user's local timezone
    DateTime ToUserLocal(DateTime utcDateTime);  // UTC → Local
    DateTime ToUtc(DateTime localDateTime);  // Local → UTC
}
```

**Key Methods:**

- **InitializeAsync()** — called once after authentication
  - Loads user's `TimeZoneId` from database
  - Falls back to "America/Toronto" if not set
  - Caches result to avoid repeated lookups

- **ToUserLocal(DateTime utcDateTime)** — converts UTC to user's local time
  - Used when displaying appointment times in Blazor components
  - Handles DST transitions transparently

- **ToUtc(DateTime localDateTime)** — converts user input to UTC
  - Used when practitioner creates/updates appointment
  - Validates against user's defined timezone

#### 4. Appointment Lifecycle

**Creating an Appointment:**

1. Practitioner fills appointment form (date/time input in local timezone)
2. Form submission calls `TimeZoneService.ToUtc()` on the input
3. `AppointmentService.CreateAsync()` receives UTC time
4. Validation (overlap checking, availability) uses UTC
5. Appointment stored in database with UTC time
6. Confirmation shows time converted back to practitioner's local timezone

**Displaying an Appointment:**

1. Blazor component loads appointment from service
2. DTO contains UTC `StartTime`
3. Component calls `TimeZoneService.ToUserLocal()` for display
4. User sees time in their configured timezone

**Recurring Appointments:**

- Base appointment start time is in UTC
- When creating N recurring appointments (e.g., weekly for 12 weeks):
  - Each slot calculated in UTC
  - Overlap checking includes buffer time (see ADR-0005)
  - Buffer expansion handles DST gracefully (based on UTC durations)

#### 5. Availability Windows

`IAvailabilityService` defines when practitioners are working.

**Availability Definition:**

Practitioners define availability as:

```csharp
// In practitioner settings (future UI)
Monday: 09:00 - 17:00
Tuesday: 09:00 - 17:00
Wednesday: OFF
Thursday: 09:00 - 17:00
Friday: 09:00 - 17:00
Saturday-Sunday: OFF
```

These times are in the practitioner's local timezone.

**Conversion to UTC:**

When validating appointment creation:

1. Load practitioner's working hours (local time)
2. Convert to UTC using practitioner's `TimeZoneId`
3. Check if appointment UTC time falls within converted window

**Challenge:** If practitioner changes timezone mid-week, old availability windows become invalid. Solution: Availability is recalculated at request time (not stored in UTC).

#### 6. Reminders and Notifications

**Email Reminder Example:**

"Your appointment with John Smith is scheduled for tomorrow at 2:00 PM."

- Reminder job runs at UTC time X
- Email recipient is local user (practitioner or client)
- Email system looks up recipient's timezone
- Calculates local time equivalent
- Sends notification with local time

**Implementation:**

```csharp
// In ReminderService
var userTz = user.TimeZoneId;
var localTime = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartTime, userTz);
var emailText = $"Appointment at {localTime:g}";  // Shows local time
```

## Consequences

### Positive

- **DST-safe** — UTC storage handles DST transitions transparently
- **Simple queries** — all database queries use UTC without conversion overhead
- **Portable** — practitioners can travel and update timezone without data migration
- **Standard** — UTC is industry standard for database storage
- **Extensible** — future multi-location practices easily extended (different timezone per location)

### Negative

- **User confusion** — if timezone setting is wrong, times appear incorrect
- **Timezone library dependency** — relies on system's `TimeZoneInfo` database (`tzdata` on Linux)
- **Manual conversion** — every UI component must call `ToUserLocal()` explicitly (easy to forget)
- **Validation complexity** — availability window validation requires runtime timezone conversion

### Mitigations

- **Default timezone** — "America/Toronto" is reasonable default for most Canadian practitioners
- **Timezone validator** — at login, show prompt if timezone appears mismatched (client IP geolocation + stored timezone)
- **Documentation** — clear guidance for UI developers on when to use `ToUserLocal()`
- **Audit logging** — log all timezone changes for compliance
- **Test coverage** — unit tests for DST transitions and edge cases

## Implementation Checklist

- [x] UTC storage in database (all times stored as `DateTimeKind.Utc`)
- [x] `TimeZoneId` field in `ApplicationUser`
- [x] `ITimeZoneService` interface and implementation
- [ ] Timezone selector in user settings UI
- [ ] Timezone validation at login (warn if mismatched)
- [ ] Explicit use of `ToUserLocal()` in all Blazor components
- [ ] Availability window conversion in `IAvailabilityService`
- [ ] Reminder email localization to user's timezone
- [ ] Test coverage for DST edge cases (November, March)
- [ ] Documentation for UI developers
- [ ] CLI commands to update user timezone (admin)

## Related ADRs

- ADR-0005: Overlap Detection Algorithm — includes buffer time expansion based on UTC durations

## References

- [IANA Timezone Database](https://www.iana.org/time-zones)
- [.NET TimeZoneInfo Documentation](https://docs.microsoft.com/en-us/dotnet/api/system.timezoneinfo)
- [Canada Timezone Regions](https://www.timeanddate.com/time/canada/)
- [DST Transition Handling in .NET](https://stackoverflow.com/questions/6993266/getting-the-correct-time-for-a-given-timezone-in-net)
