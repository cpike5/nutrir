using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record CreateProgressGoalDto(
    int ClientId,
    string Title,
    string? Description,
    GoalType GoalType,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate);

public record UpdateProgressGoalDto(
    string Title,
    string? Description,
    GoalType GoalType,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate);

public record ProgressGoalSummaryDto(
    int Id,
    int ClientId,
    string Title,
    GoalType GoalType,
    GoalStatus Status,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate,
    DateTime CreatedAt);

public record ProgressGoalDetailDto(
    int Id,
    int ClientId,
    string ClientFirstName,
    string ClientLastName,
    string CreatedByUserId,
    string? CreatedByName,
    string Title,
    string? Description,
    GoalType GoalType,
    GoalStatus Status,
    decimal? TargetValue,
    string? TargetUnit,
    DateOnly? TargetDate,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
