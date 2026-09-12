using JobForge.Application.Common;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using JobForge.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IApplicationDbContext _db;

    public DashboardService(IApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, CancellationToken cancellationToken)
    {
        var jobsQuery = _db.Jobs.Where(j => j.UserId == userId);
        var executionsQuery = _db.Executions.Where(e => e.Job!.UserId == userId);

        var totalJobs = await jobsQuery.CountAsync(cancellationToken);
        var activeJobs = await jobsQuery.CountAsync(j => j.Status == JobStatus.Active, cancellationToken);
        var pausedJobs = await jobsQuery.CountAsync(j => j.Status == JobStatus.Paused, cancellationToken);
        var runningExecutions = await executionsQuery.CountAsync(e => e.Status == ExecutionStatus.Running, cancellationToken);
        var successfulExecutions = await executionsQuery.CountAsync(e => e.Status == ExecutionStatus.Succeeded, cancellationToken);
        var failedExecutions = await executionsQuery.CountAsync(e => e.Status == ExecutionStatus.Failed, cancellationToken);

        var recent = await executionsQuery
            .Include(e => e.Job)
            .OrderByDescending(e => e.CreatedAt)
            .Take(10)
            .Select(e => new ExecutionDto(
                e.Id, e.JobId, e.Job!.Name, e.Status.ToString(), e.Attempt, e.MaxAttempts,
                e.WorkerId, e.Trigger.ToString(), e.RetryOfExecutionId, e.ScheduledAt, e.StartedAt,
                e.FinishedAt, e.DurationMs, e.HttpStatusCode, e.ResponseBody, e.ErrorMessage,
                e.NextAttemptAt, e.Status == ExecutionStatus.Retrying))
            .ToListAsync(cancellationToken);

        return new DashboardSummaryDto(
            totalJobs, activeJobs, pausedJobs, runningExecutions, successfulExecutions, failedExecutions, recent);
    }
}
