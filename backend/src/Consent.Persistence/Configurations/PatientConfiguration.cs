using Consent.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public const string CitizenIdHash = "CitizenIdHash";
    private const int EncryptedColumnLength = 512;
    private const int HashLength = 64;

    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patients");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.PatientCode).HasMaxLength(Patient.PatientCodeMaxLength);
        builder.HasIndex(p => p.PatientCode).IsUnique().HasFilter("[PatientCode] IS NOT NULL");
        builder.Property(p => p.FullName).HasMaxLength(Patient.FullNameMaxLength).IsRequired()
            .UseCollation(Collations.AccentInsensitive);
        builder.Property(p => p.Gender).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.PermanentAddress).HasMaxLength(Patient.AddressMaxLength);
        builder.Property(p => p.CurrentAddress).HasMaxLength(Patient.AddressMaxLength);
        builder.Property(p => p.Ethnicity).HasMaxLength(Patient.EthnicityMaxLength);
        builder.Property(p => p.Occupation).HasMaxLength(Patient.OccupationMaxLength);
        builder.Property(p => p.Nationality).HasMaxLength(Patient.NationalityMaxLength);

        // Chỉ mục mù của số CCCD: tra cứu/phát hiện trùng mà không cần giải mã.
        builder.Property<string?>(CitizenIdHash).HasMaxLength(HashLength).IsFixedLength().IsUnicode(false);
        builder.HasIndex(CitizenIdHash).IsUnique().HasFilter($"[{CitizenIdHash}] IS NOT NULL");

        builder.HasMany(p => p.Representatives).WithOne().HasForeignKey(r => r.PatientId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Representatives).HasField("_representatives").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasRowVersion();
    }

    /// <summary>Cột nhạy cảm được lưu dưới dạng bản mã (tên cột có hậu tố "Encrypted").</summary>
    public static void ConfigureEncryption(ModelBuilder modelBuilder, EncryptedStringConverter encrypted)
    {
        modelBuilder.Entity<Patient>(builder =>
        {
            Encrypt(builder.Property(p => p.CitizenId), "CitizenIdEncrypted", encrypted);
            Encrypt(builder.Property(p => p.OldIdNumber), "OldIdNumberEncrypted", encrypted);
            Encrypt(builder.Property(p => p.PhoneNumber), "PhoneNumberEncrypted", encrypted);
            Encrypt(builder.Property(p => p.HealthInsuranceNumber), "HealthInsuranceNumberEncrypted", encrypted);
        });
        modelBuilder.Entity<LegalRepresentative>(builder =>
        {
            Encrypt(builder.Property(r => r.CitizenId), "CitizenIdEncrypted", encrypted);
            Encrypt(builder.Property(r => r.PhoneNumber), "PhoneNumberEncrypted", encrypted);
        });
    }

    private static void Encrypt(PropertyBuilder<string?> property, string columnName, EncryptedStringConverter encrypted) =>
        property.HasColumnName(columnName).HasMaxLength(EncryptedColumnLength).IsUnicode(false).HasConversion((ValueConverter)encrypted);
}

internal sealed class LegalRepresentativeConfiguration : IEntityTypeConfiguration<LegalRepresentative>
{
    public void Configure(EntityTypeBuilder<LegalRepresentative> builder)
    {
        builder.ToTable("LegalRepresentatives");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.FullName).HasMaxLength(Patient.FullNameMaxLength).IsRequired()
            .UseCollation(Collations.AccentInsensitive);
        builder.Property(r => r.Relationship).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Gender).HasConversion<string>().HasMaxLength(10);
        builder.Property(r => r.Address).HasMaxLength(Patient.AddressMaxLength);
        builder.Property<string?>(PatientConfiguration.CitizenIdHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
        builder.HasIndex(PatientConfiguration.CitizenIdHash);
    }
}
