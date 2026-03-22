namespace Nutrir.Core.DTOs;

public record SessionNoteDto(
    int Id,
    int AppointmentId,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    bool IsDraft,
    string? Notes,
    int? AdherenceScore,
    string? MeasurementsTaken,
    string? PlanAdjustments,
    string? FollowUpActions,
    DateTime? AppointmentDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record SessionNoteSummaryDto(
    int Id,
    int AppointmentId,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    bool IsDraft,
    int? AdherenceScore,
    DateTime? AppointmentDate,
    DateTime CreatedAt);

public record UpdateSessionNoteDto(
    string? Notes,
    int? AdherenceScore,
    string? MeasurementsTaken,
    string? PlanAdjustments,
    string? FollowUpActions);
