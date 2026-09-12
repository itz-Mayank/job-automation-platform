using FluentAssertions;
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
/// Proves the core multi-worker safety guarantee (spec section 32): given one PENDING
/// execution and two independent claim attempts arriving at essentially the same time
/// (each backed by its own DbContext/connection, exactly like two separate worker
/// processes would be), exactly one succeeds and the other gets back null instead of
/// the same row. This exercises real "FOR UPDATE SKIP LOCKED" against Postgres.
/// </summary>
[Collection("Postgres")]
public class ExecutionClaimConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;

    public ExecutionClaimConcurrencyTests(PostgresFixture postgres) => _postgres = postgres;

    private string _connectionString = default!;

    public async Task InitializeAsync() => _connectionString = await _postgres.CreateIsolatedDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<Guid> SeedPendingExecutionAsync()
    {
        await using var db = CreateDbContext();

        var user = User.Create(ApiTestHelpers.UniqueEmail(), "irrelevant-hash");
        var job = Job.Create(user.Id, "Claim test job", null, HttpMethodType.GET, "https://example.com",
            null, null, ScheduleType.Manual, null, 30, 3, 30);
        var execution = JobExecution.Create(job.Id, maxAttempts: 3, trigger: ExecutionTrigger.Manual, scheduledAt: DateTimeOffset.UtcNow);

        db.Users.Add(user);
        db.Jobs.Add(job);
        db.Executions.Add(execution);
        await db.SaveChangesAsync();

        return execution.Id;
    }

    private JobForgeDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<JobForgeDbContext>()
            .UseNpgsql(_connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new JobForgeDbContext(options);
    }

    [Fact]
    public async Task TwoConcurrentClaims_OnlyOneSucceeds()
    {
        var executionId = await SeedPendingExecutionAsync();

        await using var dbForWorker1 = CreateDbContext();
        await using var dbForWorker2 = CreateDbContext();

        var clock = new FakeClock();
        var claim1 = new ExecutionClaimService(dbForWorker1, clock, NullLogger<ExecutionClaimService>.Instance);
        var claim2 = new ExecutionClaimService(dbForWorker2, clock, NullLogger<ExecutionClaimService>.Instance);

        var task1 = claim1.ClaimNextPendingAsync("worker-1", CancellationToken.None);
        var task2 = claim2.ClaimNextPendingAsync("worker-2", CancellationToken.None);
        var results = await Task.WhenAll(task1, task2);

        var claimed = results.Where(r => r is not null).ToList();
        claimed.Should().ContainSingle("exactly one worker should have claimed the row");
        claimed[0]!.Id.Should().Be(executionId);
        claimed[0]!.Status.Should().Be(ExecutionStatus.Running);

        await using var verifyDb = CreateDbContext();
        var finalRow = await verifyDb.Executions.FindAsync(executionId);
        finalRow!.Status.Should().Be(ExecutionStatus.Running);
        finalRow.WorkerId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ClaimWithNoEligibleRows_ReturnsNull()
    {
        await using var db = CreateDbContext();
        var claim = new ExecutionClaimService(db, new FakeClock(), NullLogger<ExecutionClaimService>.Instance);

        var result = await claim.ClaimNextPendingAsync("worker-1", CancellationToken.None);

        result.Should().BeNull();
    }
}
