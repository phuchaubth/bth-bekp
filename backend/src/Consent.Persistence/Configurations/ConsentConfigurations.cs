using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

internal sealed class ConsentTemplateConfiguration : IEntityTypeConfiguration<ConsentTemplate>
{
    public void Configure(EntityTypeBuilder<ConsentTemplate> builder)
    {
        builder.ToTable("ConsentTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(ConsentTemplate.CodeMaxLength).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(ConsentTemplate.NameMaxLength).IsRequired();
        builder.Property(t => t.HospitalSignerRoleCode).HasMaxLength(ConsentTemplate.RoleCodeMaxLength).IsRequired();
        builder.HasOne<Department>().WithMany().HasForeignKey(t => t.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(t => t.Versions).WithOne().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Versions).HasField("_versions").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(t => t.PublishedVersion);
        builder.Ignore(t => t.DraftVersion);
        builder.HasRowVersion();
    }
}

internal sealed class ConsentTemplateVersionConfiguration : IEntityTypeConfiguration<ConsentTemplateVersion>
{
    public void Configure(EntityTypeBuilder<ConsentTemplateVersion> builder)
    {
        builder.ToTable("ConsentTemplateVersions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.ContentJson).IsRequired();
        builder.HasIndex(v => new { v.TemplateId, v.VersionNo }).IsUnique();
    }
}

internal sealed class ConsentFormConfiguration : IEntityTypeConfiguration<ConsentForm>
{
    public void Configure(EntityTypeBuilder<ConsentForm> builder)
    {
        builder.ToTable("ConsentForms");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        builder.Property(f => f.FormNo).HasMaxLength(ConsentForm.FormNoMaxLength).IsUnicode(false).IsRequired();
        builder.HasIndex(f => f.FormNo).IsUnique();
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(30);
        builder.Property(f => f.SignerKind).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.CancelReason).HasMaxLength(ConsentForm.ReasonMaxLength);
        builder.Property(f => f.RevokeReason).HasMaxLength(ConsentForm.ReasonMaxLength);
        builder.Property(f => f.Decision).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.SignedPdfKey).HasMaxLength(200).IsUnicode(false);
        builder.Property(f => f.SignedPdfSha256).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.HasOne<ConsentForm>().WithMany().HasForeignKey(f => f.ReplacesFormId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => f.ReplacesFormId).IsUnique().HasFilter("[ReplacesFormId] IS NOT NULL");

        builder.HasMany(f => f.Signatures).WithOne().HasForeignKey(s => s.FormId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(f => f.Signatures).HasField("_signatures").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(f => f.PatientSignature);
        builder.Ignore(f => f.HospitalSignature);

        // Tham chiếu chỉ để toàn vẹn dữ liệu; không xóa dây chuyền (phiếu là hồ sơ cần lưu trữ).
        builder.HasOne<Patient>().WithMany().HasForeignKey(f => f.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConsentTemplate>().WithMany().HasForeignKey(f => f.TemplateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConsentTemplateVersion>().WithMany().HasForeignKey(f => f.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany().HasForeignKey(f => f.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(f => f.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(f => new { f.Status, f.DepartmentId, f.CreatedAtUtc });
        builder.HasIndex(f => f.PatientId);
        builder.HasIndex(f => f.TemplateId);
        builder.HasRowVersion();
    }

    /// <summary>Nội dung phiếu và thông tin đóng băng có dữ liệu cá nhân: lưu dạng bản mã (AES-256-GCM).</summary>
    public static void ConfigureEncryption(ModelBuilder modelBuilder, EncryptedStringConverter encrypted)
    {
        modelBuilder.Entity<ConsentForm>(builder =>
        {
            builder.Property(f => f.DataJson).HasColumnName("DataEncrypted").IsUnicode(false).IsRequired()
                .HasConversion((ValueConverter)encrypted);
            builder.Property(f => f.SnapshotJson).HasColumnName("SnapshotEncrypted").IsUnicode(false)
                .HasConversion((ValueConverter)encrypted);
        });
    }
}

internal sealed class ConsentSignatureConfiguration : IEntityTypeConfiguration<ConsentSignature>
{
    public void Configure(EntityTypeBuilder<ConsentSignature> builder)
    {
        builder.ToTable("ConsentSignatures");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Role).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Method).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.SignerName).HasMaxLength(ConsentSignature.NameMaxLength).IsRequired();
        builder.Property(s => s.SignerTitle).HasMaxLength(ConsentSignature.TitleMaxLength);
        builder.Property(s => s.Device).HasMaxLength(ConsentSignature.DeviceMaxLength);
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.FormId);
    }

    /// <summary>Ảnh chữ ký tay là dữ liệu cá nhân: lưu dạng bản mã (AES-256-GCM).</summary>
    public static void ConfigureEncryption(ModelBuilder modelBuilder, EncryptedBytesConverter encrypted) =>
        modelBuilder.Entity<ConsentSignature>().Property(s => s.ImagePng).HasColumnName("ImagePngEncrypted").IsUnicode(false)
            .HasConversion((ValueConverter)encrypted);
}
