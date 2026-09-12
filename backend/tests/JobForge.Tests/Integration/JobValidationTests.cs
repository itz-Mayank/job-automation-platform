using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobForge.Application.DTOs;
using JobForge.Domain.Enums;
using JobForge.Tests.TestFixtures;
using Xunit;

namespace JobForge.Tests.Integration;

[Collection("Postgres")]
public class JobValidationTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JobForgeApiFactory _factory = default!;
    private HttpClient _client = default!;

    public JobValidationTests(PostgresFixture postgres) => _postgres = postgres;

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
    public async Task CreateJob_WithPrivateNetworkUrl_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "SSRF attempt",
            HttpMethod = HttpMethodType.GET,
            Url = "http://192.168.1.1/admin",
            ScheduleType = ScheduleType.Manual,
            TimeoutSeconds = 10,
            MaxRetries = 1,
            RetryDelaySeconds = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateJob_IntervalScheduleWithoutValue_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "Bad schedule",
            HttpMethod = HttpMethodType.GET,
            Url = "https://example.com",
            ScheduleType = ScheduleType.Interval,
            ScheduleValueSeconds = null,
            TimeoutSeconds = 10,
            MaxRetries = 1,
            RetryDelaySeconds = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateJob_IntervalScheduleWithDisallowedValue_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "Bad interval",
            HttpMethod = HttpMethodType.GET,
            Url = "https://example.com",
            ScheduleType = ScheduleType.Interval,
            ScheduleValueSeconds = 42, // not one of the allowed presets
            TimeoutSeconds = 10,
            MaxRetries = 1,
            RetryDelaySeconds = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task CreateJob_MissingName_IsRejectedByModelValidation()
    {
        var response = await _client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = "",
            HttpMethod = HttpMethodType.GET,
            Url = "https://example.com",
            ScheduleType = ScheduleType.Manual,
            TimeoutSeconds = 10,
            MaxRetries = 1,
            RetryDelaySeconds = 10
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateJob_ValidRequest_Succeeds()
    {
        var job = await ApiTestHelpers.CreateJobAsync(_client, scheduleType: ScheduleType.Interval, scheduleValueSeconds: 300);

        job.ScheduleType.Should().Be("Interval");
        job.NextRunAt.Should().NotBeNull();
    }
}
