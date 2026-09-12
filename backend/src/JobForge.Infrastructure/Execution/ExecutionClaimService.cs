using JobForge.Application.Common;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace JobForge.Infrastructure.Execution;

/// <summary>
/// The one piece of code responsible for making sure two workers never process the
/// same execution. Uses a short, dedicated transaction that does nothing but
/// SELECT ... FOR UPDATE SKIP LOCKED + flip PENDING -&gt; RUNNING, then commits
/// immediately — the transaction is never held open across the external HTTP
/// call. SKIP LOCKED means a worker that would otherwise block on a row another
/// worker is claiming instead just moves on and looks at the next eligible row,
/// so N workers polling concurrently scale linearly instead of serializing.
/// </summary>
public sealed class ExecutionClaimService : IExecutionClaimService
{
    private readonly JobForgeDbContext _db;
    private readonly IClock _clock;
    private readonly ILogger<ExecutionClaimService> _logger;

    public ExecutionClaimService(JobForgeDbContext db, IClock clock, ILogger<ExecutionClaimService> logger)
    {
        _db = db;
        _clock = clock;
        _logger = logger;
    }

    public async Task<JobExecution?> ClaimNextPendingAsync(string workerId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var candidateIds = await _db.Database.SqlQueryRaw<Guid>(
                """
                SELECT id FROM job_executions
                WHERE status = 'Pending' AND scheduled_at <= {0}
                ORDER BY scheduled_at
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """,
                _clock.UtcNow)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var executionId = candidateIds[0];
        var execution = await _db.Executions.Include(e => e.Job)
            .FirstAsync(e => e.Id == executionId, cancellationToken);

        execution.MarkRunning(workerId);
        await _db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "ExecutionClaimed executionId={ExecutionId} jobId={JobId} workerId={WorkerId} attempt={Attempt}",
            execution.Id, execution.JobId, workerId, execution.Attempt);

        return execution;
    }
}
