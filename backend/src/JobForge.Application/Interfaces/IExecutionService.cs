using JobForge.Application.Common;
using JobForge.Application.DTOs;
using JobForge.Domain.Enums;

namespace JobForge.Application.Interfaces;

/// <summary>API-facing execution use cases. See IWorkerExecutionService for the worker-side orchestration.</summary>
public interface IExecutionService
{
    /// <summary>
    /// Creates exactly one new execution for the job, unless one is already active
    /// (PENDING/RUNNING/RETRYING), in which case the existing active execution is
    /// returned instead of creating a duplicate. See ENGINEERING.md "Idempotency".
    /// </summary>
    Task<ExecutionDto> RunNowAsync(Guid userId, Guid jobId, string? idempotencyKey, CancellationToken cancellationToken);

    Task<PagedResult<ExecutionDto>> ListForJobAsync(Guid userId, Guid jobId, ExecutionStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    Task<ExecutionDto> GetByIdAsync(Guid userId, Guid executionId, CancellationToken cancellationToken);

    /// <summary>Creates a brand-new execution referencing the failed one, preserving the original's history untouched.</summary>
    Task<ExecutionDto> RetryAsync(Guid userId, Guid executionId, CancellationToken cancellationToken);
}
