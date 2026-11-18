namespace Consent.Persistence.Configurations;

internal static class Collations
{
    /// <summary>
    /// Không phân biệt hoa/thường và dấu, kể cả Đ/D: tìm "nguyen van duc" khớp "Nguyễn Văn Đức".
    /// Không dùng collation Vietnamese_* vì collation đó coi ư, ơ, ê, đ… là chữ cái riêng
    /// nên "duong" không khớp "dưỡng" (người dùng thường gõ không dấu khi tìm kiếm).
    /// </summary>
    public const string AccentInsensitive = "Latin1_General_100_CI_AI";
}
