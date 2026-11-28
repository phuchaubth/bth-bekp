using Consent.Domain.Departments;
using Consent.Domain.NursingReports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

internal sealed class NursingDailyReportConfiguration : IEntityTypeConfiguration<NursingDailyReport>
{
    public void Configure(EntityTypeBuilder<NursingDailyReport> builder)
    {
        builder.ToTable("NursingDailyReports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        // Mỗi khoa một báo cáo mỗi ngày.
        builder.HasIndex(r => new { r.DepartmentId, r.ReportDate }).IsUnique();
        builder.HasIndex(r => r.ReportDate);
        // Ủy quyền mà người gửi dùng (quyết định 48); ủy quyền không bị xóa, chỉ hủy.
        builder.HasOne<NursingReportDelegation>().WithMany().HasForeignKey(r => r.SubmittedDelegationId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FiguresJson).IsRequired();
        builder.Property(r => r.ReopenReason).HasMaxLength(NursingDailyReport.ReopenReasonMaxLength);
        builder.Ignore(r => r.IsSubmitted);
        builder.HasRowVersion();
    }

    /// <summary>Phần báo cáo nhập thêm có thể có thông tin người bệnh: mã hóa như dữ liệu phiếu đồng thuận.</summary>
    public static void ConfigureEncryption(ModelBuilder modelBuilder, EncryptedStringConverter encrypted) =>
        modelBuilder.Entity<NursingDailyReport>().Property(r => r.Notes).HasColumnName("NotesEncrypted").IsUnicode(false)
            .HasConversion((ValueConverter)encrypted);
}

internal sealed class NursingReportDepartmentConfiguration : IEntityTypeConfiguration<NursingReportDepartment>
{
    public void Configure(EntityTypeBuilder<NursingReportDepartment> builder)
    {
        builder.ToTable("NursingReportDepartments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasOne<Department>().WithMany().HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.DepartmentId).IsUnique();
        builder.HasRowVersion();
    }
}

internal sealed class NursingDailyStatisticsConfiguration : IEntityTypeConfiguration<NursingDailyStatistics>
{
    public void Configure(EntityTypeBuilder<NursingDailyStatistics> builder)
    {
        builder.ToTable("NursingDailyStatistics");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => s.ReportDate).IsUnique();
        builder.Property(s => s.ValuesJson).IsRequired();
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(20);
        builder.HasRowVersion();
    }
}

internal sealed class NursingDepartmentStatisticsConfiguration : IEntityTypeConfiguration<NursingDepartmentStatistics>
{
    public void Configure(EntityTypeBuilder<NursingDepartmentStatistics> builder)
    {
        builder.ToTable("NursingDepartmentStatistics");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.HasIndex(s => new { s.ReportDate, s.DepartmentId }).IsUnique();
        builder.Property(s => s.ValuesJson).IsRequired().HasMaxLength(NursingDepartmentStatistics.ValuesMaxLength);
        builder.HasOne<NursingReportDelegation>().WithMany().HasForeignKey(s => s.DelegationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasRowVersion();
    }
}

/// <summary>
/// Ủy quyền báo cáo điều dưỡng (quyết định 48). Không khóa ngoại sang tài khoản (như người lập, người gửi báo cáo) để xóa tài
/// khoản không bị chặn; ủy quyền vẫn giữ để đối chiếu.
/// </summary>
internal sealed class NursingReportDelegationConfiguration : IEntityTypeConfiguration<NursingReportDelegation>
{
    public void Configure(EntityTypeBuilder<NursingReportDelegation> builder)
    {
        builder.ToTable("NursingReportDelegations");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.HasOne<Department>().WithMany().HasForeignKey(d => d.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(d => d.DelegateUserId);
        builder.HasIndex(d => d.DelegatorUserId);
        builder.HasIndex(d => new { d.StartDate, d.EndDate });
        builder.Property(d => d.Scope).HasConversion<int>();
        builder.Property(d => d.Reason).HasMaxLength(NursingReportDelegation.ReasonMaxLength);
        builder.Ignore(d => d.IsRevoked);
        builder.HasRowVersion();
    }
}
