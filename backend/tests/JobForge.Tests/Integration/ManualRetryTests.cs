using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobForge.Application.DTOs;
using JobForge.Domain.Enums;
using JobForge.Tests.TestFixtures;
using Xunit;

namespace JobForge.Tests.Integration;

[Collection("Postgres")]
public class ManualRetryTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JobForgeApiFactory _factory = default!;
    private HttpClient _client = default!;

    public ManualRetryTests(PostgresFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _factory = new JobForgeApiFactory(_postgres.ConnectionString);
        _client = _factory.CreateClient();
        var auth = await ApiTestHelpers.RegisterAsync(_client);
        ApiTestHelpers.Authenticate(_client, auth.Token);
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Retry_OnNonFailedExecution_ReturnsConflict()
    {
        var job = await ApiTestHelpers.CreateJobAsync(_client);
        var runResponse = await _client.PostAsync($"/api/jobs/{job.Id}/run", null);
        var execution = (await runResponse.Content.ReadFromJsonAsync<ExecutionDto>())!;
        execution.Status.Should().Be("Pending"); // no worker running in this test host

        var retryResponse = await _client.PostAsync($"/api/executions/{execution.Id}/retry", null);

        retryResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Retry_OnFailedExecution_CreatesNewExecution_AndPreservesOriginalHistory()
    {
        var job = await ApiTestHelpers.CreateJobAsync(_client);
        var runResponse = await _client.PostAsync($"/api/jobs/{job.Id}/run", null);
        var original = (await runResponse.Content.ReadFromJsonAsync<ExecutionDto>())!;

        await MarkExecutionFailedDirectlyAsync(original.Id, "simulated failure for test");

        var retryResponse = await _client.PostAsync($"/api/executions/{original.Id}/retry", null);
        retryResponse.EnsureSuccessStatusCode();
        var retried = (await retryResponse.Content.ReadFromJsonAsync<ExecutionDto>())!;

        retried.Id.Should().NotBe(original.Id, "a retry must create a new execution row, not mutate the old one");
        retried.RetryOfExecutionId.Should().Be(original.Id);
        retried.Trigger.Should().Be("Retry");
        retried.Attempt.Should().Be(1, "a manual retry starts a fresh attempt budget");

        var originalAfter = await (await _client.GetAsync($"/api/executions/{original.Id}")).Content.ReadFromJsonAsync<ExecutionDto>();
        originalAfter!.Status.Should().Be("Failed", "the original execution's history must be untouched");
        originalAfter.ErrorMessage.Should().Be("simulated failure for test");
    }

    [Fact]
    public async Task Retry_WhileAnotherActiveExecutionExists_ReturnsConflict()
    {
        var job = await ApiTestHelpers.CreateJobAsync(_client);
        var runResponse = await _client.PostAsync($"/api/jobs/{job.Id}/run", null);
        var execution = (await runResponse.Content.ReadFromJsonAsync<ExecutionDto>())!;

        await MarkExecutionFailedDirectlyAsync(execution.Id, "failure");
        // Job now has zero active executions; start a new one manually so the job is "busy" again.
        await _client.PostAsync($"/api/jobs/{job.Id}/run", null);

        var retryResponse = await _client.PostAsync($"/api/executions/{execution.Id}/retry", null);

        retryResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task MarkExecutionFailedDirectlyAsync(Guid executionId, string errorMessage)
    {
        await using var db = _postgres.CreateDbContext();
        var execution = await db.Executions.FindAsync(executionId);
        execution!.MarkRunning("test-harness");
        execution.MarkFailed(errorMessage, 500);
        await db.SaveChangesAsync();
    }
}
