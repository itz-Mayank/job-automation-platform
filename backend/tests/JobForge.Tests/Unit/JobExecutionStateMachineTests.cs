using FluentAssertions;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using JobForge.Domain.Exceptions;
using Xunit;

namespace JobForge.Tests.Unit;

public class JobExecutionStateMachineTests
{
    private static JobExecution NewExecution(int maxAttempts = 3) =>
        JobExecution.Create(Guid.NewGuid(), maxAttempts, ExecutionTrigger.Manual, DateTimeOffset.UtcNow);

    [Fact]
    public void Create_StartsInPending()
    {
        var execution = NewExecution();
        execution.Status.Should().Be(ExecutionStatus.Pending);
        execution.Attempt.Should().Be(1);
    }

    [Fact]
    public void Pending_To_Running_To_Succeeded_IsValid()
    {
        var execution = NewExecution();
        execution.MarkRunning("worker-1");
        execution.Status.Should().Be(ExecutionStatus.Running);

        execution.MarkSucceeded(200, "ok");
        execution.Status.Should().Be(ExecutionStatus.Succeeded);
        execution.FinishedAt.Should().NotBeNull();
    }

    [Fact]
    public void Running_To_Retrying_To_Pending_IsValid()
    {
        var execution = NewExecution(maxAttempts: 3);
        execution.MarkRunning("worker-1");

        execution.MarkRetryScheduled("timeout", null, DateTimeOffset.UtcNow.AddSeconds(30));
        execution.Status.Should().Be(ExecutionStatus.Retrying);

        execution.PromoteRetryToPending();
        execution.Status.Should().Be(ExecutionStatus.Pending);
        execution.Attempt.Should().Be(2);
    }

    [Fact]
    public void Succeeded_To_Running_IsInvalid()
    {
        var execution = NewExecution();
        execution.MarkRunning("worker-1");
        execution.MarkSucceeded(200, "ok");

        var act = () => execution.MarkRunning("worker-1");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Cancelled_To_Running_IsInvalid()
    {
        var execution = NewExecution();
        execution.Cancel();

        var act = () => execution.MarkRunning("worker-1");

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void Failed_To_Retrying_IsInvalid_FailedIsAlwaysTerminal()
    {
        var execution = NewExecution(maxAttempts: 1);
        execution.MarkRunning("worker-1");
        execution.MarkFailed("boom", 500);

        var act = () => execution.MarkRetryScheduled("boom", 500, DateTimeOffset.UtcNow.AddSeconds(1));

        act.Should().Throw<InvalidStateTransitionException>();
    }

    [Fact]
    public void HasAttemptsRemaining_IsFalse_WhenAtMaxAttempts()
    {
        var execution = NewExecution(maxAttempts: 2);
        execution.MarkRunning("worker-1");
        execution.MarkRetryScheduled("err", null, DateTimeOffset.UtcNow.AddSeconds(1));
        execution.PromoteRetryToPending();

        execution.Attempt.Should().Be(2);
        execution.HasAttemptsRemaining.Should().BeFalse();
    }
}
