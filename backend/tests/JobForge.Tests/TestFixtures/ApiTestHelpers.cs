using System.Net.Http.Headers;
using System.Net.Http.Json;
using JobForge.Application.DTOs;
using JobForge.Domain.Enums;

namespace JobForge.Tests.TestFixtures;

public static class ApiTestHelpers
{
    public static string UniqueEmail() => $"test_{Guid.NewGuid():N}@example.com";

    public static async Task<AuthResponse> RegisterAsync(HttpClient client, string? email = null, string password = "Password123!")
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Email = email ?? UniqueEmail(),
            Password = password
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    public static void Authenticate(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    public static async Task<JobDto> CreateJobAsync(
        HttpClient client,
        string url = "https://httpbin.org/get",
        HttpMethodType method = HttpMethodType.GET,
        ScheduleType scheduleType = ScheduleType.Manual,
        int? scheduleValueSeconds = null,
        int maxRetries = 3,
        int retryDelaySeconds = 30,
        int timeoutSeconds = 30,
        string name = "Test Job")
    {
        var response = await client.PostAsJsonAsync("/api/jobs", new CreateJobRequest
        {
            Name = name,
            HttpMethod = method,
            Url = url,
            ScheduleType = scheduleType,
            ScheduleValueSeconds = scheduleValueSeconds,
            MaxRetries = maxRetries,
            RetryDelaySeconds = retryDelaySeconds,
            TimeoutSeconds = timeoutSeconds
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JobDto>())!;
    }
}
