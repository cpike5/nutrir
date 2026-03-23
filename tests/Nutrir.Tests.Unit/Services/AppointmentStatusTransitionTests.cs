using FluentAssertions;
using Nutrir.Core.Enums;
using Nutrir.Core.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class AppointmentStatusTransitionTests
{
    // ---------------------------------------------------------------------------
    // IsValidTransition — Scheduled transitions
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsValidTransition_Scheduled_To_Confirmed_ReturnsTrue()
    {
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Scheduled, AppointmentStatus.Confirmed);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_Scheduled_To_Cancelled_ReturnsTrue()
    {
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Scheduled, AppointmentStatus.Cancelled);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_Scheduled_To_LateCancellation_ReturnsTrue()
    {
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Scheduled, AppointmentStatus.LateCancellation);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_Scheduled_To_Completed_ReturnsFalse()
    {
        // Cannot skip Confirmed — must go Scheduled → Confirmed → Completed
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Scheduled, AppointmentStatus.Completed);

        result.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // IsValidTransition — Confirmed transitions
    // ---------------------------------------------------------------------------

    [Fact]
    public void IsValidTransition_Confirmed_To_Completed_ReturnsTrue()
    {
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Confirmed, AppointmentStatus.Completed);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidTransition_Confirmed_To_NoShow_ReturnsTrue()
    {
        var result = AppointmentStatusTransitions.IsValidTransition(
            AppointmentStatus.Confirmed, AppointmentStatus.NoShow);

        result.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // IsValidTransition — Terminal states (Completed, NoShow, Cancelled, LateCancellation)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.LateCancellation)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void IsValidTransition_Completed_To_Any_ReturnsFalse(AppointmentStatus to)
    {
        var result = AppointmentStatusTransitions.IsValidTransition(AppointmentStatus.Completed, to);

        result.Should().BeFalse(
            because: "Completed is a terminal state and no transitions should be allowed from it");
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.LateCancellation)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void IsValidTransition_NoShow_To_Any_ReturnsFalse(AppointmentStatus to)
    {
        var result = AppointmentStatusTransitions.IsValidTransition(AppointmentStatus.NoShow, to);

        result.Should().BeFalse(
            because: "NoShow is a terminal state and no transitions should be allowed from it");
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.LateCancellation)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void IsValidTransition_Cancelled_To_Any_ReturnsFalse(AppointmentStatus to)
    {
        var result = AppointmentStatusTransitions.IsValidTransition(AppointmentStatus.Cancelled, to);

        result.Should().BeFalse(
            because: "Cancelled is a terminal state and no transitions should be allowed from it");
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    [InlineData(AppointmentStatus.LateCancellation)]
    [InlineData(AppointmentStatus.Cancelled)]
    public void IsValidTransition_LateCancellation_To_Any_ReturnsFalse(AppointmentStatus to)
    {
        var result = AppointmentStatusTransitions.IsValidTransition(AppointmentStatus.LateCancellation, to);

        result.Should().BeFalse(
            because: "LateCancellation is a terminal state and no transitions should be allowed from it");
    }

    // ---------------------------------------------------------------------------
    // GetAllowedTransitions
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetAllowedTransitions_Scheduled_ReturnsThreeOptions()
    {
        var allowed = AppointmentStatusTransitions.GetAllowedTransitions(AppointmentStatus.Scheduled);

        allowed.Should().HaveCount(3);
        allowed.Should().Contain(AppointmentStatus.Confirmed);
        allowed.Should().Contain(AppointmentStatus.Cancelled);
        allowed.Should().Contain(AppointmentStatus.LateCancellation);
    }

    [Fact]
    public void GetAllowedTransitions_Completed_ReturnsEmpty()
    {
        var allowed = AppointmentStatusTransitions.GetAllowedTransitions(AppointmentStatus.Completed);

        allowed.Should().BeEmpty(
            because: "Completed is a terminal state with no valid forward transitions");
    }
}
