using FluentAssertions;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class MaintenanceServiceTests
{
    private readonly MaintenanceService _sut = new();

    [Fact]
    public void GetState_Initially_ReturnsDisabled()
    {
        var state = _sut.GetState();

        state.IsEnabled.Should().BeFalse();
        state.StartedAt.Should().BeNull();
        state.EstimatedEndAt.Should().BeNull();
        state.Message.Should().BeNull();
        state.EnabledBy.Should().BeNull();
    }

    [Fact]
    public void Enable_SetsIsEnabledAndStartedAt()
    {
        _sut.Enable();

        var state = _sut.GetState();
        state.IsEnabled.Should().BeTrue();
        state.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Enable_WithMessage_SetsMessage()
    {
        _sut.Enable(message: "Upgrading database");

        var state = _sut.GetState();
        state.Message.Should().Be("Upgrading database");
    }

    [Fact]
    public void Enable_WithEstimatedMinutes_SetsEstimatedEndAt()
    {
        _sut.Enable(estimatedMinutes: 30);

        var state = _sut.GetState();
        state.EstimatedEndAt.Should().NotBeNull();
        state.EstimatedEndAt.Should().BeCloseTo(DateTime.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Enable_WithoutEstimatedMinutes_LeavesEstimatedEndAtNull()
    {
        _sut.Enable();

        var state = _sut.GetState();
        state.EstimatedEndAt.Should().BeNull();
    }

    [Fact]
    public void Enable_WithEnabledBy_SetsEnabledBy()
    {
        _sut.Enable(enabledBy: "admin-user");

        var state = _sut.GetState();
        state.EnabledBy.Should().Be("admin-user");
    }

    [Fact]
    public void Disable_AfterEnable_ResetsToDefault()
    {
        _sut.Enable(message: "Down for maintenance", estimatedMinutes: 60, enabledBy: "admin");
        _sut.Disable();

        var state = _sut.GetState();
        state.IsEnabled.Should().BeFalse();
        state.StartedAt.Should().BeNull();
        state.EstimatedEndAt.Should().BeNull();
        state.Message.Should().BeNull();
        state.EnabledBy.Should().BeNull();
    }

    [Fact]
    public void GetState_ReturnsDefensiveCopy()
    {
        _sut.Enable(message: "Test");

        var state1 = _sut.GetState();
        var state2 = _sut.GetState();

        state1.Should().NotBeSameAs(state2);
    }
}
