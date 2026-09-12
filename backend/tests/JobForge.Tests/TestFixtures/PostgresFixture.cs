using JobForge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace JobForge.Tests.TestFixtures;

/// <summary>
/// Spins up one real Postgres instance (via Testcontainers) for the whole test run and applies
/// migrations once. Shared by every integration test class through the "Postgres" collection so we
/// pay the container startup cost once. Correctness tests need real Postgres, not an in-memory
/// provider, precisely because what they're proving (SELECT ... FOR UPDATE SKIP LOCKED, the partial
/// unique index) is Postgres-specific behavior that an in-memory fake cannot exercise.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("jobforge_test")
        .WithUsername("jobforge")
        .WithPassword("jobforge")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    public JobForgeDbContext CreateDbContext() => CreateDbContext(ConnectionString);

    private static JobForgeDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<JobForgeDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new JobForgeDbContext(options);
    }

    /// <summary>
    /// Creates a brand-new, empty, migrated database on the same shared server and returns its
    /// connection string. Used by tests whose assertions depend on "nothing else is in the table"
    /// (e.g. claim-service tests) that would otherwise be flaky sharing state with every other test
    /// in the collection — without paying the cost of a second container.
    /// </summary>
    public async Task<string> CreateIsolatedDatabaseAsync()
    {
        var databaseName = $"test_{Guid.NewGuid():N}";

        await using var adminConnection = new NpgsqlConnection(ConnectionString);
        await adminConnection.OpenAsync();
        await using var createCommand = adminConnection.CreateCommand();
        createCommand.CommandText = $"CREATE DATABASE \"{databaseName}\"";
        await createCommand.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = databaseName };
        var isolatedConnectionString = builder.ConnectionString;

        await using var db = CreateDbContext(isolatedConnectionString);
        await db.Database.MigrateAsync();

        return isolatedConnectionString;
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
