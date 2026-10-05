using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniPdm.Domain.Versions;
using MiniPdm.Domain.Objects;

namespace MiniPdm.Storage.Configurations;

internal sealed class ObjectVersionConfiguration : IEntityTypeConfiguration<ObjectVersion>
{
    public void Configure(EntityTypeBuilder<ObjectVersion> b)
    {
        b.ToTable("object_versions", t =>
        {
            t.HasCheckConstraint("ck_object_versions_version_positive", "\"Version\" > 0");
            t.HasCheckConstraint("ck_object_versions_mass_nonnegative", "\"Mass\" IS NULL OR \"Mass\" >= 0");
            t.HasCheckConstraint("ck_object_versions_state", "\"State\" IN (1, 2, 3)");
        });
        b.HasKey(x => x.Id);
        b.HasAlternateKey(x => new { x.Id, x.ObjectId });
        b.HasIndex(x => new { x.ObjectId, x.Version }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(512);
        b.Property(x => x.Material).HasMaxLength(256);
        b.Property(x => x.SourceReference).HasMaxLength(2048);
        b.Property(x => x.Mass).HasPrecision(18, 6);
        b.Property(x => x.State).HasConversion<int>();
        b.HasOne(x => x.Object).WithMany(x => x.Versions).HasForeignKey(x => x.ObjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
