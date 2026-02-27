using Nutrir.Core.Enums;

namespace Nutrir.Core.DTOs;

public record AppointmentListQuery(
    int Page = 1,
    int PageSize = 25,
    string? SortColumn = null,
    SortDirection SortDirection = SortDirection.None,
    DateTime? From = null,
    DateTime? To = null,
    AppointmentStatus? StatusFilter = null);
