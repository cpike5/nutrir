namespace Nutrir.Core.Interfaces;

public interface IReportService
{
    Task<Core.DTOs.PracticeSummaryDto> GetPracticeSummaryAsync(DateTime startDate, DateTime endDate);
}
