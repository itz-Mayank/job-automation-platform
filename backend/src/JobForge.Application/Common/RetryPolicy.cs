namespace JobForge.Application.Common;

/// <summary>
/// Exponential backoff with jitter, and the retryable/non-retryable classification
/// used to decide whether a failed attempt goes to RETRYING or terminal FAILED.
/// See ENGINEERING.md "Retry Strategy" for the reasoning.
/// </summary>
public static class RetryPolicy
{
    /// <summary>delay = baseDelay * 2^(attempt-1), plus up to 1s of jitter to avoid thundering-herd retries.</summary>
    public static TimeSpan ComputeDelay(int failedAttempt, int baseDelaySeconds)
    {
        var exponential = baseDelaySeconds * Math.Pow(2, failedAttempt - 1);
        var jitterMs = Random.Shared.Next(0, 1000);
        return TimeSpan.FromSeconds(exponential) + TimeSpan.FromMilliseconds(jitterMs);
    }

    /// <summary>
    /// 429 and 5xx are treated as transient (rate limiting, upstream overload/outage) and are retried.
    /// 4xx other than 429 indicates a client-side problem with the request itself (bad auth, bad URL,
    /// not found) that a retry cannot fix, so those are not retried.
    /// </summary>
    public static bool IsRetryableStatusCode(int httpStatusCode) =>
        httpStatusCode == 429 || httpStatusCode >= 500;
}
