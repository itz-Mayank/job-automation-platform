using System.Net;
using FluentAssertions;
using JobForge.Tests.TestFixtures;
using Xunit;

namespace JobForge.Tests.Integration;

[Collection("Postgres")]
public class JobOwnershipTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JobForgeApiFactory _factory = default!;

    public JobOwnershipTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new JobForgeApiFactory(_postgres.ConnectionString);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UserB_CannotReadUserAsJob()
    {
        using var clientA = _factory.CreateClient();
        var authA = await ApiTestHelpers.RegisterAsync(clientA);
        ApiTestHelpers.Authenticate(clientA, authA.Token);
        var job = await ApiTestHelpers.CreateJobAsync(clientA);

        using var clientB = _factory.CreateClient();
        var authB = await ApiTestHelpers.RegisterAsync(clientB);
        ApiTestHelpers.Authenticate(clientB, authB.Token);

        var response = await clientB.GetAsync($"/api/jobs/{job.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserB_CannotDeleteUserAsJob()
    {
        using var clientA = _factory.CreateClient();
        var authA = await ApiTestHelpers.RegisterAsync(clientA);
        ApiTestHelpers.Authenticate(clientA, authA.Token);
        var job = await ApiTestHelpers.CreateJobAsync(clientA);

        using var clientB = _factory.CreateClient();
        var authB = await ApiTestHelpers.RegisterAsync(clientB);
        ApiTestHelpers.Authenticate(clientB, authB.Token);

        var deleteResponse = await clientB.DeleteAsync($"/api/jobs/{job.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Confirm it's still there for the actual owner.
        var getResponse = await clientA.GetAsync($"/api/jobs/{job.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserB_CannotRunUserAsJob()
    {
        using var clientA = _factory.CreateClient();
        var authA = await ApiTestHelpers.RegisterAsync(clientA);
        ApiTestHelpers.Authenticate(clientA, authA.Token);
        var job = await ApiTestHelpers.CreateJobAsync(clientA);

        using var clientB = _factory.CreateClient();
        var authB = await ApiTestHelpers.RegisterAsync(clientB);
        ApiTestHelpers.Authenticate(clientB, authB.Token);

        var response = await clientB.PostAsync($"/api/jobs/{job.Id}/run", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UserB_CannotListUserAsExecutions()
    {
        using var clientA = _factory.CreateClient();
        var authA = await ApiTestHelpers.RegisterAsync(clientA);
        ApiTestHelpers.Authenticate(clientA, authA.Token);
        var job = await ApiTestHelpers.CreateJobAsync(clientA);
        await clientA.PostAsync($"/api/jobs/{job.Id}/run", null);

        using var clientB = _factory.CreateClient();
        var authB = await ApiTestHelpers.RegisterAsync(clientB);
        ApiTestHelpers.Authenticate(clientB, authB.Token);

        var response = await clientB.GetAsync($"/api/jobs/{job.Id}/executions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListJobs_OnlyReturnsOwnJobs()
    {
        using var clientA = _factory.CreateClient();
        var authA = await ApiTestHelpers.RegisterAsync(clientA);
        ApiTestHelpers.Authenticate(clientA, authA.Token);
        await ApiTestHelpers.CreateJobAsync(clientA, name: "A's job");

        using var clientB = _factory.CreateClient();
        var authB = await ApiTestHelpers.RegisterAsync(clientB);
        ApiTestHelpers.Authenticate(clientB, authB.Token);
        await ApiTestHelpers.CreateJobAsync(clientB, name: "B's job 1");
        await ApiTestHelpers.CreateJobAsync(clientB, name: "B's job 2");

        var response = await clientA.GetAsync("/api/jobs");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("A's job");
        json.Should().NotContain("B's job");
    }
}
