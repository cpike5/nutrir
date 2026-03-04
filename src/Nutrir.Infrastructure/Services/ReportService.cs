using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ILogger<ReportService> _logger;

    public ReportService(IDbContextFactory<AppDbContext> dbContextFactory, ILogger<ReportService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task<PracticeSummaryDto> GetPracticeSummaryAsync(DateTime startDate, DateTime endDate)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var appointments = await db.Appointments
            .Where(a => a.StartTime >= startDate && a.StartTime < endDate)
            .Select(a => new AppointmentSlim(a.Status, a.Type, a.StartTime, a.ClientId))
            .ToListAsync();

        var totalVisits = appointments.Count(a => a.Status == AppointmentStatus.Completed);

        var newClients = await db.Clients
            .CountAsync(c => c.CreatedAt >= startDate && c.CreatedAt < endDate);

        var completedClientIds = appointments
            .Where(a => a.Status == AppointmentStatus.Completed)
            .Select(a => a.ClientId)
            .Distinct()
            .ToList();

        var returningClients = completedClientIds.Count > 0
            ? await db.Clients
                .CountAsync(c => completedClientIds.Contains(c.Id) && c.CreatedAt < startDate)
            : 0;

        var noShowCount = appointments.Count(a => a.Status == AppointmentStatus.NoShow);
        var cancellationCount = appointments.Count(a =>
            a.Status == AppointmentStatus.Cancelled || a.Status == AppointmentStatus.LateCancellation);

        var totalAppointments = appointments.Count;
        var noShowRate = totalAppointments > 0
            ? Math.Round((decimal)noShowCount / totalAppointments * 100, 1)
            : 0m;
        var cancellationRate = totalAppointments > 0
            ? Math.Round((decimal)cancellationCount / totalAppointments * 100, 1)
            : 0m;

        var activeClients = appointments
            .Where(a => a.Status != AppointmentStatus.Cancelled)
            .Select(a => a.ClientId)
            .Distinct()
            .Count();

        var appointmentsByType = appointments
            .GroupBy(a => a.Type)
            .Select(g => new AppointmentsByTypeDto(g.Key.ToString(), g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        var trendData = BuildTrendBuckets(appointments, startDate, endDate);

        _logger.LogInformation("Generated practice summary for {Start:d} to {End:d}: {Visits} visits, {New} new clients",
            startDate, endDate, totalVisits, newClients);

        return new PracticeSummaryDto(
            totalVisits, newClients, returningClients,
            noShowCount, noShowRate,
            cancellationCount, cancellationRate,
            activeClients, appointmentsByType, trendData);
    }

    private static bool IsCancellation(AppointmentStatus status) =>
        status == AppointmentStatus.Cancelled || status == AppointmentStatus.LateCancellation;

    private static List<TrendBucketDto> BuildTrendBuckets(
        List<AppointmentSlim> appointments,
        DateTime startDate,
        DateTime endDate)
    {
        var totalDays = (endDate - startDate).TotalDays;
        var buckets = new List<TrendBucketDto>();

        if (totalDays <= 14)
        {
            for (var date = startDate.Date; date < endDate.Date; date = date.AddDays(1))
            {
                var nextDate = date.AddDays(1);
                var bucket = appointments.Where(a => a.StartTime >= date && a.StartTime < nextDate).ToList();
                buckets.Add(MakeBucket(date, date.ToString("MMM d"), bucket));
            }
        }
        else if (totalDays <= 90)
        {
            var weekStart = startDate.Date;
            while (weekStart < endDate)
            {
                var weekEnd = weekStart.AddDays(7) < endDate ? weekStart.AddDays(7) : endDate;
                var bucket = appointments.Where(a => a.StartTime >= weekStart && a.StartTime < weekEnd).ToList();
                buckets.Add(MakeBucket(weekStart, $"W{System.Globalization.ISOWeek.GetWeekOfYear(weekStart)}", bucket));
                weekStart = weekEnd;
            }
        }
        else
        {
            var monthStart = new DateTime(startDate.Year, startDate.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (monthStart < endDate)
            {
                var monthEnd = monthStart.AddMonths(1);
                var bucketStart = monthStart < startDate ? startDate : monthStart;
                var bucketEnd = monthEnd > endDate ? endDate : monthEnd;
                var bucket = appointments.Where(a => a.StartTime >= bucketStart && a.StartTime < bucketEnd).ToList();
                buckets.Add(MakeBucket(monthStart, monthStart.ToString("MMM yyyy"), bucket));
                monthStart = monthEnd;
            }
        }

        return buckets;
    }

    private static TrendBucketDto MakeBucket(DateTime periodStart, string label, List<AppointmentSlim> bucket) =>
        new(periodStart, label,
            bucket.Count(a => a.Status == AppointmentStatus.Completed),
            bucket.Count(a => a.Status == AppointmentStatus.NoShow),
            bucket.Count(a => IsCancellation(a.Status)));

    private record AppointmentSlim(AppointmentStatus Status, AppointmentType Type, DateTime StartTime, int ClientId);
}
