using FluentAssertions;
using Microsoft.Extensions.Options;
using Nutrir.Infrastructure.Configuration;
using Nutrir.Infrastructure.Services;
using Xunit;

namespace Nutrir.Tests.Unit.Services.Ai;

public class AiRateLimiterTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Creates a limiter with small per-minute and per-day limits suitable for
    /// exhausting in a single test without many repeated calls.
    /// </summary>
    private static AiRateLimiter CreateLimiter(int requestsPerMinute = 3, int requestsPerDay = 5)
    {
        var options = Options.Create(new AiRateLimitOptions
        {
            RequestsPerMinute = requestsPerMinute,
            RequestsPerDay = requestsPerDay,
        });
        return new AiRateLimiter(options);
    }

    // ---------------------------------------------------------------------------
    // Scenario 1 – first request is allowed
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_UnderLimit_AllowsRequest()
    {
        // Arrange
        var sut = CreateLimiter();

        // Act
        var (allowed, _) = sut.CheckAndRecord("user-1");

        // Assert
        allowed.Should().BeTrue();
    }

    // ---------------------------------------------------------------------------
    // Scenario 5 – allowed result carries a null message
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_AllowedResult_HasNullMessage()
    {
        // Arrange
        var sut = CreateLimiter();

        // Act
        var (_, message) = sut.CheckAndRecord("user-1");

        // Assert
        message.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Scenario 2 – per-minute limit is enforced
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_MinuteLimitExceeded_BlocksRequest()
    {
        // Arrange – limit of 3 requests per minute
        var sut = CreateLimiter(requestsPerMinute: 3, requestsPerDay: 10);
        const string userId = "user-minute";

        // Act – exhaust the per-minute quota
        sut.CheckAndRecord(userId);
        sut.CheckAndRecord(userId);
        sut.CheckAndRecord(userId);
        var (allowed, _) = sut.CheckAndRecord(userId);

        // Assert
        allowed.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // Scenario 6 – blocked per-minute message references the limit value
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_MinuteLimitExceeded_HasDescriptiveMessage()
    {
        // Arrange
        const int minuteLimit = 3;
        var sut = CreateLimiter(requestsPerMinute: minuteLimit, requestsPerDay: 10);
        const string userId = "user-minute-msg";

        for (var i = 0; i < minuteLimit; i++)
            sut.CheckAndRecord(userId);

        // Act
        var (_, message) = sut.CheckAndRecord(userId);

        // Assert
        message.Should().NotBeNullOrEmpty();
        message.Should().Contain(minuteLimit.ToString(),
            because: "the error message must include the per-minute limit so the caller can surface it to the user");
    }

    // ---------------------------------------------------------------------------
    // Scenario 3 – per-day limit is enforced
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_DailyLimitExceeded_BlocksRequest()
    {
        // Arrange – minute limit is large so it never triggers; day limit is 3
        var sut = CreateLimiter(requestsPerMinute: 100, requestsPerDay: 3);
        const string userId = "user-day";

        // Act – exhaust the daily quota
        sut.CheckAndRecord(userId);
        sut.CheckAndRecord(userId);
        sut.CheckAndRecord(userId);
        var (allowed, _) = sut.CheckAndRecord(userId);

        // Assert
        allowed.Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // Scenario 6 (daily variant) – blocked daily message references the limit
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_DailyLimitExceeded_HasDescriptiveMessage()
    {
        // Arrange
        const int dayLimit = 3;
        var sut = CreateLimiter(requestsPerMinute: 100, requestsPerDay: dayLimit);
        const string userId = "user-day-msg";

        for (var i = 0; i < dayLimit; i++)
            sut.CheckAndRecord(userId);

        // Act
        var (_, message) = sut.CheckAndRecord(userId);

        // Assert
        message.Should().NotBeNullOrEmpty();
        message.Should().Contain(dayLimit.ToString(),
            because: "the error message must include the daily limit so the caller can surface it to the user");
    }

    // ---------------------------------------------------------------------------
    // Scenario 4 – each user has an independent rate-limit state
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_IndependentUsers_TrackedSeparately()
    {
        // Arrange – minute limit of 3; user A will exhaust it, user B should not be affected
        var sut = CreateLimiter(requestsPerMinute: 3, requestsPerDay: 10);
        const string userA = "user-a";
        const string userB = "user-b";

        // Act – exhaust user A's per-minute quota
        sut.CheckAndRecord(userA);
        sut.CheckAndRecord(userA);
        sut.CheckAndRecord(userA);
        var (userAAllowed, _) = sut.CheckAndRecord(userA);

        // First request for user B must still be allowed
        var (userBAllowed, userBMessage) = sut.CheckAndRecord(userB);

        // Assert
        userAAllowed.Should().BeFalse("user A has exceeded the per-minute limit");
        userBAllowed.Should().BeTrue("user B has a completely independent counter");
        userBMessage.Should().BeNull();
    }

    // ---------------------------------------------------------------------------
    // Additional edge cases
    // ---------------------------------------------------------------------------

    [Fact]
    public void CheckAndRecord_SameUserMultipleRequestsUnderBothLimits_AllAllowed()
    {
        // Arrange – limits are 3/min and 5/day; send exactly 3 requests
        var sut = CreateLimiter(requestsPerMinute: 3, requestsPerDay: 5);
        const string userId = "user-under";

        // Act & Assert – every call up to the limit must succeed
        for (var i = 0; i < 3; i++)
        {
            var (allowed, message) = sut.CheckAndRecord(userId);
            allowed.Should().BeTrue($"request {i + 1} is within both limits");
            message.Should().BeNull($"no error expected for request {i + 1}");
        }
    }

    [Fact]
    public void CheckAndRecord_NewUser_AlwaysStartsWithCleanState()
    {
        // Arrange – two completely separate limiter instances share no state
        var sut = CreateLimiter(requestsPerMinute: 1, requestsPerDay: 5);
        const string userId = "user-fresh";

        // Exhaust the per-minute limit
        sut.CheckAndRecord(userId);
        var (firstBlocked, _) = sut.CheckAndRecord(userId);
        firstBlocked.Should().BeFalse();

        // A brand-new user on the same limiter instance must not be affected
        var (newUserAllowed, _) = sut.CheckAndRecord("brand-new-user");
        newUserAllowed.Should().BeTrue();
    }
}
