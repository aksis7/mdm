using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniPdm.Storage.Entities;

namespace MiniPdm.Storage.Configurations;

internal sealed class BackgroundTaskConfiguration : IEntityTypeConfiguration<BackgroundTask>
{
    public void Configure(EntityTypeBuilder<BackgroundTask> builder)
    {
        builder.ToTable("background_tasks", table =>
        {
            table.HasCheckConstraint("ck_background_tasks_interval", "\"IntervalMinutes\" BETWEEN 1 AND 525600");
            table.HasCheckConstraint("ck_background_tasks_state", "\"State\" IN ('Idle', 'Running', 'Succeeded', 'PartiallySucceeded', 'Failed', 'Interrupted')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.Name).HasMaxLength(256).IsRequired();
        builder.Property(x => x.State).HasMaxLength(32).IsRequired();
    }
}
