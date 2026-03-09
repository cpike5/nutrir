using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record ClientDataExportDto(
    ExportMetadataDto ExportMetadata,
    ClientProfileExportDto ClientProfile,
    HealthProfileExportDto HealthProfile,
    List<AppointmentExportDto> Appointments,
    List<MealPlanExportDto> MealPlans,
    List<ProgressGoalExportDto> ProgressGoals,
    List<ProgressEntryExportDto> ProgressEntries,
    List<IntakeFormExportDto> IntakeForms,
    ConsentHistoryExportDto ConsentHistory,
    List<AuditLogExportDto> AuditLog);

public record ExportMetadataDto(
    DateTime ExportDate,
    string ExportVersion,
    string ExportFormat,
    int ClientId,
    string GeneratedByName,
    string PipedaNotice);

public record ClientProfileExportDto(
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth,
    string? Notes,
    bool ConsentGiven,
    DateTime? ConsentTimestamp,
    string? ConsentPolicyVersion,
    string PrimaryNutritionistName,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt);

public record HealthProfileExportDto(
    List<AllergyExportDto> Allergies,
    List<MedicationExportDto> Medications,
    List<ConditionExportDto> Conditions,
    List<DietaryRestrictionExportDto> DietaryRestrictions);

public record AllergyExportDto(string Name, string Severity, string AllergyType, bool IsDeleted, DateTime? DeletedAt);
public record MedicationExportDto(string Name, string? Dosage, string? Frequency, string? PrescribedFor, bool IsDeleted, DateTime? DeletedAt);
public record ConditionExportDto(string Name, string? Code, DateOnly? DiagnosisDate, string Status, string? Notes, bool IsDeleted, DateTime? DeletedAt);
public record DietaryRestrictionExportDto(string RestrictionType, string? Notes, bool IsDeleted, DateTime? DeletedAt);

public record AppointmentExportDto(
    string Type, string Status, DateTime StartTime, int DurationMinutes,
    string Location, string? LocationNotes, string? Notes,
    string NutritionistName, string? CancellationReason, DateTime? CancelledAt,
    bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt);

public record MealPlanExportDto(
    string Title, string? Description, string Status,
    DateOnly? StartDate, DateOnly? EndDate,
    decimal? CalorieTarget, decimal? ProteinTargetG, decimal? CarbsTargetG, decimal? FatTargetG,
    string? Notes, string? Instructions, string CreatedByName,
    List<MealPlanDayExportDto> Days,
    bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt);

public record MealPlanDayExportDto(int DayNumber, string? Label, string? Notes, List<MealSlotExportDto> MealSlots);
public record MealSlotExportDto(string MealType, string? CustomName, string? Notes, List<MealItemExportDto> Items);
public record MealItemExportDto(string FoodName, decimal Quantity, string Unit, decimal CaloriesKcal, decimal ProteinG, decimal CarbsG, decimal FatG, string? Notes);

public record ProgressGoalExportDto(
    string Title, string? Description, string GoalType,
    decimal? TargetValue, string? TargetUnit, DateOnly? TargetDate, string Status,
    string CreatedByName, bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt);

public record ProgressEntryExportDto(
    DateOnly EntryDate, string? Notes, string CreatedByName,
    List<ProgressMeasurementExportDto> Measurements,
    bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt);

public record ProgressMeasurementExportDto(string MetricType, string? CustomMetricName, decimal Value, string? Unit);

public record IntakeFormExportDto(
    string Status, DateTime? SubmittedAt, DateTime? ReviewedAt,
    string? ReviewedByName, string CreatedByName,
    List<IntakeFormResponseExportDto> Responses,
    bool IsDeleted, DateTime? DeletedAt, DateTime CreatedAt);

public record IntakeFormResponseExportDto(string SectionKey, string FieldKey, string Value);

public record ConsentHistoryExportDto(
    List<ConsentEventExportDto> Events,
    List<ConsentFormExportDto> Forms);

public record ConsentEventExportDto(
    string EventType, string ConsentPurpose, string PolicyVersion,
    DateTime Timestamp, string RecordedByName, string? Notes);

public record ConsentFormExportDto(
    string FormVersion, DateTime GeneratedAt, string GeneratedByName,
    string SignatureMethod, bool IsSigned, DateTime? SignedAt,
    string? SignedByName, string? Notes, DateTime CreatedAt);

public record AuditLogExportDto(
    DateTime Timestamp, string Action, string EntityType,
    string? EntityId, string? Details, string Source);
