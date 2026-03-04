# Practice Reports Specification

## Overview

Monthly Practice Reports provide practitioners with summary metrics and trends for their practice. The reports page displays key performance indicators (KPIs), trend charts, and appointment breakdowns for configurable time periods.

## Metrics

| Metric | Calculation |
|--------|------------|
| Total Visits | Count of appointments with `Status == Completed` in period |
| New Clients | Count of clients with `CreatedAt` within period |
| Returning Clients | Clients with a completed appointment in period who were created *before* the period start |
| No-Show Count | Count of appointments with `Status == NoShow` in period |
| No-Show Rate | `NoShowCount / TotalScheduledAppointments * 100` (where scheduled = all non-cancelled) |
| Cancellation Count | Count of appointments with `Status == Cancelled` or `Status == LateCancellation` in period |
| Cancellation Rate | `CancellationCount / TotalAppointments * 100` |
| Active Clients | Distinct clients with at least one appointment (any status except `Cancelled`) in period |
| Appointments by Type | Breakdown count by `AppointmentType` (`InitialConsultation`, `FollowUp`, `CheckIn`) |

## Period Options

| Period | Date Range |
|--------|-----------|
| This Week | Monday of current week through Sunday |
| This Month | First day of current month through last day |
| This Quarter | First day of current quarter through last day |
| Custom Range | User-selected start and end dates |

## Trend Data

Trend data groups metrics into time buckets for charting:

| Period Length | Bucket Size |
|--------------|-------------|
| Up to 14 days | Daily |
| 15 to 90 days | Weekly |
| Over 90 days | Monthly |

Each bucket contains: visits, no-shows, and cancellations.

## UI Layout

1. **Header** with breadcrumb and page title
2. **Period selector** buttons (This Week / This Month / This Quarter / Custom)
3. **Custom date range** inputs (shown when Custom is selected)
4. **KPI metric cards** row: Total Visits, New Clients, No-Show Rate, Cancellation Rate
5. **Trend chart** — grouped bar chart (visits, no-shows, cancellations over time)
6. **Appointments by type** table breakdown

## Data Sources

- **Appointments**: `Appointment` entity — `Status`, `Type`, `StartTime`, `ClientId`
- **Clients**: `Client` entity — `CreatedAt`, `Id`

## Extensibility

- Per-practitioner filtering planned as future enhancement (filter by `NutritionistId`)
- Additional metrics (revenue, retention rate) may be added in future milestones
