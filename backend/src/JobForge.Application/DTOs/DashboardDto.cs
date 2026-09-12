namespace JobForge.Application.DTOs;

public sealed record DashboardSummaryDto(
    int TotalJobs,
    int ActiveJobs,
    int PausedJobs,
    int RunningExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    IReadOnlyList<ExecutionDto> RecentExecutions);
