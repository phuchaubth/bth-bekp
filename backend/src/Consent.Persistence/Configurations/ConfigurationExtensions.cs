using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Consent.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public const string RowVersion = "RowVersion";

    /// <summary>Cột rowversion (shadow) để phát hiện cập nhật đồng thời.</summary>
    public static void HasRowVersion<T>(this EntityTypeBuilder<T> builder)
        where T : class =>
        builder.Property<byte[]>(RowVersion).IsRowVersion();
}
