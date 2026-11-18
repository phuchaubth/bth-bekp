using BEKP.Domain.Beds;
using BEKP.Domain.Common;
using BEKP.Domain.Departments;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BEKP.Persistence.Configurations;

internal sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> builder)
    {
        builder.ToTable("Departments");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Code).HasMaxLength(BusinessCodes.MaxLength).IsUnicode(false).IsRequired();
        builder.HasIndex(d => d.Code).IsUnique();
        builder.Property(d => d.Name).HasMaxLength(Department.NameMaxLength).IsRequired();
        builder.Property(d => d.Description).HasMaxLength(Department.DescriptionMaxLength).IsRequired();
        builder.Property(d => d.Group).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
    }
}

internal sealed class BedConfiguration : IEntityTypeConfiguration<Bed>
{
    public void Configure(EntityTypeBuilder<Bed> builder)
    {
        builder.ToTable("Beds");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(BusinessCodes.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(b => b.RoomLabel).HasMaxLength(Bed.RoomLabelMaxLength).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.HasIndex(b => new { b.DepartmentId, b.Code }).IsUnique();
        builder.HasOne<Department>().WithMany().HasForeignKey(b => b.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class InpatientStayConfiguration : IEntityTypeConfiguration<InpatientStay>
{
    public void Configure(EntityTypeBuilder<InpatientStay> builder)
    {
        builder.ToTable("InpatientStays");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Code).HasMaxLength(BusinessCodes.MaxLength).IsUnicode(false).IsRequired();
        builder.HasIndex(s => s.Code).IsUnique();
        builder.Property(s => s.CareLevel).HasConversion<string>().HasMaxLength(10).IsUnicode(false);
        builder.Property(s => s.PrincipalDiagnosisCode).HasMaxLength(8).IsUnicode(false).IsRequired();
        builder.Property(s => s.DiseaseGroup).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
        builder.Ignore(s => s.IsActive);
        builder.Ignore(s => s.IsSevere);
        builder.HasIndex(s => new { s.DepartmentId, s.DischargedAtUtc });
        builder.HasIndex(s => s.AdmittedAtUtc);
        builder.HasOne<Department>().WithMany().HasForeignKey(s => s.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Bed>().WithMany().HasForeignKey(s => s.BedId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FebrileNeutropeniaEpisodeConfiguration : IEntityTypeConfiguration<FebrileNeutropeniaEpisode>
{
    public void Configure(EntityTypeBuilder<FebrileNeutropeniaEpisode> builder)
    {
        builder.ToTable("FebrileNeutropeniaEpisodes");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.TemperatureCelsius).HasPrecision(4, 1);
        builder.Ignore(e => e.AwaitingAntibiotic);
        builder.HasIndex(e => new { e.DepartmentId, e.AntibioticStartedAtUtc });
        builder.HasOne<InpatientStay>().WithMany().HasForeignKey(e => e.StayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(e => e.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BloodRequestConfiguration : IEntityTypeConfiguration<BloodRequest>
{
    public void Configure(EntityTypeBuilder<BloodRequest> builder)
    {
        builder.ToTable("BloodRequests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Product).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(r => r.AboGroup).HasConversion<string>().HasMaxLength(2).IsUnicode(false);
        builder.Property(r => r.Urgency).HasConversion<string>().HasMaxLength(12).IsUnicode(false);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsUnicode(false);
        builder.Ignore(r => r.IsPending);
        builder.HasIndex(r => new { r.DepartmentId, r.Status });
        builder.HasIndex(r => r.IssuedAtUtc);
        builder.HasOne<InpatientStay>().WithMany().HasForeignKey(r => r.StayId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(r => r.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BloodInventoryItemConfiguration : IEntityTypeConfiguration<BloodInventoryItem>
{
    public void Configure(EntityTypeBuilder<BloodInventoryItem> builder)
    {
        builder.ToTable("BloodInventory");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Product).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(i => i.AboGroup).HasConversion<string>().HasMaxLength(2).IsUnicode(false);
        builder.Ignore(i => i.Level);
        builder.HasIndex(i => new { i.Product, i.AboGroup }).IsUnique();
    }
}

internal sealed class OperationalEventConfiguration : IEntityTypeConfiguration<OperationalEvent>
{
    public void Configure(EntityTypeBuilder<OperationalEvent> builder)
    {
        builder.ToTable("OperationalEvents");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
        builder.Property(e => e.DepartmentCode).HasMaxLength(BusinessCodes.MaxLength).IsUnicode(false);
        builder.Property(e => e.StayCode).HasMaxLength(BusinessCodes.MaxLength).IsUnicode(false);
        builder.Property(e => e.Subject).HasMaxLength(OperationalEvent.SubjectMaxLength);
        builder.Property(e => e.Value).HasMaxLength(OperationalEvent.SubjectMaxLength);
        builder.HasIndex(e => e.OccurredAtUtc);
        builder.HasIndex(e => new { e.DepartmentId, e.OccurredAtUtc });
    }
}

internal sealed class ProcessTimingConfiguration : IEntityTypeConfiguration<ProcessTiming>
{
    public void Configure(EntityTypeBuilder<ProcessTiming> builder)
    {
        builder.ToTable("ProcessTimings");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Standard).HasConversion<string>().HasMaxLength(40).IsUnicode(false);
        builder.Property(t => t.Subject).HasMaxLength(40).IsUnicode(false);
        builder.HasIndex(t => new { t.CompletedAtUtc, t.DepartmentId });
        builder.HasOne<Department>().WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
    }
}
