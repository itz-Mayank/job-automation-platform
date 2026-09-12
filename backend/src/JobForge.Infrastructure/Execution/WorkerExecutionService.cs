using JobForge.Application.Common;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using JobForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobForge.Infrastructure.Execution;

public sealed class WorkerExecutionService : IWorkerExecutionService
{
    private static readonly ExecutionStatus[] ActiveStatuses =
    {
        ExecutionStatus.Pending, ExecutionStatus.Running, ExecutionStatus.Retrying
    };

    private readonly JobForgeDbContext _db;
    private readonly IExecutionClaimService _claimService;
    private readonly IJobExecutor _jobExecutor;
    private readonly IClock _clock;
    private readonly ILogger<WorkerExecutionService> _logger;

    public WorkerExecutionService(
        JobForgeDbContext db,
        IExecutionClaimService claimService,
        IJobExecutor jobExecutor,
        IClock clock,
        ILogger<WorkerExecutionService> logger)
    {
        _db = db;
        _claimService = claimService;
        _jobExecutor = jobExecutor;
        _clock = clock;
        _logger = logger;
    }

    public async Task<bool> ClaimAndProcessNextAsync(string workerId, CancellationToken cancellationToken)
    {
        var execution = await _claimService.ClaimNextPendingAsync(workerId, cancellationToken);
        if (execution is null)
        {
            return false;
        }

        var job = execution.Job!;

        // The claim transaction has already committed by this point (see ExecutionClaimService) —
        // the external call below runs with no database transaction held open.
        var outcome = await _jobExecutor.ExecuteAsync(job, cancellationToken);

        if (outcome.Success)
        {
            execution.MarkSucceeded(outcome.HttpStatusCode!.Value, outcome.ResponseBody);
            _logger.LogInformation(
                "ExecutionSucceeded executionId={ExecutionId} jobId={JobId} workerId={WorkerId} attempt={Attempt} httpStatus={HttpStatus} durationMs={DurationMs}",
                execution.Id, job.Id, workerId, execution.Attempt, execution.HttpStatusCode, execution.DurationMs);
        }
        else if (outcome.IsRetryable && execution.HasAttemptsRemaining)
        {
            var delay = RetryPolicy.ComputeDelay(execution.Attempt, job.RetryDelaySeconds);
            var nextAttemptAt = _clock.UtcNow + delay;
            execution.MarkRetryScheduled(outcome.ErrorMessage!, outcome.HttpStatusCode, nextAttemptAt);
            _logger.LogWarning(
                "ExecutionRetryScheduled executionId={ExecutionId} jobId={JobId} workerId={WorkerId} attempt={Attempt} maxAttempts={MaxAttempts} nextAttemptAt={NextAttemptAt} error={Error}",
                execution.Id, job.Id, workerId, execution.Attempt, execution.MaxAttempts, nextAttemptAt, outcome.ErrorMessage);
        }
        else
        {
            execution.MarkFailed(outcome.ErrorMessage ?? "Unknown error", outcome.HttpStatusCode);
            _logger.LogWarning(
                "ExecutionFailed executionId={ExecutionId} jobId={JobId} workerId={WorkerId} attempt={Attempt} error={Error}",
                execution.Id, job.Id, workerId, execution.Attempt, outcome.ErrorMessage);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> PromoteDueRetriesAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var dueIds = await _db.Executions
            .Where(e => e.Status == ExecutionStatus.Retrying && e.NextAttemptAt <= now)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var promoted = 0;
        foreach (var id in dueIds)
        {
            try
            {
                var execution = await _db.Executions.FirstAsync(e => e.Id == id, cancellationToken);
                execution.PromoteRetryToPending();
                await _db.SaveChangesAsync(cancellationToken);
                promoted++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker's maintenance sweep already promoted this row — expected under
                // multiple workers, not an error.
                DetachAll();
            }
        }

        return promoted;
    }

    public async Task<int> RecoverStaleExecutionsAsync(TimeSpan staleThreshold, CancellationToken cancellationToken)
    {
        var cutoff = _clock.UtcNow - staleThreshold;
        var staleIds = await _db.Executions
            .Where(e => e.Status == ExecutionStatus.Running && e.StartedAt <= cutoff)
            .Select(e => e.Id)
            .ToListAsync(cancellationToken);

        var recovered = 0;
        foreach (var id in staleIds)
        {
            try
            {
                var execution = await _db.Executions.Include(e => e.Job).FirstAsync(e => e.Id == id, cancellationToken);
                var reason = $"Recovered: worker '{execution.WorkerId}' exceeded the stale-execution threshold without reporting a result (likely crashed).";

                if (execution.HasAttemptsRemaining)
                {
                    var delay = RetryPolicy.ComputeDelay(execution.Attempt, execution.Job?.RetryDelaySeconds ?? 30);
                    execution.MarkRetryScheduled(reason, null, _clock.UtcNow + delay);
                }
                else
                {
                    execution.MarkFailed(reason, null);
                }

                await _db.SaveChangesAsync(cancellationToken);
                recovered++;

                _logger.LogWarning(
                    "ExecutionRecovered executionId={ExecutionId} jobId={JobId} previousWorkerId={WorkerId} newStatus={Status}",
                    execution.Id, execution.JobId, execution.WorkerId, execution.Status);
            }
            catch (DbUpdateConcurrencyException)
            {
                DetachAll();
            }
        }

        return recovered;
    }

    public async Task<int> CreateDueScheduledExecutionsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var dueJobIds = await _db.Jobs
            .Where(j => j.Status == JobStatus.Active && j.ScheduleType == ScheduleType.Interval && j.NextRunAt <= now)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        var created = 0;
        foreach (var jobId in dueJobIds)
        {
            var job = await _db.Jobs.FirstAsync(j => j.Id == jobId, cancellationToken);

            var hasActive = await _db.Executions.AnyAsync(
                e => e.JobId == jobId && ActiveStatuses.Contains(e.Status), cancellationToken);

            if (hasActive)
            {
                // Previous scheduled run (or a manual Run Now) is still in flight. Skip this tick —
                // NextRunAt is left as-is, so the very next sweep re-checks. See ENGINEERING.md
                // "Known Limitations" for the accepted trade-off (a missed tick vs. a queued burst).
                continue;
            }

            var execution = JobExecution.Create(
                job.Id,
                maxAttempts: job.MaxRetries + 1,
                trigger: ExecutionTrigger.Scheduled,
                scheduledAt: now);

            _db.Executions.Add(execution);
            job.RecordScheduledRun(now);

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                created++;
                _logger.LogInformation("ExecutionCreated executionId={ExecutionId} jobId={JobId} trigger=Scheduled", execution.Id, job.Id);
            }
            catch (DbUpdateException)
            {
                // Lost a race with a concurrent manual Run Now — fine, one active execution exists either way.
                DetachAll();
            }
        }

        return created;
    }

    private void DetachAll()
    {
        foreach (var entry in _db.ChangeTracker.Entries().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
