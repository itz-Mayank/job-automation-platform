using JobForge.Application.Common;
using JobForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace JobForge.Infrastructure.Persistence;

public class JobForgeDbContext : DbContext, IApplicationDbContext
{
    public JobForgeDbContext(DbContextOptions<JobForgeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobExecution> Executions => Set<JobExecution>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JobForgeDbContext).Assembly);
    }
}
