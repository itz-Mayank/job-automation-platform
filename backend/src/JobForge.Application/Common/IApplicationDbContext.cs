using JobForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Application.Common;

/// <summary>
/// Abstraction over the EF Core DbContext so Application-layer services depend only
/// on this interface, not on Infrastructure/EF Core directly. Implemented by
/// JobForgeDbContext in Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Job> Jobs { get; }
    DbSet<JobExecution> Executions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
