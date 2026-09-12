using JobForge.Application.Common;
using JobForge.Application.Interfaces;
using JobForge.Application.Services;
using JobForge.Infrastructure.Authentication;
using JobForge.Infrastructure.Execution;
using JobForge.Infrastructure.Persistence;
using JobForge.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobForge.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Wires persistence, auth, execution, and Application-layer services in one place.
    /// Shared by the API and the Worker so both processes configure identically.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = NormalizeConnectionString(
            configuration.GetConnectionString("Postgres")
                ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Postgres' configuration."));

        services.AddDbContext<JobForgeDbContext>(options =>
            options.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<JobForgeDbContext>());

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IUrlValidator, UrlValidator>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJobService, JobService>();
        services.AddScoped<IExecutionService, ExecutionService>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddScoped<IExecutionClaimService, ExecutionClaimService>();
        services.AddScoped<IWorkerExecutionService, WorkerExecutionService>();
        services.AddScoped<IJobExecutor, HttpJobExecutor>();

        services.AddHttpClient(SsrfSafeHttpClient.Name)
            .ConfigurePrimaryHttpMessageHandler(SsrfSafeHttpClient.CreateHandler);

        return services;
    }

    /// <summary>
    /// Managed Postgres on Render/Railway/Heroku-style hosts injects the connection as a
    /// "postgres://user:pass@host:port/db" URI, which Npgsql's ADO.NET-style parser rejects outright.
    /// Local dev and Docker Compose already use the "Host=...;Port=...;..." keyword form, so only
    /// convert when a URI is detected — this keeps the same appsettings/env var working unchanged
    /// across local, Docker, and a managed-Postgres deployment.
    /// </summary>
    public static string NormalizeConnectionString(string raw)
    {
        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var database = uri.AbsolutePath.TrimStart('/');

        return $"Host={uri.Host};Port={uri.Port};Database={database};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo[1])};" +
               "SSL Mode=Require;Trust Server Certificate=true";
    }
}
