using FluentAssertions;
using Nutrir.Core.Enums;
using Nutrir.Core.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AppointmentStatusTransitionsTests
{
    // ---------------------------------------------------------------------------
    // IsValidTransition — valid transitions
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.LateCancellation)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Confirmed, AppointmentStatus.LateCancellation)]
    public void IsValidTransition_WithValidTransition_ReturnsTrue(AppointmentStatus from, AppointmentStatus to)
    {
        AppointmentStatusTransitions.IsValidTransition(from, to).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // IsValidTransition — invalid transitions
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Scheduled, AppointmentStatus.NoShow)]
    public void IsValidTransition_SkippingConfirmed_ReturnsFalse(AppointmentStatus from, AppointmentStatus to)
    {
        AppointmentStatusTransitions.IsValidTransition(from, to).Should().BeFalse();
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.LateCancellation)]
    public void IsValidTransition_FromTerminalState_ReturnsFalseForAll(AppointmentStatus terminalStatus)
    {
        foreach (var target in Enum.GetValues<AppointmentStatus>())
        {
            AppointmentStatusTransitions.IsValidTransition(terminalStatus, target).Should().BeFalse(
                $"terminal status {terminalStatus} should not transition to {target}");
        }
    }

    [Fact]
    public void IsValidTransition_SameStatus_ReturnsFalse()
    {
        AppointmentStatusTransitions.IsValidTransition(AppointmentStatus.Scheduled, AppointmentStatus.Scheduled)
            .Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // GetAllowedTransitions
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetAllowedTransitions_FromScheduled_ReturnsConfirmedCancelledLateCancellation()
    {
        var allowed = AppointmentStatusTransitions.GetAllowedTransitions(AppointmentStatus.Scheduled);

        allowed.Should().BeEquivalentTo([
            AppointmentStatus.Confirmed,
            AppointmentStatus.Cancelled,
            AppointmentStatus.LateCancellation
        ]);
    }

    [Fact]
    public void GetAllowedTransitions_FromConfirmed_ReturnsCompletedNoShowCancelledLateCancellation()
    {
        var allowed = AppointmentStatusTransitions.GetAllowedTransitions(AppointmentStatus.Confirmed);

        allowed.Should().BeEquivalentTo([
            AppointmentStatus.Completed,
            AppointmentStatus.NoShow,
            AppointmentStatus.Cancelled,
            AppointmentStatus.LateCancellation
        ]);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.LateCancellation)]
    public void GetAllowedTransitions_FromTerminalState_ReturnsEmpty(AppointmentStatus terminalStatus)
    {
        var allowed = AppointmentStatusTransitions.GetAllowedTransitions(terminalStatus);

        allowed.Should().BeEmpty();
    }
}
