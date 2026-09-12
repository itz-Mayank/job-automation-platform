namespace JobForge.Domain.Enums;

/// <summary>
/// How an execution came into existence. Purely informational (shown in the UI) —
/// does not affect state-machine behavior.
/// </summary>
public enum ExecutionTrigger
{
    Manual = 0,
    Scheduled = 1,
    Retry = 2
}
