using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record GridQuery(
    int Page = 1,
    int PageSize = 25,
    string? SortColumn = null,
    SortDirection SortDirection = SortDirection.None);
