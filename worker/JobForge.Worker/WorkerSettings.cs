namespace JobForge.Worker;

public sealed class WorkerSettings
{
    public const string SectionName = "Worker";

    /// <summary>Identifies this process in execution.worker_id and logs. Defaults to machine name + a short random suffix.</summary>
    public string? WorkerId { get; set; }

    public int PollIntervalSeconds { get; set; } = 2;

    public int ErrorBackoffSeconds { get; set; } = 5;

    /// <summary>How long a RUNNING execution can go without finishing before it's considered abandoned by a crashed worker.</summary>
    public int StaleExecutionThresholdSeconds { get; set; } = 120;
}
