using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using JobForge.Application.DTOs;
using JobForge.Tests.TestFixtures;
using Xunit;

namespace JobForge.Tests.Integration;

[Collection("Postgres")]
public class AuthTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres;
    private JobForgeApiFactory _factory = default!;
    private HttpClient _client = default!;

    public AuthTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync()
    {
        _factory = new JobForgeApiFactory(_postgres.ConnectionString);
        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Register_ThenLogin_ReturnsMatchingUser()
    {
        var email = ApiTestHelpers.UniqueEmail();
        var registerResponse = await ApiTestHelpers.RegisterAsync(_client, email, "Password123!");

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Password123!" });
        loginResponse.EnsureSuccessStatusCode();
        var login = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();

        login!.User.Id.Should().Be(registerResponse.User.Id);
        login.Token.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsConflict()
    {
        var email = ApiTestHelpers.UniqueEmail();
        await ApiTestHelpers.RegisterAsync(_client, email);

        var response = await _client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Email = email, Password = "Password123!" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        var email = ApiTestHelpers.UniqueEmail();
        await ApiTestHelpers.RegisterAsync(_client, email, "CorrectPassword1!");

        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "WrongPassword1!" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithoutToken_ReturnsUnauthorized()
    {
        var response = await _client.GetAsync("/api/auth/me");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_WithValidToken_ReturnsCurrentUser()
    {
        var auth = await ApiTestHelpers.RegisterAsync(_client);
        ApiTestHelpers.Authenticate(_client, auth.Token);

        var response = await _client.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserDto>();

        user!.Id.Should().Be(auth.User.Id);
    }
}
