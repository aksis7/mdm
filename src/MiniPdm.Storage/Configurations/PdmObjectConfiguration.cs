using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniPdm.Domain.Objects;

namespace MiniPdm.Storage.Configurations;

internal sealed class PdmObjectConfiguration : IEntityTypeConfiguration<PdmObject>
{
    public void Configure(EntityTypeBuilder<PdmObject> b)
    {
        b.ToTable("pdm_objects", t => t.HasCheckConstraint("ck_pdm_objects_identity", "(\"Type\" IN (1,2) AND \"Designation\" IS NOT NULL AND \"NormalizedName\" IS NULL) OR (\"Type\" = 3 AND \"Designation\" IS NULL AND \"NormalizedName\" IS NOT NULL)"));
        b.HasKey(x => x.Id);
        b.HasOne(x => x.CurrentVersion).WithMany().HasForeignKey(x => new { x.CurrentVersionId, x.Id }).HasPrincipalKey(x => new { x.Id, x.ObjectId }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Designation).HasMaxLength(16);
        b.Property(x => x.NormalizedName).HasMaxLength(512);
        b.Property(x => x.StandardName).HasMaxLength(512);
        b.Property(x => x.ConcurrencyToken).IsConcurrencyToken();
        b.HasIndex(x => x.Designation).IsUnique().HasFilter("\"Type\" IN (1, 2) AND \"Designation\" IS NOT NULL");
        b.HasIndex(x => x.NormalizedName).IsUnique().HasFilter("\"Type\" = 3 AND \"NormalizedName\" IS NOT NULL");
    }
}
