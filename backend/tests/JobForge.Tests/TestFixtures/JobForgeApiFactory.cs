using Microsoft.AspNetCore.Mvc.Testing;

namespace JobForge.Tests.TestFixtures;

/// <summary>
/// Boots the real API host against the shared Testcontainers Postgres instance. The connection
/// string is injected via the ConnectionStrings__Postgres environment variable (read by
/// WebApplication.CreateBuilder's default configuration sources) rather than via
/// ConfigureWebHost/ConfigureAppConfiguration — for this project's top-level-statement Program.cs,
/// that hook was found to run too late to affect values already captured by AddInfrastructure()
/// during the builder's own construction, silently leaving tests pointed at the developer's local
/// database instead of the test container.
/// </summary>
public sealed class JobForgeApiFactory : WebApplicationFactory<Program>
{
    public JobForgeApiFactory(string connectionString)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        Environment.SetEnvironmentVariable("ApplyMigrationsOnStartup", "false");
    }
}
