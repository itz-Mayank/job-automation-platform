using JobForge.Application.Common;
using JobForge.Application.DTOs;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using JobForge.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Application.Services;

public sealed class JobService : IJobService
{
    private static readonly int[] AllowedIntervalSeconds = { 60, 300, 900, 3600, 86400 };

    private readonly IApplicationDbContext _db;
    private readonly IUrlValidator _urlValidator;

    public JobService(IApplicationDbContext db, IUrlValidator urlValidator)
    {
        _db = db;
        _urlValidator = urlValidator;
    }

    public async Task<PagedResult<JobDto>> ListAsync(Guid userId, string? search, int page, int pageSize, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Jobs.Where(j => j.UserId == userId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(j => j.Name.ToLower().Contains(term));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<JobDto>
        {
            Items = jobs.Select(ToDto).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<JobDto> GetByIdAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await FindOwnedJobAsync(userId, jobId, cancellationToken);
        return ToDto(job);
    }

    public async Task<JobDto> CreateAsync(Guid userId, CreateJobRequest request, CancellationToken cancellationToken)
    {
        ValidateSchedule(request.ScheduleType, request.ScheduleValueSeconds);
        _urlValidator.ValidateJobUrl(request.Url);

        var job = Job.Create(
            userId,
            request.Name.Trim(),
            request.Description?.Trim(),
            request.HttpMethod,
            request.Url.Trim(),
            NormalizeHeaders(request.Headers),
            request.Body,
            request.ScheduleType,
            request.ScheduleType == ScheduleType.Interval ? request.ScheduleValueSeconds : null,
            request.TimeoutSeconds,
            request.MaxRetries,
            request.RetryDelaySeconds);

        _db.Jobs.Add(job);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(job);
    }

    public async Task<JobDto> UpdateAsync(Guid userId, Guid jobId, UpdateJobRequest request, CancellationToken cancellationToken)
    {
        ValidateSchedule(request.ScheduleType, request.ScheduleValueSeconds);
        _urlValidator.ValidateJobUrl(request.Url);

        var job = await FindOwnedJobAsync(userId, jobId, cancellationToken);

        job.Update(
            request.Name.Trim(),
            request.Description?.Trim(),
            request.HttpMethod,
            request.Url.Trim(),
            NormalizeHeaders(request.Headers),
            request.Body,
            request.ScheduleType,
            request.ScheduleType == ScheduleType.Interval ? request.ScheduleValueSeconds : null,
            request.TimeoutSeconds,
            request.MaxRetries,
            request.RetryDelaySeconds);

        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(job);
    }

    public async Task DeleteAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await FindOwnedJobAsync(userId, jobId, cancellationToken);
        _db.Jobs.Remove(job);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<JobDto> PauseAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await FindOwnedJobAsync(userId, jobId, cancellationToken);
        job.Pause();
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(job);
    }

    public async Task<JobDto> ResumeAsync(Guid userId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await FindOwnedJobAsync(userId, jobId, cancellationToken);
        job.Resume();
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(job);
    }

    /// <summary>Ownership is enforced in the query itself (never fetch-then-check), so a job belonging to
    /// another user is indistinguishable from a nonexistent one.</summary>
    private async Task<Job> FindOwnedJobAsync(Guid userId, Guid jobId, CancellationToken cancellationToken) =>
        await _db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId, cancellationToken)
            ?? throw new NotFoundException(nameof(Job), jobId);

    private static void ValidateSchedule(ScheduleType scheduleType, int? scheduleValueSeconds)
    {
        if (scheduleType == ScheduleType.Interval && (scheduleValueSeconds is null || !AllowedIntervalSeconds.Contains(scheduleValueSeconds.Value)))
        {
            throw new ValidationException("ScheduleValueSeconds must be one of: 60, 300, 900, 3600, 86400 when ScheduleType is Interval.");
        }
    }

    private static Dictionary<string, string>? NormalizeHeaders(Dictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0) return null;

        // Authorization/secret-shaped headers are still allowed to be *sent* (the job needs them to call
        // the target API) but callers should not rely on retrieving them back — see execution-side redaction.
        return headers.Where(kv => !string.IsNullOrWhiteSpace(kv.Key)).ToDictionary(kv => kv.Key.Trim(), kv => kv.Value);
    }

    private static JobDto ToDto(Job job) => new(
        job.Id,
        job.Name,
        job.Description,
        job.JobType.ToString(),
        job.Status.ToString(),
        job.HttpMethod.ToString(),
        job.Url,
        job.Headers is null ? null : new Dictionary<string, string>(job.Headers),
        job.Body,
        job.ScheduleType.ToString(),
        job.ScheduleValueSeconds,
        job.TimeoutSeconds,
        job.MaxRetries,
        job.RetryDelaySeconds,
        job.LastRunAt,
        job.NextRunAt,
        job.CreatedAt,
        job.UpdatedAt);
}
