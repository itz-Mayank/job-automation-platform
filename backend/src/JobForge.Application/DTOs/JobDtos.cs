using System.ComponentModel.DataAnnotations;
using JobForge.Domain.Enums;

namespace JobForge.Application.DTOs;

public class CreateJobRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [Required]
    public HttpMethodType HttpMethod { get; set; }

    [Required, MaxLength(2048)]
    public string Url { get; set; } = default!;

    public Dictionary<string, string>? Headers { get; set; }

    [MaxLength(65536)]
    public string? Body { get; set; }

    [Required]
    public ScheduleType ScheduleType { get; set; }

    /// <summary>Required and must be one of the supported intervals (60/300/900/3600/86400) when ScheduleType is Interval.</summary>
    public int? ScheduleValueSeconds { get; set; }

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    [Range(1, 3600)]
    public int RetryDelaySeconds { get; set; } = 30;
}

public sealed class UpdateJobRequest : CreateJobRequest
{
}

public sealed record JobDto(
    Guid Id,
    string Name,
    string? Description,
    string JobType,
    string Status,
    string HttpMethod,
    string Url,
    Dictionary<string, string>? Headers,
    string? Body,
    string ScheduleType,
    int? ScheduleValueSeconds,
    int TimeoutSeconds,
    int MaxRetries,
    int RetryDelaySeconds,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
