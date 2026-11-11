namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>Giá trị chỉ dùng cho kiểm thử (không phải bí mật thật).</summary>
internal static class TestSettings
{
    public const string SigningKey = "integration-tests-only-signing-key-0123456789";
    public const string AdminPassword = "Quantri@KiemThu2026";
    public const string AdminNewPassword = "Quantri@DaDoi2026";
    public const string DemoPassword = "Demo@KiemThu2026";
    public const string SqlServerImage = "mcr.microsoft.com/mssql/server:2022-latest";
}
