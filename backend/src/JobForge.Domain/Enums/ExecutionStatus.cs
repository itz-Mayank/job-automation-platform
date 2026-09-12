namespace JobForge.Domain.Enums;

/// <summary>
/// See <see cref="JobForge.Domain.Entities.JobExecution"/> for the transition table.
/// RETRYING is a distinct state from FAILED: FAILED is always terminal, RETRYING
/// means the current attempt failed but a future attempt is already scheduled.
/// </summary>
public enum ExecutionStatus
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Retrying = 4,
    Cancelled = 5
}
