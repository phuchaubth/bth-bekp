using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consent.Persistence.Configurations;

internal sealed class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("ExportJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();
        builder.Property(j => j.Kind).HasMaxLength(ExportJob.KindMaxLength).IsUnicode(false).IsRequired();
        builder.Property(j => j.Format).HasConversion<string>().HasMaxLength(10);
        builder.Property(j => j.FiltersJson).HasMaxLength(ExportJob.FiltersMaxLength).IsRequired();
        builder.Property(j => j.Columns).HasMaxLength(ExportJob.ColumnsMaxLength).IsUnicode(false).IsRequired();
        builder.Property(j => j.FileName).HasMaxLength(ExportJob.FileNameMaxLength).IsRequired();
        builder.Property(j => j.FileSha256).HasMaxLength(ExportJob.Sha256Length).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Ignore(j => j.ColumnKeys);
        builder.HasOne<User>().WithMany().HasForeignKey(j => j.RequestedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(j => new { j.RequestedByUserId, j.RequestedAtUtc });
        builder.HasIndex(j => j.RequestedAtUtc);
    }
}
