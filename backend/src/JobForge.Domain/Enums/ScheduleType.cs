namespace JobForge.Domain.Enums;

/// <summary>
/// Manual jobs only ever get executions via "Run Now". Interval jobs additionally
/// get scheduled executions created by the worker based on NextRunAt.
/// </summary>
public enum ScheduleType
{
    Manual = 0,
    Interval = 1
}
