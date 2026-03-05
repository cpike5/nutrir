using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class CalendarService : ICalendarService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<CalendarService> _logger;

    public CalendarService(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<CalendarService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<List<CalendarAppointmentDto>> GetAppointmentsByDateRangeAsync(DateTime start, DateTime end)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        // Max appointment is 90 minutes; over-fetch by that buffer then filter in memory
        // to avoid EF Core translation issues with AddMinutes on column values
        var bufferStart = start.AddMinutes(-90);

        var candidates = await db.Appointments
            .Where(a => !a.IsDeleted
                && a.StartTime < end
                && a.StartTime > bufferStart)
            .Join(db.Clients,
                a => a.ClientId,
                c => c.Id,
                (a, c) => new
                {
                    a.Id,
                    ClientName = c.FirstName + " " + c.LastName,
                    a.StartTime,
                    a.DurationMinutes,
                    a.Type,
                    a.Status
                })
            .ToListAsync();

        return candidates
            .Where(a => a.StartTime.AddMinutes(a.DurationMinutes) > start)
            .Select(a => new CalendarAppointmentDto(
                a.Id,
                a.ClientName,
                a.StartTime,
                a.StartTime.AddMinutes(a.DurationMinutes),
                a.Type,
                a.Status))
            .ToList();
    }
}
