using JobForge.Domain.Entities;

namespace JobForge.Application.Interfaces;

/// <summary>
/// Executes the side-effecting part of a job. Kept separate from the worker's
/// orchestration loop (claim/persist) so job execution logic is independently
/// testable and swappable (e.g. a future non-HTTP job type could add another
/// implementation without touching the worker).
/// </summary>
public interface IJobExecutor
{
    Task<JobExecutionOutcome> ExecuteAsync(Job job, CancellationToken cancellationToken);
}

/// <summary>Structured result of running one attempt — never throws; all failure modes are captured here.</summary>
public sealed record JobExecutionOutcome
{
    public required bool Success { get; init; }
    public int? HttpStatusCode { get; init; }
    public string? ResponseBody { get; init; }
    public string? ErrorMessage { get; init; }

    /// <summary>Only meaningful when Success is false. Drives whether the execution goes to RETRYING or FAILED.</summary>
    public bool IsRetryable { get; init; }

    public static JobExecutionOutcome Ok(int httpStatusCode, string? responseBody) => new()
    {
        Success = true,
        HttpStatusCode = httpStatusCode,
        ResponseBody = responseBody
    };

    public static JobExecutionOutcome Failure(string errorMessage, bool isRetryable, int? httpStatusCode = null) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        IsRetryable = isRetryable,
        HttpStatusCode = httpStatusCode
    };
}
