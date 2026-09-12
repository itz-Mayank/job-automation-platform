namespace JobForge.Application.DTOs;

public sealed record ExecutionDto(
    Guid Id,
    Guid JobId,
    string JobName,
    string Status,
    int Attempt,
    int MaxAttempts,
    string? WorkerId,
    string Trigger,
    Guid? RetryOfExecutionId,
    DateTimeOffset ScheduledAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    int? DurationMs,
    int? HttpStatusCode,
    string? ResponseBody,
    string? ErrorMessage,
    DateTimeOffset? NextAttemptAt,
    bool WillRetry);
