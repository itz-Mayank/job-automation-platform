namespace JobForge.Application.Interfaces;

/// <summary>
/// Worker-facing orchestration: claim -&gt; execute -&gt; persist, plus the periodic
/// maintenance sweeps (retry promotion, stale recovery, scheduled-job fan-out).
/// Kept separate from IExecutionService (API-facing use cases) because the two
/// have different callers, different transaction shapes, and different failure
/// handling (infra errors here must be swallowed and backed off, not surfaced as
/// HTTP responses).
/// </summary>
public interface IWorkerExecutionService
{
    /// <summary>Claims and fully processes at most one execution. Returns true if one was found and processed.</summary>
    Task<bool> ClaimAndProcessNextAsync(string workerId, CancellationToken cancellationToken);

    /// <summary>Moves RETRYING rows whose NextAttemptAt has elapsed back to PENDING.</summary>
    Task<int> PromoteDueRetriesAsync(CancellationToken cancellationToken);

    /// <summary>Recovers RUNNING rows whose lease (StartedAt + threshold) has expired — see ENGINEERING.md "Failure Recovery".</summary>
    Task<int> RecoverStaleExecutionsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken);

    /// <summary>Creates PENDING executions for interval-scheduled jobs whose NextRunAt has elapsed.</summary>
    Task<int> CreateDueScheduledExecutionsAsync(CancellationToken cancellationToken);
}
