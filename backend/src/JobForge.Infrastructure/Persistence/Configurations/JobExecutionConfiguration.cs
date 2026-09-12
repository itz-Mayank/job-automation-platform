using JobForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobForge.Infrastructure.Persistence.Configurations;

public class JobExecutionConfiguration : IEntityTypeConfiguration<JobExecution>
{
    public void Configure(EntityTypeBuilder<JobExecution> builder)
    {
        builder.ToTable("job_executions");

        builder.HasKey(e => e.Id);

        builder.HasOne(e => e.Job)
            .WithMany(j => j.Executions)
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.Trigger).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.WorkerId).HasMaxLength(100);
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);
        builder.Property(e => e.ErrorMessage).HasMaxLength(4000);

        // Response bodies are truncated by the executor before being stored (see HttpJobExecutor);
        // this is a hard backstop against ever persisting an unbounded blob.
        builder.Property(e => e.ResponseBody).HasMaxLength(20_000);

        builder.Property(e => e.ScheduledAt).IsRequired();

        // Covers "all executions for job X, most recent first" (the execution-history listing query).
        // A separate single-column index on JobId is deliberately not added: HasIndex() with an
        // identical property list reconfigures the same index rather than creating a second one, so
        // it would just collide with (and be overwritten by) the partial unique index below.
        builder.HasIndex(e => new { e.JobId, e.ScheduledAt }).HasDatabaseName("ix_job_executions_job_id_scheduled_at");
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => e.ScheduledAt);
        builder.HasIndex(e => new { e.Status, e.ScheduledAt });
        builder.HasIndex(e => new { e.JobId, e.IdempotencyKey });

        // The core duplicate-execution guard (spec section 11 / ENGINEERING.md "Idempotency"):
        // Postgres enforces at most one row per job whose status is PENDING/RUNNING/RETRYING,
        // regardless of any race between concurrent Run Now requests or app-level bugs.
        builder.HasIndex(e => e.JobId)
            .IsUnique()
            .HasDatabaseName("ix_job_executions_one_active_per_job")
            .HasFilter("status IN ('Pending', 'Running', 'Retrying')");

        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
