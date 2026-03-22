using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ReminderBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderBackgroundService> _logger;

    public ReminderBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<ReminderBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Appointment reminder service started");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        // Run immediately on startup, then every 15 minutes
        do
        {
            try
            {
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error in reminder processing tick");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("Appointment reminder service stopped");
    }

    private async Task ProcessRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();
        var emailBuilder = scope.ServiceProvider.GetRequiredService<IReminderEmailBuilder>();
        var auditLogService = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var cutoff48h = now.AddHours(48);

        // Find eligible appointments: upcoming within 48h, with opted-in clients
        var appointments = await db.Appointments
            .Where(a => (a.Status == AppointmentStatus.Scheduled || a.Status == AppointmentStatus.Confirmed)
                && a.StartTime > now
                && a.StartTime <= cutoff48h)
            .Join(db.Clients.Where(c => c.EmailRemindersEnabled && c.ConsentGiven && c.Email != null),
                a => a.ClientId,
                c => c.Id,
                (a, c) => new { Appointment = a, Client = c })
            .ToListAsync(ct);

        if (appointments.Count == 0) return;

        _logger.LogInformation("Processing {Count} eligible appointments for reminders", appointments.Count);

        foreach (var item in appointments)
        {
            try
            {
                await ProcessAppointmentRemindersAsync(
                    db, emailService, emailBuilder, auditLogService,
                    item.Appointment, item.Client, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to process reminders for appointment {AppointmentId}",
                    item.Appointment.Id);
            }
        }
    }

    private async Task ProcessAppointmentRemindersAsync(
        AppDbContext db,
        IEmailService emailService,
        IReminderEmailBuilder emailBuilder,
        IAuditLogService auditLogService,
        Appointment appointment,
        Client client,
        DateTime now,
        CancellationToken ct)
    {
        var hoursUntil = (appointment.StartTime - now).TotalHours;

        // Determine which reminder types are eligible
        var eligibleTypes = new List<ReminderType>();

        // 48h reminder: appointment is 24-48h away, and was created before the 48h window
        if (hoursUntil <= 48 && hoursUntil > 24 && appointment.CreatedAt < appointment.StartTime.AddHours(-48))
        {
            eligibleTypes.Add(ReminderType.FortyEightHour);
        }

        // 24h reminder: appointment is 0-24h away, and was created before the 24h window
        if (hoursUntil <= 24 && appointment.CreatedAt < appointment.StartTime.AddHours(-24))
        {
            eligibleTypes.Add(ReminderType.TwentyFourHour);
        }

        foreach (var reminderType in eligibleTypes)
        {
            // Idempotency check: skip if already sent for this (appointment, type, scheduledFor)
            var alreadySent = await db.AppointmentReminders
                .IgnoreQueryFilters()
                .AnyAsync(r =>
                    r.AppointmentId == appointment.Id
                    && r.ReminderType == reminderType
                    && r.ScheduledFor == appointment.StartTime,
                    ct);

            if (alreadySent) continue;

            await SendReminderAsync(db, emailService, emailBuilder, auditLogService,
                appointment, client, reminderType, ct);
        }
    }

    private async Task SendReminderAsync(
        AppDbContext db,
        IEmailService emailService,
        IReminderEmailBuilder emailBuilder,
        IAuditLogService auditLogService,
        Appointment appointment,
        Client client,
        ReminderType reminderType,
        CancellationToken ct)
    {
        var (subject, htmlBody) = emailBuilder.BuildReminderEmail(
            client.FirstName, appointment.StartTime, reminderType);

        var reminder = new AppointmentReminder
        {
            AppointmentId = appointment.Id,
            ReminderType = reminderType,
            ScheduledFor = appointment.StartTime,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await emailService.SendEmailAsync(client.Email!, client.FirstName, subject, htmlBody, ct);

            reminder.Status = ReminderStatus.Sent;
            reminder.SentAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Sent {ReminderType} reminder for appointment {AppointmentId}, client {ClientId}",
                reminderType, appointment.Id, client.Id);
        }
        catch (Exception ex)
        {
            reminder.Status = ReminderStatus.Failed;
            reminder.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

            _logger.LogError(ex,
                "Failed to send {ReminderType} reminder for appointment {AppointmentId}, client {ClientId}",
                reminderType, appointment.Id, client.Id);
        }

        db.AppointmentReminders.Add(reminder);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist {ReminderType} reminder record for appointment {AppointmentId}",
                reminderType, appointment.Id);
            return;
        }

        // Audit log every send attempt
        await auditLogService.LogAsync(
            "System",
            reminder.Status == ReminderStatus.Sent ? "ReminderSent" : "ReminderFailed",
            "Appointment",
            appointment.Id.ToString(),
            $"{reminderType} reminder for client {client.Id}: {reminder.Status}"
                + (reminder.FailureReason is not null ? $" — {reminder.FailureReason}" : ""));
    }
}
