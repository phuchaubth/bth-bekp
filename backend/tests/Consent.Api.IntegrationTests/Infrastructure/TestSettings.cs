namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>Giá trị chỉ dùng cho kiểm thử (không phải bí mật thật).</summary>
internal static class TestSettings
{
    public const string SigningKey = "integration-tests-only-signing-key-0123456789";
    public const string AdminPassword = "Quantri@KiemThu2026";
    public const string AdminNewPassword = "Quantri@DaDoi2026";
    public const string DemoPassword = "Demo@KiemThu2026";
    /// <summary>
    /// Ảnh SQL Server cho kiểm thử tích hợp. Mặc định 2025 (phiên bản bệnh viện dùng, quyết định 16); đặt biến môi trường
    /// <c>CONSENT_TEST_SQL_IMAGE</c> để chạy với phiên bản khác (ví dụ <c>mcr.microsoft.com/mssql/server:2022-latest</c>).
    /// </summary>
    public static string SqlServerImage { get; } =
        Environment.GetEnvironmentVariable("CONSENT_TEST_SQL_IMAGE") is { Length: > 0 } image
            ? image
            : "mcr.microsoft.com/mssql/server:2025-latest";

    // Khóa bảo vệ dữ liệu chỉ dùng cho kiểm thử (Base64 của 32 byte cố định).
    public const string FieldEncryptionKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    public const string BlindIndexKey = "ZWZnaGlqa2xtbm9wcXJzdHV2d3h5ent8fX5/gIGCg4Q=";

    /// <summary>Cấu hình bắt buộc dùng chung cho mọi factory kiểm thử.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> RequiredSecrets() =>
    [
        new("Jwt:SigningKey", SigningKey),
        new("DataProtection:FieldEncryptionKey", FieldEncryptionKey),
        new("DataProtection:BlindIndexKey", BlindIndexKey),
        new("His:FakeLatencyMilliseconds", "0"),
    ];
}
