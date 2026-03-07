using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record IntakeFormDto(
    int Id,
    int? ClientId,
    int? AppointmentId,
    string Token,
    IntakeFormStatus Status,
    string ClientEmail,
    DateTime ExpiresAt,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    string? ReviewedByUserId,
    string CreatedByUserId,
    DateTime CreatedAt,
    List<IntakeFormResponseDto> Responses);

public record IntakeFormResponseDto(
    string SectionKey,
    string FieldKey,
    string Value);

public record IntakeFormListDto(
    int Id,
    int? ClientId,
    string? ClientName,
    int? AppointmentId,
    IntakeFormStatus Status,
    string ClientEmail,
    DateTime ExpiresAt,
    DateTime? SubmittedAt,
    DateTime? ReviewedAt,
    DateTime CreatedAt);
