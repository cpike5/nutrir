using FluentAssertions;
using Nutrir.Core.Enums;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class ReminderEmailBuilderTests
{
    private readonly ReminderEmailBuilder _sut = new();

    [Fact]
    public void BuildReminderEmail_ReturnsAppointmentReminderSubject()
    {
        var (subject, _) = _sut.BuildReminderEmail("Alice", DateTime.UtcNow.AddDays(1), ReminderType.TwentyFourHour);

        subject.Should().Be("Appointment Reminder");
    }

    [Fact]
    public void BuildReminderEmail_HtmlContainsClientName()
    {
        var (_, html) = _sut.BuildReminderEmail("Alice", DateTime.UtcNow.AddDays(1), ReminderType.TwentyFourHour);

        html.Should().Contain("Hi Alice,");
    }

    [Fact]
    public void BuildReminderEmail_FortyEightHour_ContainsInTwoDays()
    {
        var (_, html) = _sut.BuildReminderEmail("Bob", DateTime.UtcNow.AddDays(2), ReminderType.FortyEightHour);

        html.Should().Contain("in 2 days");
    }

    [Fact]
    public void BuildReminderEmail_TwentyFourHour_ContainsTomorrow()
    {
        var (_, html) = _sut.BuildReminderEmail("Carol", DateTime.UtcNow.AddDays(1), ReminderType.TwentyFourHour);

        html.Should().Contain("tomorrow");
    }

    [Fact]
    public void BuildReminderEmail_HtmlContainsFormattedDateAndTime()
    {
        // Use a known UTC time so we can predict the Toronto local conversion
        var utcTime = new DateTime(2026, 6, 15, 18, 30, 0, DateTimeKind.Utc);
        var torontoTz = TimeZoneInfo.FindSystemTimeZoneById("America/Toronto");
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, torontoTz);
        var expectedDate = localTime.ToString("dddd, MMMM d, yyyy");
        var expectedTime = localTime.ToString("h:mm tt");

        var (_, html) = _sut.BuildReminderEmail("Dave", utcTime, ReminderType.TwentyFourHour);

        html.Should().Contain(expectedDate);
        html.Should().Contain(expectedTime);
    }

    [Fact]
    public void BuildReminderEmail_ReturnsValidHtml()
    {
        var (_, html) = _sut.BuildReminderEmail("Eve", DateTime.UtcNow.AddDays(1), ReminderType.TwentyFourHour);

        html.Should().Contain("<!DOCTYPE html>");
        html.Should().Contain("</html>");
        html.Should().Contain("Nutrir");
    }
}
