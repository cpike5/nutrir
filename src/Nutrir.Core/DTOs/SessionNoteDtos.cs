using Nutrir.Core.Enums;

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
    SessionType? SessionType,
    string? Notes,
    int? AdherenceScore,
    string? PractitionerAssessment,
    string? ContextualFactors,
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
    SessionType? SessionType,
    int? AdherenceScore,
    DateTime? AppointmentDate,
    DateTime CreatedAt);

public record UpdateSessionNoteDto(
    SessionType? SessionType,
    string? Notes,
    int? AdherenceScore,
    string? PractitionerAssessment,
    string? ContextualFactors,
    string? MeasurementsTaken,
    string? PlanAdjustments,
    string? FollowUpActions);
