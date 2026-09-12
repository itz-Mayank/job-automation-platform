using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;

namespace JobForge.Tests.TestFixtures;

/// <summary>Lets a test dictate exactly what the "HTTP call" returns, without touching the network.</summary>
public sealed class FakeJobExecutor : IJobExecutor
{
    private readonly Queue<JobExecutionOutcome> _outcomes = new();
    public JobExecutionOutcome DefaultOutcome { get; set; } = JobExecutionOutcome.Ok(200, "ok");
    public List<Guid> ExecutedJobIds { get; } = new();

    public void Enqueue(JobExecutionOutcome outcome) => _outcomes.Enqueue(outcome);

    public Task<JobExecutionOutcome> ExecuteAsync(Job job, CancellationToken cancellationToken)
    {
        ExecutedJobIds.Add(job.Id);
        var outcome = _outcomes.Count > 0 ? _outcomes.Dequeue() : DefaultOutcome;
        return Task.FromResult(outcome);
    }
}
