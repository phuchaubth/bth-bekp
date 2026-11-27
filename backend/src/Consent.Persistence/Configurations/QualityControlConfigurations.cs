using Consent.Domain.Departments;
using Consent.Domain.QualityControl;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consent.Persistence.Configurations;

/// <summary>Máy đo đường huyết của sổ nội kiểm (quyết định 57).</summary>
internal sealed class QcMeterConfiguration : IEntityTypeConfiguration<QcMeter>
{
    public void Configure(EntityTypeBuilder<QcMeter> builder)
    {
        builder.ToTable("QcMeters");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Model).HasMaxLength(QcMeter.ModelMaxLength).IsRequired();
        builder.Property(m => m.SerialNumber).HasMaxLength(QcMeter.SerialMaxLength).IsRequired();
        builder.HasIndex(m => m.SerialNumber).IsUnique();
        builder.HasIndex(m => m.DepartmentId);
        builder.HasOne<Department>().WithMany().HasForeignKey(m => m.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(m => m.Label);
        builder.HasRowVersion();
    }
}

/// <summary>Lô que thử, dung dịch chứng (quyết định 57).</summary>
internal sealed class QcLotConfiguration : IEntityTypeConfiguration<QcLot>
{
    public void Configure(EntityTypeBuilder<QcLot> builder)
    {
        builder.ToTable("QcLots");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.ProductName).HasMaxLength(QcLot.ProductMaxLength).IsRequired();
        builder.Property(l => l.LotNumber).HasMaxLength(QcLot.LotNumberMaxLength).IsRequired();
        builder.Property(l => l.RangeLow).HasPrecision(5, 1);
        builder.Property(l => l.RangeHigh).HasPrecision(5, 1);
        builder.Property(l => l.Unit).HasConversion<string>().HasMaxLength(10);

        // Một số lô chỉ khai báo một lần cho mỗi loại vật tư và mức (que thử không có mức). Bỏ bộ lọc "IS NOT NULL" EF tự thêm
        // cho cột cho phép NULL, để số lô que thử cũng không trùng.
        builder.HasIndex(l => new { l.Kind, l.LotNumber, l.Level }).IsUnique().HasFilter(null);
        builder.HasRowVersion();
    }
}

/// <summary>Sổ nội kiểm tháng của một máy trong một khoa, cùng kết quả và lần xem xét từng ngày (quyết định 57).</summary>
internal sealed class QcBookConfiguration : IEntityTypeConfiguration<QcBook>
{
    public void Configure(EntityTypeBuilder<QcBook> builder)
    {
        builder.ToTable("QcBooks");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.ReturnReason).HasMaxLength(QcBook.ReasonMaxLength);
        builder.HasIndex(b => new { b.MeterId, b.DepartmentId, b.Year, b.Month }).IsUnique();
        builder.HasIndex(b => new { b.Year, b.Month });
        builder.HasOne<QcMeter>().WithMany().HasForeignKey(b => b.MeterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(b => b.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(b => b.Runs).WithOne().HasForeignKey(r => r.BookId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Runs).HasField("_runs").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(b => b.Reviews).WithOne().HasForeignKey(r => r.BookId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(b => b.Reviews).HasField("_reviews").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(b => b.FirstDay);
        builder.Ignore(b => b.LastDay);
        builder.HasRowVersion();
    }
}

internal sealed class QcRunConfiguration : IEntityTypeConfiguration<QcRun>
{
    public void Configure(EntityTypeBuilder<QcRun> builder)
    {
        builder.ToTable("QcRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.StripLotNumber).HasMaxLength(QcLot.LotNumberMaxLength).IsRequired();
        builder.Property(r => r.Control1LotNumber).HasMaxLength(QcLot.LotNumberMaxLength).IsRequired();
        builder.Property(r => r.Control2LotNumber).HasMaxLength(QcLot.LotNumberMaxLength).IsRequired();
        foreach (var value in new[]
                 {
                     nameof(QcRun.Control1Low), nameof(QcRun.Control1High), nameof(QcRun.Control1Value), nameof(QcRun.Control1RepeatValue),
                     nameof(QcRun.Control2Low), nameof(QcRun.Control2High), nameof(QcRun.Control2Value), nameof(QcRun.Control2RepeatValue),
                 })
        {
            builder.Property(value).HasPrecision(5, 1);
        }

        builder.Property(r => r.Control1Unit).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Control2Unit).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.FailureReason).HasMaxLength(QcRun.ReasonMaxLength);
        builder.Property(r => r.CancelReason).HasMaxLength(QcRun.ReasonMaxLength);
        builder.HasIndex(r => new { r.BookId, r.RunDate });
        builder.HasOne<QcLot>().WithMany().HasForeignKey(r => r.StripLotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QcLot>().WithMany().HasForeignKey(r => r.Control1LotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QcLot>().WithMany().HasForeignKey(r => r.Control2LotId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(r => r.Control1Passed);
        builder.Ignore(r => r.Control2Passed);
        builder.Ignore(r => r.Passed);
        builder.Ignore(r => r.HasRepeat);
        builder.Ignore(r => r.Control1RepeatPassed);
        builder.Ignore(r => r.Control2RepeatPassed);
        builder.Ignore(r => r.RepeatPassed);
        builder.Ignore(r => r.FinalPassed);
        builder.Ignore(r => r.HasFollowUp);
        builder.Ignore(r => r.IsActive);
        builder.Ignore(r => r.IsLate);
        builder.HasRowVersion();
    }
}

internal sealed class QcDayReviewConfiguration : IEntityTypeConfiguration<QcDayReview>
{
    public void Configure(EntityTypeBuilder<QcDayReview> builder)
    {
        builder.ToTable("QcDayReviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Outcome).HasConversion<string>().HasMaxLength(40);
        builder.Property(r => r.Note).HasMaxLength(QcDayReview.NoteMaxLength);
        builder.Property(r => r.ReturnReason).HasMaxLength(QcDayReview.NoteMaxLength);
        builder.HasIndex(r => new { r.BookId, r.Date });
        builder.Ignore(r => r.IsEffective);
        builder.HasRowVersion();
    }
}
