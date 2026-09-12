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
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Postgres' configuration.");

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
}
