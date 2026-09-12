using JobForge.Domain.Enums;
using JobForge.Domain.Exceptions;

namespace JobForge.Domain.Entities;

/// <summary>
/// One row represents one logical execution attempt sequence for a job. It cycles
/// through PENDING/RUNNING/RETRYING as automatic retries happen, converging on a
/// terminal state (SUCCEEDED, FAILED, or CANCELLED). All state changes go through
/// this class so invalid transitions (e.g. SUCCEEDED -> RUNNING) are impossible
/// regardless of which service/controller is calling in.
///
/// Only the most recent attempt's timing/response/error is retained on the row
/// (matching the flat schema) — Attempt/MaxAttempts tell the reader how many
/// tries have happened and how many remain, but per-attempt history beyond the
/// latest is not separately stored. This is a deliberate simplification; see
/// ENGINEERING.md "Known Limitations".
/// </summary>
public class JobExecution
{
    private static readonly Dictionary<ExecutionStatus, ExecutionStatus[]> AllowedTransitions = new()
    {
        [ExecutionStatus.Pending] = new[] { ExecutionStatus.Running, ExecutionStatus.Cancelled },
        [ExecutionStatus.Running] = new[] { ExecutionStatus.Succeeded, ExecutionStatus.Failed, ExecutionStatus.Retrying, ExecutionStatus.Cancelled },
        [ExecutionStatus.Retrying] = new[] { ExecutionStatus.Pending, ExecutionStatus.Cancelled },
        [ExecutionStatus.Succeeded] = Array.Empty<ExecutionStatus>(),
        [ExecutionStatus.Failed] = Array.Empty<ExecutionStatus>(),
        [ExecutionStatus.Cancelled] = Array.Empty<ExecutionStatus>(),
    };

    public Guid Id { get; private set; }
    public Guid JobId { get; private set; }
    public Job? Job { get; private set; }

    public ExecutionStatus Status { get; private set; }
    public int Attempt { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? WorkerId { get; private set; }
    public ExecutionTrigger Trigger { get; private set; }

    /// <summary>Client-supplied Run Now dedup key. Null for scheduled/retry executions.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Set when this execution was created by a manual "Retry" of a failed execution, pointing at the original.</summary>
    public Guid? RetryOfExecutionId { get; private set; }

    public DateTimeOffset ScheduledAt { get; private set; }
    public DateTimeOffset? StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }
    public int? DurationMs { get; private set; }

    public int? HttpStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When RETRYING, the time the next attempt becomes eligible to be promoted back to PENDING.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private JobExecution() { }

    public static JobExecution Create(
        Guid jobId,
        int maxAttempts,
        ExecutionTrigger trigger,
        DateTimeOffset scheduledAt,
        string? idempotencyKey = null,
        Guid? retryOfExecutionId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobExecution
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            Status = ExecutionStatus.Pending,
            Attempt = 1,
            MaxAttempts = maxAttempts,
            Trigger = trigger,
            IdempotencyKey = idempotencyKey,
            RetryOfExecutionId = retryOfExecutionId,
            ScheduledAt = scheduledAt,
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    private void TransitionTo(ExecutionStatus next)
    {
        if (!AllowedTransitions.TryGetValue(Status, out var allowed) || !allowed.Contains(next))
        {
            throw new InvalidStateTransitionException(Status, next);
        }

        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Claims the execution for a worker. Caller is responsible for the atomic DB-level claim (see IExecutionClaimService); this only enforces the state rule.</summary>
    public void MarkRunning(string workerId)
    {
        TransitionTo(ExecutionStatus.Running);
        WorkerId = workerId;
        StartedAt = DateTimeOffset.UtcNow;
        FinishedAt = null;
        HttpStatusCode = null;
        ErrorMessage = null;
    }

    public void MarkSucceeded(int httpStatusCode, string? responseBody)
    {
        var now = DateTimeOffset.UtcNow;
        TransitionTo(ExecutionStatus.Succeeded);
        FinishedAt = now;
        DurationMs = ComputeDurationMs(now);
        HttpStatusCode = httpStatusCode;
        ResponseBody = responseBody;
        ErrorMessage = null;
        NextAttemptAt = null;
    }

    /// <summary>Retryable failure with attempts remaining: schedules the next attempt.</summary>
    public void MarkRetryScheduled(string errorMessage, int? httpStatusCode, DateTimeOffset nextAttemptAt)
    {
        var now = DateTimeOffset.UtcNow;
        TransitionTo(ExecutionStatus.Retrying);
        FinishedAt = now;
        DurationMs = ComputeDurationMs(now);
        HttpStatusCode = httpStatusCode;
        ErrorMessage = errorMessage;
        NextAttemptAt = nextAttemptAt;
    }

    /// <summary>Non-retryable failure, or attempts exhausted: terminal.</summary>
    public void MarkFailed(string errorMessage, int? httpStatusCode)
    {
        var now = DateTimeOffset.UtcNow;
        TransitionTo(ExecutionStatus.Failed);
        FinishedAt = now;
        DurationMs = ComputeDurationMs(now);
        HttpStatusCode = httpStatusCode;
        ErrorMessage = errorMessage;
        NextAttemptAt = null;
    }

    /// <summary>Promotes a RETRYING row back to PENDING for the next attempt. Called by the worker's retry-promotion pass once NextAttemptAt has elapsed.</summary>
    public void PromoteRetryToPending()
    {
        TransitionTo(ExecutionStatus.Pending);
        Attempt += 1;
        ScheduledAt = NextAttemptAt ?? DateTimeOffset.UtcNow;
        NextAttemptAt = null;
        WorkerId = null;
    }

    public void Cancel()
    {
        TransitionTo(ExecutionStatus.Cancelled);
        FinishedAt = DateTimeOffset.UtcNow;
    }

    public bool HasAttemptsRemaining => Attempt < MaxAttempts;

    private int ComputeDurationMs(DateTimeOffset now) =>
        StartedAt.HasValue ? (int)(now - StartedAt.Value).TotalMilliseconds : 0;
}
