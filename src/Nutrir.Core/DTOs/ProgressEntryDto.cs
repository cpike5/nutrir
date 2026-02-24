using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record CreateProgressEntryDto(
    int ClientId,
    DateOnly EntryDate,
    string? Notes,
    List<CreateProgressMeasurementDto> Measurements);

public record CreateProgressMeasurementDto(
    MetricType MetricType,
    string? CustomMetricName,
    decimal Value,
    string? Unit);

public record UpdateProgressEntryDto(
    DateOnly EntryDate,
    string? Notes,
    List<CreateProgressMeasurementDto> Measurements);

public record ProgressEntrySummaryDto(
    int Id,
    int ClientId,
    DateOnly EntryDate,
    int MeasurementCount,
    string? NotePreview,
    DateTime CreatedAt);

public record ProgressEntryDetailDto(
    int Id,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    DateOnly EntryDate,
    string? Notes,
    List<ProgressMeasurementDto> Measurements,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public record ProgressMeasurementDto(
    int Id,
    MetricType MetricType,
    string? CustomMetricName,
    decimal Value,
    string? Unit);
