using System.Net.Http.Json;
using FluentAssertions;
using JobForge.Application.DTOs;
using JobForge.Tests.TestFixtures;
using Xunit;

namespace JobForge.Tests.Integration;

/// <summary>
/// Proves the guarantee described in ENGINEERING.md "Idempotency": no matter how many
/// Run Now requests arrive at the same moment for the same job, at most one active
/// execution ever exists. This exercises the real Postgres partial unique index
/// (ix_job_executions_one_active_per_job), not a mock — that constraint, not the
/// application-level check, is what actually makes this safe under a true race.
/// </summary>
[Collection("Postgres")]
public class RunNowConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JobForgeApiFactory _factory = default!;
    private HttpClient _client = default!;

    public RunNowConcurrencyTests(PostgresFixture postgres) => _postgres = postgres;

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
    public async Task ConcurrentRunNowRequests_CreateExactlyOneActiveExecution()
    {
        var job = await ApiTestHelpers.CreateJobAsync(_client);

        const int concurrentRequests = 20;
        var tasks = Enumerable.Range(0, concurrentRequests)
            .Select(_ => _client.PostAsync($"/api/jobs/{job.Id}/run", null))
            .ToArray();

        var responses = await Task.WhenAll(tasks);
        var executions = new List<ExecutionDto>();
        foreach (var response in responses)
        {
            response.EnsureSuccessStatusCode();
            executions.Add((await response.Content.ReadFromJsonAsync<ExecutionDto>())!);
        }

        // Every single response — no matter which request "won" the race — must describe the
        // same execution row. If duplicate protection ever regresses, this assertion catches it
        // even though only one row would show up in a naive "count rows" check.
        executions.Select(e => e.Id).Distinct().Should().ContainSingle();

        var listResponse = await _client.GetAsync($"/api/jobs/{job.Id}/executions");
        listResponse.EnsureSuccessStatusCode();
        var list = await listResponse.Content.ReadFromJsonAsync<JobForge.Application.Common.PagedResult<ExecutionDto>>();

        list!.TotalCount.Should().Be(1);
    }
}
