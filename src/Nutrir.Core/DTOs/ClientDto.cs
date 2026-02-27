namespace Nutrir.Core.DTOs;

public record ClientDto(
    int Id,
    string FirstName,
    string LastName,
    string? Email,
    string? Phone,
    DateOnly? DateOfBirth,
    string PrimaryNutritionistId,
    string? PrimaryNutritionistName,
    bool ConsentGiven,
    DateTime? ConsentTimestamp,
    string? ConsentPolicyVersion,
    string? Notes,
    bool IsDeleted,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    DateTime? DeletedAt,
    DateTime? LastAppointmentDate = null);
