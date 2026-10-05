using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MiniPdm.Storage.Entities;

namespace MiniPdm.Storage.Configurations;

internal sealed class ImportJournalConfiguration : IEntityTypeConfiguration<ImportJournal>
{
    public void Configure(EntityTypeBuilder<ImportJournal> b)
    {
        b.ToTable("import_journal");
        b.HasKey(x => x.ImportId);
        b.Property(x => x.ReportJson).IsRequired();
    }
}
