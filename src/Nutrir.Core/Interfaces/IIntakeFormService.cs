using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IIntakeFormService
{
    Task<IntakeFormDto> CreateFormAsync(string clientEmail, int? appointmentId, int? clientId, string createdByUserId);

    Task<IntakeFormDto?> GetByTokenAsync(string token);

    Task<IntakeFormDto?> GetByIdAsync(int formId);

    Task<IntakeFormListDto?> GetByAppointmentIdAsync(int appointmentId);

    Task<List<IntakeFormListDto>> ListFormsAsync(IntakeFormStatus? statusFilter = null);

    Task<(bool Success, string? Error)> SubmitFormAsync(string token, List<IntakeFormResponseDto> responses);

    Task<(bool Success, int? ClientId, string? Error)> ReviewFormAsync(int formId, string reviewedByUserId);
}
