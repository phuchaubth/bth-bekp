using Consent.Domain.CriticalResults;
using Consent.Domain.Departments;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consent.Persistence.Configurations;

/// <summary>Danh mục kết quả nguy cấp (quyết định 60).</summary>
internal sealed class CriticalTestConfiguration : IEntityTypeConfiguration<CriticalTest>
{
    public void Configure(EntityTypeBuilder<CriticalTest> builder)
    {
        builder.ToTable("CriticalTests");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Name).HasMaxLength(CriticalTest.NameMaxLength).IsRequired();
        builder.Property(t => t.AdultRange).HasMaxLength(CriticalTest.RangeMaxLength).IsRequired();
        builder.Property(t => t.ChildRange).HasMaxLength(CriticalTest.RangeMaxLength);
        builder.Property(t => t.Unit).HasMaxLength(CriticalTest.UnitMaxLength);
        builder.Property(t => t.Section).HasMaxLength(CriticalTest.SectionMaxLength);
        builder.Property(t => t.Logbook).HasConversion<string>().HasMaxLength(20);
        builder.HasRowVersion();
    }
}

/// <summary>Hồ sơ kết quả nguy cấp và các lần báo (quyết định 60).</summary>
internal sealed class CriticalResultConfiguration : IEntityTypeConfiguration<CriticalResult>
{
    public void Configure(EntityTypeBuilder<CriticalResult> builder)
    {
        builder.ToTable("CriticalResults");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(CriticalResult.NumberMaxLength).IsRequired();
        builder.HasIndex(r => r.Number).IsUnique();
        builder.Property(r => r.PatientCode).HasMaxLength(CriticalResult.PatientCodeMaxLength).IsRequired();
        builder.Property(r => r.PatientName).HasMaxLength(CriticalResult.PatientNameMaxLength).IsRequired();
        builder.Property(r => r.PatientGender).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.PatientAddress).HasMaxLength(CriticalResult.AddressMaxLength);
        builder.Property(r => r.PatientSource).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.TestName).HasMaxLength(CriticalTest.NameMaxLength).IsRequired();
        builder.Property(r => r.TestAdultRange).HasMaxLength(CriticalTest.RangeMaxLength).IsRequired();
        builder.Property(r => r.TestChildRange).HasMaxLength(CriticalTest.RangeMaxLength);
        builder.Property(r => r.Logbook).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Value).HasMaxLength(CriticalResult.TextValueMaxLength).IsRequired();
        builder.Property(r => r.Unit).HasMaxLength(CriticalTest.UnitMaxLength);
        builder.Property(r => r.SampleId).HasMaxLength(CriticalResult.SampleIdMaxLength);
        builder.Property(r => r.RecordedByName).HasMaxLength(CriticalResult.ActorNameMaxLength).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.AcknowledgedByName).HasMaxLength(CriticalResult.ActorNameMaxLength);
        builder.Property(r => r.ActionByName).HasMaxLength(CriticalResult.ActorNameMaxLength);
        builder.Property(r => r.ActionSummary).HasMaxLength(CriticalResult.SummaryMaxLength);
        builder.Property(r => r.CancelledByName).HasMaxLength(CriticalResult.ActorNameMaxLength);
        builder.Property(r => r.CancelReason).HasMaxLength(CriticalResult.ReasonMaxLength);

        // Bàn làm việc hai khoa (đang mở, vừa xong) và dashboard, sổ theo giờ có kết quả.
        builder.HasIndex(r => new { r.SendingDepartmentId, r.Status, r.UpdatedAtUtc });
        builder.HasIndex(r => new { r.ReceivingDepartmentId, r.Status, r.UpdatedAtUtc });
        builder.HasIndex(r => r.ResultAtUtc);
        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.SendingDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.ReceivingDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CriticalTest>().WithMany().HasForeignKey(r => r.TestId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Notifications).WithOne().HasForeignKey(n => n.ResultId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(r => r.Notifications).HasField("_notifications").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(r => r.NotifyDueAtUtc);
        builder.Ignore(r => r.ActionDueAtUtc);
        builder.Ignore(r => r.CurrentNotification);
        builder.Ignore(r => r.IsCancelled);
        builder.Ignore(r => r.NotifiedOnTime);
        builder.Ignore(r => r.ActedOnTime);
        builder.Ignore(r => r.LateEntry);
        builder.HasRowVersion();
    }
}

internal sealed class CriticalNotificationConfiguration : IEntityTypeConfiguration<CriticalNotification>
{
    public void Configure(EntityTypeBuilder<CriticalNotification> builder)
    {
        builder.ToTable("CriticalNotifications");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.ReceiverName).HasMaxLength(CriticalNotification.ReceiverNameMaxLength).IsRequired();
        builder.Property(n => n.NotifiedByName).HasMaxLength(CriticalResult.ActorNameMaxLength).IsRequired();
        builder.Property(n => n.RejectedByName).HasMaxLength(CriticalResult.ActorNameMaxLength);
        builder.Property(n => n.RejectReason).HasMaxLength(CriticalNotification.ReasonMaxLength);
        builder.HasIndex(n => new { n.ResultId, n.AttemptNo }).IsUnique();
        builder.HasOne<Department>().WithMany().HasForeignKey(n => n.ReceivingDepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(n => n.IsRejected);
        builder.HasRowVersion();
    }
}
