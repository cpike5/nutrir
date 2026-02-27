using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record ClientListQuery(
    int Page = 1,
    int PageSize = 25,
    string? SortColumn = null,
    SortDirection SortDirection = SortDirection.None,
    string? SearchTerm = null,
    string? ConsentFilter = null);
