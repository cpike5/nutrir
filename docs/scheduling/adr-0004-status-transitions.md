# ADR-0004: Appointment Status Transition State Machine

**Status:** Accepted
**Date:** 2026-03-22
**Domain:** Scheduling
**Participants:** Clinical requirements, architecture team

## Context

Appointments move through multiple states during their lifecycle: scheduled, confirmed, completed, no-show, cancelled, etc. Different transitions are valid depending on business rules:

- A scheduled appointment can be confirmed by client/practitioner
- A scheduled appointment can be cancelled in advance
- A confirmed appointment can be marked as no-show if client didn't attend
- A completed appointment is terminal (cannot be reverted)

Without a formal state machine, it's easy to create invalid transitions (e.g., completed → cancelled).

### Requirements

1. **Clinical accuracy** — appointment status must reflect reality (client attended, no-showed, was cancelled)
2. **Audit trail** — all status changes logged with timestamp and reason
3. **Business logic** — only valid transitions allowed
4. **UI constraints** — UI only shows valid action buttons per state
5. **Recurring appointments** — handle status changes gracefully in recurring appointment series

## Decision

**Implement a state machine with explicit transition rules. Enforce at service layer. Validate in UI.**

## Appointment Status Enum

```csharp
public enum AppointmentStatus
{
    Scheduled = 0,        // Booked but not yet confirmed
    Confirmed = 1,        // Client/practitioner has confirmed attendance
    Completed = 2,        // Session took place
    NoShow = 3,           // Client did not attend
    LateCancellation = 4, // Cancelled within 24 hours of session
    Cancelled = 5         // Cancelled with advance notice
}
```

## State Transition Rules

### Valid Transitions

| From         | To                            | Conditions | Notes |
|--------------|-------------------------------|-----------|-------|
| Scheduled    | Confirmed                     | Any time  | Client or practitioner confirms attendance |
| Scheduled    | Cancelled                     | >24h before start | Advance cancellation |
| Scheduled    | LateCancellation              | <24h before start | Last-minute cancellation |
| Confirmed    | Completed                     | After session end time | Session occurred |
| Confirmed    | NoShow                        | After session end time | Client didn't attend |
| Confirmed    | Cancelled                     | >24h before start | Advance cancellation of confirmed appointment |
| Confirmed    | LateCancellation              | <24h before start | Last-minute cancellation |
| Cancelled    | Cancelled                     | Any time  | No-op (idempotent) |
| LateCancellation | LateCancellation            | Any time  | No-op (idempotent) |
| Completed    | Completed                     | Any time  | No-op (idempotent) |
| NoShow       | NoShow                        | Any time  | No-op (idempotent) |

### Invalid Transitions (Rejected)

| From         | To                  | Reason |
|--------------|---------------------|--------|
| Any          | Scheduled           | Cannot un-schedule |
| Completed    | Any other state     | Terminal state |
| NoShow       | Confirmed/Completed | Session already missed |
| Cancelled    | Scheduled           | Cannot resurrect |
| LateCancellation | Scheduled       | Cannot resurrect |
| Scheduled    | Completed           | Cannot skip confirmation |
| Confirmed    | Scheduled           | Cannot unconfirm |

### Terminal States

Once an appointment reaches one of these states, no further transitions are allowed (except idempotent no-op):

- **Completed** — session took place, is now historical
- **NoShow** — client missed appointment, is historical
- **Cancelled** — cancelled, is historical
- **LateCancellation** — cancelled, is historical

## Implementation

### Service Layer Validation

`AppointmentService.UpdateStatusAsync()` enforces rules:

```csharp
public async Task<AppointmentDto> UpdateStatusAsync(
    int id,
    AppointmentStatus newStatus,
    string userId,
    string? cancellationReason = null)
{
    var entity = await _dbContext.Appointments.FindAsync(id);
    if (entity is null)
        throw new EntityNotFoundException($"Appointment {id} not found");

    // Validate transition is allowed
    if (!IsValidTransition(entity.Status, newStatus))
        throw new InvalidOperationException(
            $"Cannot transition from {entity.Status} to {newStatus}");

    // Apply business logic based on transition
    switch (newStatus)
    {
        case AppointmentStatus.Cancelled:
        case AppointmentStatus.LateCancellation:
            if (string.IsNullOrWhiteSpace(cancellationReason))
                throw new ArgumentException("Cancellation reason required");
            entity.CancellationReason = cancellationReason;
            entity.CancelledAt = DateTime.UtcNow;
            break;
    }

    entity.Status = newStatus;
    entity.UpdatedAt = DateTime.UtcNow;
    await _dbContext.SaveChangesAsync();

    // Log state change
    await _auditLogService.LogAsync(
        userId,
        "AppointmentStatusChanged",
        "Appointment",
        entity.Id.ToString(),
        $"Status changed from {entity.Status} to {newStatus}");

    return MapToDto(entity);
}

private static bool IsValidTransition(AppointmentStatus from, AppointmentStatus to)
{
    // Check state machine rules
    return (from, to) switch
    {
        (AppointmentStatus.Scheduled, AppointmentStatus.Confirmed) => true,
        (AppointmentStatus.Scheduled, AppointmentStatus.Cancelled) => true,
        (AppointmentStatus.Scheduled, AppointmentStatus.LateCancellation) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.Completed) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.NoShow) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.Cancelled) => true,
        (AppointmentStatus.Confirmed, AppointmentStatus.LateCancellation) => true,
        // Idempotent transitions
        (s, t) when s == t => true,
        // All others invalid
        _ => false
    };
}
```

### UI Constraints

Blazor components only show buttons for valid transitions.

**Example: AppointmentDetail.razor**

```razor
@if (appointment.Status == AppointmentStatus.Scheduled)
{
    <button @onclick="() => ConfirmAppointment()">Confirm</button>
    <button @onclick="() => CancelAppointment()">Cancel</button>
}
else if (appointment.Status == AppointmentStatus.Confirmed)
{
    <button @onclick="() => MarkCompleted()">Mark Completed</button>
    <button @onclick="() => MarkNoShow()">Mark No-Show</button>
    <button @onclick="() => CancelAppointment()">Cancel</button>
}
else if (appointment.Status == AppointmentStatus.Completed ||
         appointment.Status == AppointmentStatus.NoShow ||
         appointment.Status == AppointmentStatus.Cancelled ||
         appointment.Status == AppointmentStatus.LateCancellation)
{
    <p>Appointment is in terminal state. No further changes allowed.</p>
}
```

## Time-Based Transitions

Some transitions are only valid if appointment timing criteria are met.

### Scheduled → Cancelled vs. LateCancellation

**Rule:** Use appointment time to determine which cancellation type:

```csharp
var hoursUntilStart = (entity.StartTime - DateTime.UtcNow).TotalHours;

if (hoursUntilStart > 24)
{
    // Advance cancellation
    newStatus = AppointmentStatus.Cancelled;
}
else
{
    // Late cancellation (within 24 hours)
    newStatus = AppointmentStatus.LateCancellation;
}
```

### Confirmed → Completed/NoShow Only After Session

**Rule:** Cannot mark completed or no-show until after appointment end time.

```csharp
var appointmentEndTime = entity.StartTime.AddMinutes(entity.DurationMinutes);

if (newStatus == AppointmentStatus.Completed || newStatus == AppointmentStatus.NoShow)
{
    if (DateTime.UtcNow < appointmentEndTime)
        throw new InvalidOperationException(
            "Cannot mark appointment completed/no-show before session end time");
}
```

## Recurring Appointment Series

When updating status of a recurring appointment:

**Single Appointment Update:**
- Update only the specified appointment ID
- Other appointments in series are unaffected

**Series Update (Future Feature):**
- Option to update "this and all future" appointments
- Validates each appointment for valid transition
- Skips appointments that cannot transition
- Logs which appointments were updated and which skipped

**Example:**

```csharp
// User marks a recurring appointment as no-show and wants to cancel remaining series
var updates = await AppointmentService.UpdateStatusSeriesAsync(
    seriesId,
    fromDate: DateTime.UtcNow,
    newStatus: AppointmentStatus.Cancelled,
    userId: currentUser.Id);

// Returns: { UpdatedCount: 5, SkippedCount: 2, SkippedReasons: [...] }
```

## Audit Logging

Every status change is logged with:

- **User ID** — who made the change
- **Previous status** — from state
- **New status** — to state
- **Timestamp** — when change occurred
- **Reason** (for cancellations) — why appointment was cancelled
- **IP address** (from audit context) — where request came from

Example audit log entry:

```
Action: AppointmentStatusChanged
EntityType: Appointment
EntityId: 42
Details: "Status changed from Scheduled to Cancelled. Reason: Client requested cancellation."
Timestamp: 2026-03-22T14:30:00Z
UserId: user-789
Source: api
```

## Consequences

### Positive

- **Correctness** — invalid states impossible
- **Auditability** — all transitions logged
- **UX clarity** — UI shows only available actions
- **Business logic** — rules enforced consistently at service layer

### Negative

- **Rigidity** — special cases require exception handling (e.g., "undo cancellation")
- **Complexity** — state machine adds lines of validation code
- **Testing overhead** — need test cases for all transitions

### Mitigations

- **Whitelist approach** — explicitly define valid transitions (safer than blacklist)
- **Audit trail** — if special case needed (e.g., revert cancellation), create new appointment rather than violating state machine
- **Documentation** — clear state diagram and rules for developers

## State Diagram

```
┌─────────┐
│Scheduled│─────────────────────────────┐
└────┬────┘                             │
     │                                  │
     │ Confirm                          │ Cancel
     ▼                                  ▼
┌─────────┐                      ┌───────────┐
│Confirmed│                      │ Cancelled │ (terminal)
└─┬──┬────┘                      └───────────┘
  │  │
  │  │ Cancel
  │  └─────────────────────┐
  │                        │
  │ Completed/NoShow       ▼
  │                 ┌──────────────────┐
  │                 │LateCancellation  │ (terminal)
  │                 └──────────────────┘
  │
  ├─ Completed (no-op)
  ├─ NoShow (terminal)
  └─ Cancelled (terminal)
```

## Related ADRs

- ADR-0005: Overlap Detection Algorithm — uses appointment status when checking availability

## References

- [State Machine Pattern in C#](https://en.wikipedia.org/wiki/State_pattern)
- [Appointment Lifecycle Best Practices](https://www.ncbi.nlm.nih.gov/pmc/articles/PMC6259852/)
