using Nutrir.Core.Enums;
using Nutrir.Core.Interfaces;

namespace Nutrir.Infrastructure.Services;

public class ReminderEmailBuilder : IReminderEmailBuilder
{
    private static readonly TimeZoneInfo TorontoTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");

    public (string Subject, string HtmlBody) BuildReminderEmail(string clientFirstName, DateTime appointmentTimeUtc, ReminderType type)
    {
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(appointmentTimeUtc, TorontoTimeZone);
        var dateStr = localTime.ToString("dddd, MMMM d, yyyy");
        var timeStr = localTime.ToString("h:mm tt");

        var timeframeText = type == ReminderType.FortyEightHour ? "in 2 days" : "tomorrow";

        var html = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8" /></head>
            <body style="margin:0;padding:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background-color:#f9fafb;">
                <div style="max-width:560px;margin:32px auto;background:#ffffff;border-radius:8px;border:1px solid #e5e7eb;overflow:hidden;">
                    <div style="background:#16a34a;padding:24px 32px;">
                        <h1 style="margin:0;color:#ffffff;font-size:20px;font-weight:600;">Nutrir</h1>
                    </div>
                    <div style="padding:32px;">
                        <p style="margin:0 0 16px;font-size:16px;color:#111827;">Hi {clientFirstName},</p>
                        <p style="margin:0 0 24px;font-size:16px;color:#111827;">
                            This is a friendly reminder that you have an appointment {timeframeText}:
                        </p>
                        <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:20px;margin:0 0 24px;">
                            <p style="margin:0 0 8px;font-size:14px;color:#15803d;font-weight:600;">Appointment Details</p>
                            <p style="margin:0 0 4px;font-size:16px;color:#111827;font-weight:500;">{dateStr}</p>
                            <p style="margin:0;font-size:16px;color:#111827;">{timeStr}</p>
                        </div>
                        <p style="margin:0 0 8px;font-size:14px;color:#6b7280;">
                            If you need to reschedule or cancel, please contact us as soon as possible.
                        </p>
                        <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;" />
                        <p style="margin:0;font-size:12px;color:#9ca3af;">
                            You are receiving this because you opted in to appointment reminders.
                            To stop receiving these emails, please ask your practitioner to update your preferences.
                        </p>
                    </div>
                </div>
            </body>
            </html>
            """;

        return ("Appointment Reminder", html);
    }
}
