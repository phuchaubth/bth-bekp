namespace Consent.Persistence.Seeding;

/// <summary>
/// Cấu hình khởi tạo dữ liệu (mục <c>Seed</c>). Mật khẩu chỉ cấu hình qua biến môi trường/user-secrets,
/// không ghi trong appsettings.
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Seed";

    /// <summary>Chạy khởi tạo dữ liệu khi API khởi động (vai trò hệ thống, tài khoản quản trị đầu tiên).</summary>
    public bool Enabled { get; set; }

    public string AdminUserName { get; set; } = "admin";

    public string AdminFullName { get; set; } = "Quản trị hệ thống";

    /// <summary>Mật khẩu tài khoản quản trị đầu tiên. Chỉ dùng khi cơ sở dữ liệu chưa có người dùng nào.</summary>
    public string? AdminPassword { get; set; }

    /// <summary>Tạo dữ liệu minh họa (khoa và tài khoản demo). Chỉ dùng cho môi trường phát triển/thử nghiệm.</summary>
    public bool DemoData { get; set; }

    /// <summary>Mật khẩu chung cho các tài khoản demo.</summary>
    public string? DemoPassword { get; set; }
}
