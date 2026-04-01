using FluentAssertions;
using NSubstitute;
using Nutrir.Core.DTOs;
using Nutrir.Core.Interfaces;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services;

public class MealPlanPdfServiceTests
{
    private readonly IMealPlanService _mealPlanService = Substitute.For<IMealPlanService>();
    private readonly IAuditLogService _auditLogService = Substitute.For<IAuditLogService>();
    private readonly MealPlanPdfService _sut;

    private const string UserId = "test-user-pdf-001";

    public MealPlanPdfServiceTests()
    {
        _sut = new MealPlanPdfService(_mealPlanService, _auditLogService);
    }

    [Fact]
    public async Task GeneratePdfAsync_WhenMealPlanNotFound_ThrowsKeyNotFoundException()
    {
        _mealPlanService.GetByIdAsync(99).Returns((MealPlanDetailDto?)null);

        var act = () => _sut.GeneratePdfAsync(99, UserId);

        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*99*");
    }

    [Fact]
    public async Task GeneratePdfAsync_WhenMealPlanNotFound_DoesNotLogAudit()
    {
        _mealPlanService.GetByIdAsync(99).Returns((MealPlanDetailDto?)null);

        try { await _sut.GeneratePdfAsync(99, UserId); } catch { }

        await _auditLogService.DidNotReceive().LogAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task GeneratePdfAsync_FetchesMealPlanById()
    {
        _mealPlanService.GetByIdAsync(42).Returns((MealPlanDetailDto?)null);

        try { await _sut.GeneratePdfAsync(42, UserId); } catch { }

        await _mealPlanService.Received(1).GetByIdAsync(42);
    }
}
