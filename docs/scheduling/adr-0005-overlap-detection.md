# ADR-0005: Appointment Overlap Detection Algorithm

**Status:** Accepted
**Date:** 2026-03-22
**Domain:** Scheduling
**Participants:** Clinical requirements, database team

## Context

A nutrition practitioner can only see one client at a time. Nutrir must prevent double-booking by detecting scheduling conflicts when appointments are created or updated.

### Requirements

1. **No double-booking** — practitioner cannot have overlapping appointments
2. **Buffer time** — allow configurable break time between appointments (default 15 min)
3. **Time zone aware** — all checks performed in UTC (see ADR-0003)
4. **Cancelled appointments ignored** — don't count cancelled/late-cancelled slots as conflicts
5. **Recurring appointments** — handle multi-week series correctly
6. **Performance** — overlap check should be fast (<10ms) even with 100+ appointments

### Challenge

Buffer time expansion creates edge cases:

- Appointment 1: 2:00 PM - 2:30 PM + 15 min buffer = 2:45 PM
- Appointment 2: 2:45 PM - 3:15 PM + 15 min buffer = 3:30 PM
- Are they in conflict? **Yes** — practitioner needs 15 min break before next appointment

## Decision

**Expand appointment time windows by buffer time on both sides, then use simple interval overlap logic.**

## Algorithm

### Time Window Expansion

For each appointment, expand its time window by the practitioner's buffer time:

```
Original appointment: [StartTime, StartTime + DurationMinutes]
Expanded window:      [StartTime - BufferMinutes, StartTime + DurationMinutes + BufferMinutes]
```

**Example (with 15 min buffer):**

```
Appointment A: 2:00 PM - 2:30 PM
Expanded A:    1:45 PM - 2:45 PM (includes 15 min before and after)

Appointment B: 2:46 PM - 3:15 PM
Expanded B:    2:31 PM - 3:30 PM

Overlap check: 1:45 PM < 3:30 PM AND 2:45 PM > 2:31 PM?
Result: YES, conflict (missing 1 minute)
```

### Overlap Detection Logic

Two intervals `[A_start, A_end]` and `[B_start, B_end]` overlap if:

```
A_start < B_end  AND  A_end > B_start
```

This handles all overlap cases:

- Full overlap (A contains B)
- Partial overlap (A and B intersect)
- Touching boundaries (A_end == B_start → no overlap)

### Implementation

**File:** `/src/Nutrir.Infrastructure/Services/AppointmentService.cs`

```csharp
private async Task CheckOverlapAsync(
    AppDbContext db,
    string nutritionistId,
    DateTime startUtc,
    int durationMinutes,
    int? excludeAppointmentId = null)
{
    var endUtc = startUtc.AddMinutes(durationMinutes);

    // Load practitioner's buffer time preference
    var user = await db.Users
        .OfType<ApplicationUser>()
        .FirstOrDefaultAsync(u => u.Id == nutritionistId);
    var bufferMinutes = user?.BufferTimeMinutes ?? 15;

    // Expand appointment window by buffer on both sides
    var checkStart = startUtc.AddMinutes(-bufferMinutes);
    var checkEnd = endUtc.AddMinutes(bufferMinutes);

    // Find conflicting appointments (exclude cancelled and late-cancelled)
    var query = db.Appointments
        .Where(a => a.NutritionistId == nutritionistId
                    && a.Status != AppointmentStatus.Cancelled
                    && a.Status != AppointmentStatus.LateCancellation);

    if (excludeAppointmentId.HasValue)
        query = query.Where(a => a.Id != excludeAppointmentId.Value);

    // Overlap check: new appointment window overlaps any existing appointment window
    var conflicts = await query
        .Where(a => a.StartTime < checkEnd
                    && a.StartTime.AddMinutes(a.DurationMinutes) > checkStart)
        .ToListAsync();

    if (conflicts.Count == 0)
        return;  // No conflict, proceed

    // Conflict found — throw exception with details
    var conflict = conflicts.First();
    var conflictEnd = conflict.StartTime.AddMinutes(conflict.DurationMinutes);

    // Determine reason: direct overlap vs. buffer violation
    var directOverlap = conflict.StartTime < endUtc && conflictEnd > startUtc;
    var reason = directOverlap ? "Overlapping appointment" : "Buffer time violation";

    // Resolve client name for error message
    var client = await db.Clients
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(c => c.Id == conflict.ClientId);
    var clientName = client is not null
        ? $"{client.FirstName} {client.LastName}"
        : "Unknown";

    throw new SchedulingConflictException(
        reason,
        $"{reason}: conflicts with appointment on {conflict.StartTime:g} for {clientName}",
        conflictId: conflict.Id,
        conflictStartTime: conflict.StartTime,
        conflictEndTime: conflictEnd,
        conflictClientName: clientName);
}
```

### Status Exclusions

Cancelled and late-cancelled appointments do NOT prevent new bookings:

```csharp
&& a.Status != AppointmentStatus.Cancelled
&& a.Status != AppointmentStatus.LateCancellation
```

**Rationale:** These statuses indicate the appointment slot is truly free for rescheduling. Confirmed/completed appointments permanently consume the slot.

### Excluded Appointment ID

When updating an existing appointment, exclude it from conflict check:

```csharp
if (excludeAppointmentId.HasValue)
    query = query.Where(a => a.Id != excludeAppointmentId.Value);
```

**Example:** Practitioner reschedules appointment 42 from 2 PM to 3 PM. Appointment 42 should not conflict with itself.

## Database Query Performance

### Indexes

To support fast overlap detection, create an index on:

```sql
CREATE INDEX idx_appointments_nutritionist_status_time
  ON Appointments(NutritionistId, Status)
  INCLUDE (StartTime, DurationMinutes);
```

This allows the query to:

1. Filter by nutritionist ID (fast)
2. Exclude cancelled statuses (fast)
3. Check time windows (fast)

**Expected Query Time:** <5ms for typical practitioner (50-100 appointments/month).

### Algorithm Complexity

- **Database Filter:** O(n) where n = non-cancelled appointments for practitioner
- **Actual Conflicts:** Usually 0-1 (single practitioner rarely >1 overlap)
- **Total Time:** Dominated by database I/O, not algorithm

For larger scales (multi-location practices), consider:

- Separate database per location
- Calendar-aware indexing (date-partitioned tables)
- In-memory cache of this week's appointments

## Buffer Time Strategy

### Default Buffer

- **Default:** 15 minutes
- **Rationale:** Allows practitioner time to take notes, prepare for next client, use restroom
- **Customizable:** Practitioners can adjust in settings

### Dynamic Buffer

Future enhancement: Different buffer times for different appointment types:

```csharp
var bufferMinutes = appointmentType switch
{
    AppointmentType.InitialConsultation => 30,  // Longer buffer for first meeting
    AppointmentType.FollowUp => 15,
    AppointmentType.CheckIn => 10,
    _ => 15
};
```

Currently not implemented (uses single user-level buffer).

## Recurring Appointments

When creating a series (e.g., weekly for 12 weeks):

```csharp
for (var i = 0; i < seriesCount; i++)
{
    var appointmentStartTime = baseStartTime.AddDays(i * intervalDays);
    try
    {
        await CreateAsync(appointmentDto, userId);
    }
    catch (SchedulingConflictException ex)
    {
        skippedReasons.Add($"{appointmentStartTime:g}: {ex.Message}");
    }
}
```

**Behavior:**

- Each appointment in series checked independently
- If appointment N conflicts, it's skipped and logged
- Remaining series continues
- User is notified which appointments were skipped and why

**Example Output:**

```
Successfully created 10 recurring appointments.
Skipped 2 due to conflicts:
  2026-03-29 2:00 PM: Buffer time violation with appointment 42
  2026-04-19 2:00 PM: Overlapping appointment with John Smith
```

## Cross-Timezone Considerations

All overlap checks happen in UTC (see ADR-0003 for timezone strategy).

**Example:** Practitioner in America/Toronto receives appointment at 2:00 PM Toronto time:

1. UI calls `TimeZoneService.ToUtc()` → converts to 6:00 PM UTC
2. `CreateAsync()` receives 6:00 PM UTC
3. `CheckOverlapAsync()` checks against existing appointments (also in UTC)
4. Buffer expansion uses UTC math
5. No timezone conversion issues

## Error Handling

When conflict detected, throw `SchedulingConflictException` with context:

```csharp
public class SchedulingConflictException : Exception
{
    public string Reason { get; }  // "Overlapping appointment" or "Buffer time violation"
    public int ConflictAppointmentId { get; }
    public DateTime ConflictStartTime { get; }
    public DateTime ConflictEndTime { get; }
    public string ConflictClientName { get; }
}
```

**UI Displays:**

```
Cannot schedule appointment: Buffer time violation
Conflicts with: John Smith on Saturday, March 22, 2:00 PM
```

## Testing Strategy

### Unit Tests

```csharp
[Fact]
public async Task CheckOverlapAsync_WithDirectOverlap_ThrowsException()
{
    // Arrange: Existing appointment 2:00 PM - 2:30 PM
    // Act: Create new appointment 2:15 PM - 2:45 PM
    // Assert: SchedulingConflictException thrown with "Overlapping appointment"
}

[Fact]
public async Task CheckOverlapAsync_WithBufferViolation_ThrowsException()
{
    // Arrange: Existing appointment 2:00 PM - 2:30 PM, buffer = 15 min
    // Act: Create new appointment 2:31 PM - 3:00 PM (1 min into buffer)
    // Assert: SchedulingConflictException thrown with "Buffer time violation"
}

[Fact]
public async Task CheckOverlapAsync_WithCancelledAppointment_Succeeds()
{
    // Arrange: Existing cancelled appointment 2:00 PM - 2:30 PM
    // Act: Create new appointment 2:15 PM - 2:45 PM
    // Assert: No exception (cancelled doesn't block)
}

[Fact]
public async Task CheckOverlapAsync_WithSeparateSlots_Succeeds()
{
    // Arrange: Existing appointment 2:00 PM - 2:30 PM, buffer = 15 min
    // Act: Create new appointment 2:46 PM - 3:15 PM (1 min after buffer)
    // Assert: No exception
}
```

### Edge Cases

- **DST transition:** Appointment crossing DST boundary (UTC handles this)
- **Midnight boundary:** Appointment 11:45 PM - 12:15 AM (next day)
- **Daylight hours only:** Availability window enforcement (separate from overlap)
- **Zero-duration appointments:** (Shouldn't occur, but gracefully handled)
- **Very large buffer:** 8-hour buffer blocks entire work day

## Consequences

### Positive

- **Simple logic** — interval overlap is well-understood, easy to test
- **Correct** — handles all edge cases (touching boundaries, partial overlap, full overlap)
- **Performant** — O(n) query scales to hundreds of appointments
- **Flexible** — buffer time configurable per practitioner
- **Clear errors** — distinguishes direct overlap from buffer violation

### Negative

- **No lookahead** — if practitioner is unavailable tomorrow, system doesn't know (handled by availability service separately)
- **No weighted conflicts** — all conflicts treated equally (no "soft reservation")
- **Recurring complexity** — series with gaps hard to track

### Mitigations

- **Separate concern:** Availability windows managed by `IAvailabilityService` (see ADR-0003)
- **Documentation:** Clear error messages guide user to reschedule
- **Series feedback:** Show skipped appointments in UI with reasons

## Related ADRs

- ADR-0003: Timezone Handling — overlap checks use UTC
- ADR-0004: Status Transitions — cancelled appointments excluded from overlap check

## References

- [Interval Scheduling Problem](https://en.wikipedia.org/wiki/Interval_scheduling)
- [Calendar Systems and Double-Booking Prevention](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6259852/)
- [PostgreSQL Range Types for Scheduling](https://www.postgresql.org/docs/current/rangetypes.html) (potential future optimization)
