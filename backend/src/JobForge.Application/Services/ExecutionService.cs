using JobForge.Application.Common;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using JobForge.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Application.Services;

public sealed class ExecutionService : IExecutionService
{
    private static readonly ExecutionStatus[] ActiveStatuses =
    {
        ExecutionStatus.Pending, ExecutionStatus.Running, ExecutionStatus.Retrying
    };

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;

    public ExecutionService(IApplicationDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<ExecutionDto> RunNowAsync(Guid userId, Guid jobId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), jobId);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await _db.Executions.FirstOrDefaultAsync(
                e => e.JobId == jobId && e.IdempotencyKey == idempotencyKey, cancellationToken);
            if (replay is not null)
            {
                return ToDto(replay, job.Name);
            }
        }

        var existingActive = await _db.Executions.FirstOrDefaultAsync(
            e => e.JobId == jobId && ActiveStatuses.Contains(e.Status), cancellationToken);
        if (existingActive is not null)
        {
            return ToDto(existingActive, job.Name);
        }

        var execution = JobExecution.Create(
            job.Id,
            maxAttempts: job.MaxRetries + 1,
            trigger: ExecutionTrigger.Manual,
            scheduledAt: _clock.UtcNow,
            idempotencyKey: idempotencyKey);

        _db.Executions.Add(execution);

        try
        {
            job.RecordManualRun(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The only realistic cause of this specific insert failing is the partial unique index
            // (one active execution per job) — another concurrent Run Now won the race. Re-read and
            // return whatever it created instead of surfacing a 500.
            var winner = await _db.Executions.FirstOrDefaultAsync(
                e => e.JobId == jobId && ActiveStatuses.Contains(e.Status), cancellationToken);
            if (winner is not null)
            {
                return ToDto(winner, job.Name);
            }
            throw;
        }

        return ToDto(execution, job.Name);
    }

    public async Task<PagedResult<ExecutionDto>> ListForJobAsync(Guid userId, Guid jobId, ExecutionStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var job = await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), jobId);

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Executions.Where(e => e.JobId == jobId);
        if (status is not null)
        {
            query = query.Where(e => e.Status == status);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var executions = await query
            .OrderByDescending(e => e.ScheduledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ExecutionDto>
        {
            Items = executions.Select(e => ToDto(e, job.Name)).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<ExecutionDto> GetByIdAsync(Guid userId, Guid executionId, CancellationToken cancellationToken)
    {
        var execution = await _db.Executions.Include(e => e.Job)
            .FirstOrDefaultAsync(e => e.Id == executionId && e.Job!.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobExecution), executionId);

        return ToDto(execution, execution.Job!.Name);
    }

    public async Task<ExecutionDto> RetryAsync(Guid userId, Guid executionId, CancellationToken cancellationToken)
    {
        var original = await _db.Executions.Include(e => e.Job)
            .FirstOrDefaultAsync(e => e.Id == executionId && e.Job!.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(JobExecution), executionId);

        if (original.Status != ExecutionStatus.Failed)
        {
            throw new ConflictException("Only a failed execution can be retried.");
        }

        var job = original.Job!;

        var existingActive = await _db.Executions.FirstOrDefaultAsync(
            e => e.JobId == job.Id && ActiveStatuses.Contains(e.Status), cancellationToken);
        if (existingActive is not null)
        {
            throw new ConflictException("This job already has an active execution; wait for it to finish before retrying.");
        }

        var retryExecution = JobExecution.Create(
            job.Id,
            maxAttempts: job.MaxRetries + 1,
            trigger: ExecutionTrigger.Retry,
            scheduledAt: _clock.UtcNow,
            retryOfExecutionId: original.Id);

        _db.Executions.Add(retryExecution);

        try
        {
            job.RecordManualRun(_clock.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("This job already has an active execution; wait for it to finish before retrying.");
        }

        return ToDto(retryExecution, job.Name);
    }

    private static ExecutionDto ToDto(JobExecution e, string jobName) => new(
        e.Id,
        e.JobId,
        jobName,
        e.Status.ToString(),
        e.Attempt,
        e.MaxAttempts,
        e.WorkerId,
        e.Trigger.ToString(),
        e.RetryOfExecutionId,
        e.ScheduledAt,
        e.StartedAt,
        e.FinishedAt,
        e.DurationMs,
        e.HttpStatusCode,
        e.ResponseBody,
        e.ErrorMessage,
        e.NextAttemptAt,
        WillRetry: e.Status == ExecutionStatus.Retrying);
}
