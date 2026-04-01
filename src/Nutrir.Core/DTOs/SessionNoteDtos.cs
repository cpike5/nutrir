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
    string? MeasurementsTaken,
    string? PlanAdjustments,
    string? FollowUpActions,
    string? PractitionerAssessment,
    string? ContextualFactors,
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
    SessionType? SessionType,
    string? Notes,
    int? AdherenceScore,
    string? MeasurementsTaken,
    string? PlanAdjustments,
    string? FollowUpActions,
    string? PractitionerAssessment,
    string? ContextualFactors);
