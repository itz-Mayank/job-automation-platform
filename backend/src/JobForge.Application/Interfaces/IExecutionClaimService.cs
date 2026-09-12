using JobForge.Domain.Entities;

namespace JobForge.Application.Interfaces;

/// <summary>
/// Owns the one operation that must be race-safe across any number of worker
/// processes: picking exactly one PENDING execution and atomically transitioning
/// it to RUNNING under this worker's id. Implemented in Infrastructure using
/// "SELECT ... FOR UPDATE SKIP LOCKED" so concurrent workers never block on each
/// other and never claim the same row twice.
/// </summary>
public interface IExecutionClaimService
{
    /// <summary>Returns the claimed execution (already RUNNING, with Job loaded), or null if nothing is eligible right now.</summary>
    Task<JobExecution?> ClaimNextPendingAsync(string workerId, CancellationToken cancellationToken);
}
