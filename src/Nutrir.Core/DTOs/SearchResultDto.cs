namespace Nutrir.Core.DTOs;

public record SearchResultDto(
    List<SearchResultGroup> Groups,
    int TotalCount);

public record SearchResultGroup(
    string EntityType,
    List<SearchResultItem> Items,
    int TotalInGroup);

public record SearchResultItem(
    int Id,
    string PrimaryText,
    string? SecondaryText,
    string? StatusLabel,
    string StatusVariant,
    string Url,
    string Initials);
