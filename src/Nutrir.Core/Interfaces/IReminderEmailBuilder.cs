using Nutrir.Core.Enums;

namespace Nutrir.Core.Interfaces;

public interface IReminderEmailBuilder
{
    (string Subject, string HtmlBody) BuildReminderEmail(string clientFirstName, DateTime appointmentTimeUtc, ReminderType type);
}
