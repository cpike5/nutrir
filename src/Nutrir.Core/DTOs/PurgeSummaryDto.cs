namespace Nutrir.Core.DTOs;

public record PurgeSummaryDto(
    int ClientId,
    string ClientName,
    DateTime? LastInteractionDate,
    DateTime? RetentionExpiresAt,
    int AppointmentCount,
    int MealPlanCount,
    int ProgressEntryCount,
    int ProgressGoalCount,
    int ConsentEventCount,
    int HealthProfileItemCount,
    int IntakeFormCount,
    int SessionNoteCount);

public record DataPurgeResult(bool Success, string? Error = null);

public record RetentionClientDto(
    int ClientId,
    string FirstName,
    string LastName,
    DateTime? LastInteractionDate,
    DateTime? RetentionExpiresAt,
    int RetentionYears,
    int DaysUntilExpiry);

public record DataPurgeAuditLogDto(
    int Id,
    DateTime PurgedAt,
    string PurgedByUserId,
    string PurgedByName,
    int ClientId,
    string ClientIdentifier,
    string PurgedEntities,
    string Justification);
