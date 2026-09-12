using JobForge.Domain.Enums;

namespace JobForge.Domain.Entities;

public class Job
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User? User { get; private set; }

    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public JobType JobType { get; private set; }
    public JobStatus Status { get; private set; }

    public HttpMethodType HttpMethod { get; private set; }
    public string Url { get; private set; } = default!;
    public IReadOnlyDictionary<string, string>? Headers { get; private set; }
    public string? Body { get; private set; }

    public ScheduleType ScheduleType { get; private set; }
    public int? ScheduleValueSeconds { get; private set; }

    public int TimeoutSeconds { get; private set; }
    public int MaxRetries { get; private set; }
    public int RetryDelaySeconds { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }
    public DateTimeOffset? NextRunAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public ICollection<JobExecution> Executions { get; private set; } = new List<JobExecution>();

    private Job() { }

    public static Job Create(
        Guid userId,
        string name,
        string? description,
        HttpMethodType httpMethod,
        string url,
        IReadOnlyDictionary<string, string>? headers,
        string? body,
        ScheduleType scheduleType,
        int? scheduleValueSeconds,
        int timeoutSeconds,
        int maxRetries,
        int retryDelaySeconds)
    {
        var now = DateTimeOffset.UtcNow;
        var job = new Job
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = name,
            Description = description,
            JobType = JobType.Http,
            Status = JobStatus.Active,
            HttpMethod = httpMethod,
            Url = url,
            Headers = headers,
            Body = body,
            ScheduleType = scheduleType,
            ScheduleValueSeconds = scheduleValueSeconds,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
            RetryDelaySeconds = retryDelaySeconds,
            CreatedAt = now,
            UpdatedAt = now
        };

        job.NextRunAt = scheduleType == ScheduleType.Interval
            ? now.AddSeconds(scheduleValueSeconds!.Value)
            : null;

        return job;
    }

    public void Update(
        string name,
        string? description,
        HttpMethodType httpMethod,
        string url,
        IReadOnlyDictionary<string, string>? headers,
        string? body,
        ScheduleType scheduleType,
        int? scheduleValueSeconds,
        int timeoutSeconds,
        int maxRetries,
        int retryDelaySeconds)
    {
        Name = name;
        Description = description;
        HttpMethod = httpMethod;
        Url = url;
        Headers = headers;
        Body = body;
        TimeoutSeconds = timeoutSeconds;
        MaxRetries = maxRetries;
        RetryDelaySeconds = retryDelaySeconds;
        UpdatedAt = DateTimeOffset.UtcNow;

        if (scheduleType != ScheduleType)
        {
            ScheduleType = scheduleType;
            ScheduleValueSeconds = scheduleValueSeconds;
            NextRunAt = scheduleType == ScheduleType.Interval
                ? DateTimeOffset.UtcNow.AddSeconds(scheduleValueSeconds!.Value)
                : null;
        }
        else if (scheduleType == ScheduleType.Interval && ScheduleValueSeconds != scheduleValueSeconds)
        {
            ScheduleValueSeconds = scheduleValueSeconds;
            NextRunAt = DateTimeOffset.UtcNow.AddSeconds(scheduleValueSeconds!.Value);
        }
    }

    public void Pause()
    {
        Status = JobStatus.Paused;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Resume()
    {
        Status = JobStatus.Active;
        UpdatedAt = DateTimeOffset.UtcNow;
        if (ScheduleType == ScheduleType.Interval && NextRunAt is null)
        {
            NextRunAt = DateTimeOffset.UtcNow.AddSeconds(ScheduleValueSeconds!.Value);
        }
    }

    /// <summary>Called by the worker after it creates a scheduled execution for this job.</summary>
    public void RecordScheduledRun(DateTimeOffset ranAt)
    {
        LastRunAt = ranAt;
        NextRunAt = ScheduleType == ScheduleType.Interval
            ? ranAt.AddSeconds(ScheduleValueSeconds!.Value)
            : null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordManualRun(DateTimeOffset ranAt)
    {
        LastRunAt = ranAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
