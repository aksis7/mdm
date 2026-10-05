using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniPdm.Domain.Composition;
using MiniPdm.Domain.Versions;

namespace MiniPdm.Storage.Configurations;

internal sealed class BomLinkConfiguration : IEntityTypeConfiguration<BomLink>
{
    public void Configure(EntityTypeBuilder<BomLink> b)
    {
        b.ToTable("bom_links", t => t.HasCheckConstraint("ck_bom_links_quantity_positive", "\"Quantity\" > 0"));
        b.HasKey(x => new { x.ParentVersionId, x.ChildObjectId });
        b.HasOne(x => x.ParentVersion).WithMany(x => x.Components).HasForeignKey(x => x.ParentVersionId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ChildObject).WithMany().HasForeignKey(x => x.ChildObjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
