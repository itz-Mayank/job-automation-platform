using FluentAssertions;
using JobForge.Application.Interfaces;
using JobForge.Domain.Entities;
using JobForge.Domain.Enums;
using JobForge.Infrastructure.Execution;
using JobForge.Infrastructure.Persistence;
using JobForge.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JobForge.Tests.Integration;

/// <summary>
/// Covers the worker-side reliability guarantees: bounded retries with backoff (never an
/// infinite retry loop), and recovery of an execution abandoned by a crashed worker. Uses
/// a FakeJobExecutor so these tests are deterministic and don't depend on network access,
/// and an isolated database per test since the maintenance sweeps under test
/// (PromoteDueRetriesAsync, RecoverStaleExecutionsAsync) scan globally with no per-test scoping.
/// </summary>
[Collection("Postgres")]
public class RetryAndStaleRecoveryTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private string _connectionString = default!;

    public RetryAndStaleRecoveryTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync() => _connectionString = await _postgres.CreateIsolatedDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private JobForgeDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<JobForgeDbContext>().UseNpgsql(_connectionString).UseSnakeCaseNamingConvention().Options);

    private async Task<(Guid JobId, Guid ExecutionId)> SeedPendingExecutionAsync(JobForgeDbContext db, int maxRetries, int retryDelaySeconds = 10)
    {
        var user = User.Create(ApiTestHelpers.UniqueEmail(), "hash");
        var job = Job.Create(user.Id, "Retry test job", null, HttpMethodType.GET, "https://example.com",
            null, null, ScheduleType.Manual, null, 30, maxRetries, retryDelaySeconds);
        var execution = JobExecution.Create(job.Id, maxAttempts: maxRetries + 1, trigger: ExecutionTrigger.Manual, scheduledAt: DateTimeOffset.UtcNow);

        db.Users.Add(user);
        db.Jobs.Add(job);
        db.Executions.Add(execution);
        await db.SaveChangesAsync();

        return (job.Id, execution.Id);
    }

    [Fact]
    public async Task RetryableFailure_IsRetried_ThenExhaustsToFailed_AfterMaxAttempts()
    {
        await using var db = CreateDbContext();
        var (_, executionId) = await SeedPendingExecutionAsync(db, maxRetries: 1, retryDelaySeconds: 10); // MaxAttempts = 2

        var clock = new FakeClock();
        var executor = new FakeJobExecutor { DefaultOutcome = JobExecutionOutcome.Failure("boom", isRetryable: true, httpStatusCode: 500) };
        var claimService = new ExecutionClaimService(db, clock, NullLogger<ExecutionClaimService>.Instance);
        var workerService = new WorkerExecutionService(db, claimService, executor, clock, NullLogger<WorkerExecutionService>.Instance);

        // Attempt 1 fails, retryable, attempts remain -> RETRYING.
        (await workerService.ClaimAndProcessNextAsync("worker-1", CancellationToken.None)).Should().BeTrue();
        var afterAttempt1 = await db.Executions.FindAsync(executionId);
        afterAttempt1!.Status.Should().Be(ExecutionStatus.Retrying);
        afterAttempt1.Attempt.Should().Be(1);
        afterAttempt1.NextAttemptAt.Should().NotBeNull();

        // Fast-forward past the backoff window and promote back to PENDING.
        clock.UtcNow = afterAttempt1.NextAttemptAt!.Value.AddSeconds(1);
        (await workerService.PromoteDueRetriesAsync(CancellationToken.None)).Should().Be(1);
        var afterPromotion = await db.Executions.FindAsync(executionId);
        afterPromotion!.Status.Should().Be(ExecutionStatus.Pending);
        afterPromotion.Attempt.Should().Be(2);

        // Attempt 2 fails again; MaxAttempts (2) is now exhausted -> terminal FAILED, no more retries.
        (await workerService.ClaimAndProcessNextAsync("worker-1", CancellationToken.None)).Should().BeTrue();
        var final = await db.Executions.FindAsync(executionId);
        final!.Status.Should().Be(ExecutionStatus.Failed);
        final.Attempt.Should().Be(2);
        final.ErrorMessage.Should().Contain("boom");

        executor.ExecutedJobIds.Should().HaveCount(2, "the job must not be called again once FAILED is terminal");
    }

    [Fact]
    public async Task NonRetryableFailure_GoesStraightToFailed_EvenWithAttemptsRemaining()
    {
        await using var db = CreateDbContext();
        var (_, executionId) = await SeedPendingExecutionAsync(db, maxRetries: 5);

        var clock = new FakeClock();
        var executor = new FakeJobExecutor { DefaultOutcome = JobExecutionOutcome.Failure("not found", isRetryable: false, httpStatusCode: 404) };
        var claimService = new ExecutionClaimService(db, clock, NullLogger<ExecutionClaimService>.Instance);
        var workerService = new WorkerExecutionService(db, claimService, executor, clock, NullLogger<WorkerExecutionService>.Instance);

        await workerService.ClaimAndProcessNextAsync("worker-1", CancellationToken.None);

        var result = await db.Executions.FindAsync(executionId);
        result!.Status.Should().Be(ExecutionStatus.Failed);
        result.Attempt.Should().Be(1);
    }

    [Fact]
    public async Task StaleRunningExecution_WithAttemptsRemaining_IsRecoveredToRetrying()
    {
        await using var db = CreateDbContext();
        var (_, executionId) = await SeedPendingExecutionAsync(db, maxRetries: 3);

        var clock = new FakeClock();
        var claimService = new ExecutionClaimService(db, clock, NullLogger<ExecutionClaimService>.Instance);

        // Simulate a worker claiming it and then crashing before reporting any result.
        await claimService.ClaimNextPendingAsync("worker-that-crashed", CancellationToken.None);
        var runningRow = await db.Executions.FindAsync(executionId);
        runningRow!.Status.Should().Be(ExecutionStatus.Running);

        // Advance the clock well past the stale threshold.
        clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(10);
        var workerService = new WorkerExecutionService(db, claimService, new FakeJobExecutor(), clock, NullLogger<WorkerExecutionService>.Instance);

        var recoveredCount = await workerService.RecoverStaleExecutionsAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

        recoveredCount.Should().Be(1);
        var recovered = await db.Executions.FindAsync(executionId);
        recovered!.Status.Should().Be(ExecutionStatus.Retrying);
        recovered.ErrorMessage.Should().Contain("Recovered");
    }

    [Fact]
    public async Task StaleRunningExecution_WithNoAttemptsRemaining_IsRecoveredToFailed()
    {
        await using var db = CreateDbContext();
        var (_, executionId) = await SeedPendingExecutionAsync(db, maxRetries: 0); // MaxAttempts = 1

        var clock = new FakeClock();
        var claimService = new ExecutionClaimService(db, clock, NullLogger<ExecutionClaimService>.Instance);
        await claimService.ClaimNextPendingAsync("worker-that-crashed", CancellationToken.None);

        clock.UtcNow = DateTimeOffset.UtcNow.AddMinutes(10);
        var workerService = new WorkerExecutionService(db, claimService, new FakeJobExecutor(), clock, NullLogger<WorkerExecutionService>.Instance);

        await workerService.RecoverStaleExecutionsAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

        var recovered = await db.Executions.FindAsync(executionId);
        recovered!.Status.Should().Be(ExecutionStatus.Failed);
    }

    [Fact]
    public async Task FreshRunningExecution_BelowStaleThreshold_IsNotRecovered()
    {
        await using var db = CreateDbContext();
        var (_, executionId) = await SeedPendingExecutionAsync(db, maxRetries: 3);

        var clock = new FakeClock();
        var claimService = new ExecutionClaimService(db, clock, NullLogger<ExecutionClaimService>.Instance);
        await claimService.ClaimNextPendingAsync("worker-1", CancellationToken.None);

        var workerService = new WorkerExecutionService(db, claimService, new FakeJobExecutor(), clock, NullLogger<WorkerExecutionService>.Instance);
        var recoveredCount = await workerService.RecoverStaleExecutionsAsync(TimeSpan.FromMinutes(2), CancellationToken.None);

        recoveredCount.Should().Be(0);
        var stillRunning = await db.Executions.FindAsync(executionId);
        stillRunning!.Status.Should().Be(ExecutionStatus.Running);
    }
}
