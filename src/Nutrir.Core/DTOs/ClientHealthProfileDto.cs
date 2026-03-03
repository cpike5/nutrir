using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

// --- Allergy DTOs ---

public record ClientAllergyDto(
    int Id,
    int ClientId,
    string Name,
    AllergySeverity Severity,
    AllergyType AllergyType,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateClientAllergyDto(
    int ClientId,
    string Name,
    AllergySeverity Severity,
    AllergyType AllergyType);

public record UpdateClientAllergyDto(
    string Name,
    AllergySeverity Severity,
    AllergyType AllergyType);

// --- Medication DTOs ---

public record ClientMedicationDto(
    int Id,
    int ClientId,
    string Name,
    string? Dosage,
    string? Frequency,
    string? PrescribedFor,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateClientMedicationDto(
    int ClientId,
    string Name,
    string? Dosage,
    string? Frequency,
    string? PrescribedFor);

public record UpdateClientMedicationDto(
    string Name,
    string? Dosage,
    string? Frequency,
    string? PrescribedFor);

// --- Condition DTOs ---

public record ClientConditionDto(
    int Id,
    int ClientId,
    string Name,
    string? Code,
    DateOnly? DiagnosisDate,
    ConditionStatus Status,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateClientConditionDto(
    int ClientId,
    string Name,
    string? Code,
    DateOnly? DiagnosisDate,
    ConditionStatus Status,
    string? Notes);

public record UpdateClientConditionDto(
    string Name,
    string? Code,
    DateOnly? DiagnosisDate,
    ConditionStatus Status,
    string? Notes);

// --- Dietary Restriction DTOs ---

public record ClientDietaryRestrictionDto(
    int Id,
    int ClientId,
    DietaryRestrictionType RestrictionType,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record CreateClientDietaryRestrictionDto(
    int ClientId,
    DietaryRestrictionType RestrictionType,
    string? Notes);

public record UpdateClientDietaryRestrictionDto(
    DietaryRestrictionType RestrictionType,
    string? Notes);
