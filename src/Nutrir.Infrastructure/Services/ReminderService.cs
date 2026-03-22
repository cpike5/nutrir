using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nutrir.Core.DTOs;
using Nutrir.Core.Entities;
using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Data;

namespace Nutrir.Infrastructure.Services;

public class ReminderService : IReminderService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly IEmailService _emailService;
    private readonly IReminderEmailBuilder _emailBuilder;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<ReminderService> _logger;

    public ReminderService(
        IDbContextFactory<AppDbContext> dbContextFactory,
        IEmailService emailService,
        IReminderEmailBuilder emailBuilder,
        IAuditLogService auditLogService,
        ILogger<ReminderService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _emailService = emailService;
        _emailBuilder = emailBuilder;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<List<AppointmentReminderDto>> GetRemindersForAppointmentAsync(int appointmentId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        return await db.AppointmentReminders
            .Where(r => r.AppointmentId == appointmentId)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new AppointmentReminderDto(
                r.Id,
                r.AppointmentId,
                r.ReminderType,
                r.ScheduledFor,
                r.Status,
                r.SentAt,
                r.FailureReason,
                r.CreatedAt))
            .ToListAsync();
    }

    public async Task ResendReminderAsync(int appointmentId, ReminderType type, string userId)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var appointment = await db.Appointments.FindAsync(appointmentId)
            ?? throw new InvalidOperationException($"Appointment {appointmentId} not found.");

        var client = await db.Clients.FindAsync(appointment.ClientId)
            ?? throw new InvalidOperationException($"Client {appointment.ClientId} not found.");

        if (string.IsNullOrEmpty(client.Email))
            throw new InvalidOperationException("Client does not have an email address.");

        if (!client.ConsentGiven || !client.EmailRemindersEnabled)
            throw new InvalidOperationException("Client has not opted in to email reminders.");

        var (subject, htmlBody) = _emailBuilder.BuildReminderEmail(
            client.FirstName, appointment.StartTime, type);

        var reminder = new AppointmentReminder
        {
            AppointmentId = appointmentId,
            ReminderType = type,
            ScheduledFor = appointment.StartTime,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _emailService.SendEmailAsync(client.Email, client.FirstName, subject, htmlBody);

            reminder.Status = ReminderStatus.Sent;
            reminder.SentAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Manually resent {ReminderType} reminder for appointment {AppointmentId}, client {ClientId} by {UserId}",
                type, appointmentId, client.Id, userId);
        }
        catch (Exception ex)
        {
            reminder.Status = ReminderStatus.Failed;
            reminder.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;

            _logger.LogError(ex,
                "Failed to resend {ReminderType} reminder for appointment {AppointmentId}, client {ClientId}",
                type, appointmentId, client.Id);
        }

        db.AppointmentReminders.Add(reminder);
        await db.SaveChangesAsync();

        await _auditLogService.LogAsync(
            userId,
            reminder.Status == ReminderStatus.Sent ? "ReminderResent" : "ReminderResendFailed",
            "Appointment",
            appointmentId.ToString(),
            $"Manual {type} reminder for client {client.Id}: {reminder.Status}");
    }
}
