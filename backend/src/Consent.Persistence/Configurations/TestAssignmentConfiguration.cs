using Consent.Domain.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

/// <summary>Phân công kiểm thử tính năng mới (quyết định 49).</summary>
internal sealed class TestAssignmentConfiguration : IEntityTypeConfiguration<TestAssignment>
{
    public void Configure(EntityTypeBuilder<TestAssignment> builder)
    {
        builder.ToTable("TestAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Version).HasMaxLength(TestAssignment.VersionMaxLength).IsRequired();
        builder.Property(a => a.Feature).HasMaxLength(TestAssignment.FeatureMaxLength).IsRequired();
        builder.Property(a => a.Instructions).HasMaxLength(TestAssignment.TextMaxLength);
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.ReviewNote).HasMaxLength(TestAssignment.ReviewNoteMaxLength);
        builder.HasIndex(a => a.AssigneeUserId);
        builder.HasIndex(a => new { a.Version, a.CreatedAtUtc });
        builder.Ignore(a => a.IsCancelled);
        builder.Ignore(a => a.IsReported);
        builder.Ignore(a => a.IsReviewed);
        builder.Ignore(a => a.IsLate);
        builder.HasRowVersion();
    }

    /// <summary>
    /// Lỗi phát hiện và đề xuất do người kiểm thử tự ghi, có thể nhắc tới người bệnh khi kiểm thử trên dữ liệu thật: mã hóa như
    /// phần nhập thêm của báo cáo điều dưỡng.
    /// </summary>
    public static void ConfigureEncryption(ModelBuilder modelBuilder, EncryptedStringConverter encrypted)
    {
        var entity = modelBuilder.Entity<TestAssignment>();
        entity.Property(a => a.Findings).HasColumnName("FindingsEncrypted").IsUnicode(false).HasConversion((ValueConverter)encrypted);
        entity.Property(a => a.Suggestions).HasColumnName("SuggestionsEncrypted").IsUnicode(false).HasConversion((ValueConverter)encrypted);
    }
}
