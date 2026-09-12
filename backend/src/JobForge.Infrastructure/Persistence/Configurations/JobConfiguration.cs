using System.Text.Json;
using JobForge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace JobForge.Infrastructure.Persistence.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");

        builder.HasKey(j => j.Id);

        builder.HasOne(j => j.User)
            .WithMany(u => u.Jobs)
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(j => j.Name).HasMaxLength(200).IsRequired();
        builder.Property(j => j.Description).HasMaxLength(1000);

        builder.Property(j => j.JobType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.HttpMethod).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(j => j.ScheduleType).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(j => j.Url).HasMaxLength(2048).IsRequired();
        builder.Property(j => j.Body);

        var headersComparer = new ValueComparer<IReadOnlyDictionary<string, string>?>(
            (a, b) => JsonSerializer.Serialize(a, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(b, (JsonSerializerOptions?)null),
            d => d == null ? 0 : JsonSerializer.Serialize(d, (JsonSerializerOptions?)null).GetHashCode(),
            d => d == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(JsonSerializer.Serialize(d, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null));

        builder.Property(j => j.Headers)
            .HasConversion(
                d => d == null ? null : JsonSerializer.Serialize(d, (JsonSerializerOptions?)null),
                s => s == null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(s, (JsonSerializerOptions?)null))
            .Metadata.SetValueComparer(headersComparer);
        builder.Property(j => j.Headers).HasColumnType("jsonb");

        builder.Property(j => j.CreatedAt).IsRequired();
        builder.Property(j => j.UpdatedAt).IsRequired();

        builder.HasIndex(j => j.UserId);
        builder.HasIndex(j => j.NextRunAt);

        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
