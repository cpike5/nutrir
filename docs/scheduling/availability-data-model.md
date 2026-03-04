# Practitioner Availability Data Model

## Entities

### PractitionerSchedule

Recurring weekly availability windows for a practitioner.

| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| UserId | string (FK → AspNetUsers) | The practitioner |
| DayOfWeek | DayOfWeek (stored as string) | .NET DayOfWeek enum (0=Sunday..6=Saturday) |
| StartTime | TimeOnly | Start of availability window |
| EndTime | TimeOnly | End of availability window |
| IsAvailable | bool | false = day off override |
| IsDeleted | bool | Soft-delete flag |
| CreatedAt | DateTime | UTC timestamp |
| UpdatedAt | DateTime? | Last modification |
| DeletedAt | DateTime? | Soft-delete timestamp |
| DeletedBy | string? | User who deleted |

**Index:** `(UserId, DayOfWeek)`

### PractitionerTimeBlock

One-off blocked periods (lunch, personal, meeting) on a specific date.

| Field | Type | Notes |
|-------|------|-------|
| Id | int (PK) | Auto-increment |
| UserId | string (FK → AspNetUsers) | The practitioner |
| Date | DateOnly | Specific date |
| StartTime | TimeOnly | Block start |
| EndTime | TimeOnly | Block end |
| BlockType | TimeBlockType (stored as string) | Lunch, Personal, Meeting |
| Notes | string? (text) | Optional description |
| IsDeleted | bool | Soft-delete flag |
| CreatedAt | DateTime | UTC timestamp |
| UpdatedAt | DateTime? | Last modification |
| DeletedAt | DateTime? | Soft-delete timestamp |
| DeletedBy | string? | User who deleted |

**Index:** `(UserId, Date)`

### ApplicationUser (modified)

| Field | Type | Notes |
|-------|------|-------|
| BufferTimeMinutes | int | Default: 15. Time between appointments. |

## Enums

### TimeBlockType

- `Lunch`
- `Personal`
- `Meeting`

## Available Slots Algorithm

`GetAvailableSlotsAsync(practitionerId, date, durationMinutes)`:

1. Load practitioner's schedule for the given day of week
2. If `!IsAvailable` or no schedule entry → return empty
3. Load buffer time from ApplicationUser
4. Load existing booked appointments (excluding cancelled) for the practitioner on that date
5. Load time blocks for that date
6. Build blocked intervals: appointments (expanded by buffer on both sides) + time blocks
7. Generate candidate slots at 15-minute intervals within the availability window
8. Filter out slots that overlap any blocked interval
9. Return remaining slots as `AvailableSlotDto(Start, End)`

## Overlap Detection

When creating or updating appointments, the system checks for conflicts:

1. Expand the proposed appointment window by buffer time on both sides
2. Query for any non-deleted, non-cancelled appointment for the same practitioner overlapping the expanded window
3. If a conflict is found, throw `SchedulingConflictException` with details about the conflicting appointment
