using FluentAssertions;
using JobForge.Application.Common;
using Xunit;

namespace JobForge.Tests.Unit;

public class RetryPolicyTests
{
    [Theory]
    [InlineData(1, 30, 30)]
    [InlineData(2, 30, 60)]
    [InlineData(3, 30, 120)]
    public void ComputeDelay_FollowsExponentialBackoff(int failedAttempt, int baseDelay, int expectedMinSeconds)
    {
        var delay = RetryPolicy.ComputeDelay(failedAttempt, baseDelay);

        // Allow for the up-to-1s jitter added on top of the exponential base.
        delay.TotalSeconds.Should().BeGreaterThanOrEqualTo(expectedMinSeconds);
        delay.TotalSeconds.Should().BeLessThan(expectedMinSeconds + 1);
    }

    [Theory]
    [InlineData(429, true)]
    [InlineData(500, true)]
    [InlineData(502, true)]
    [InlineData(503, true)]
    [InlineData(504, true)]
    [InlineData(400, false)]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(404, false)]
    public void IsRetryableStatusCode_ClassifiesCorrectly(int statusCode, bool expectedRetryable)
    {
        RetryPolicy.IsRetryableStatusCode(statusCode).Should().Be(expectedRetryable);
    }
}
