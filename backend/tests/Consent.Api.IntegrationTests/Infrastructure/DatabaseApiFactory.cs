namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>
/// API kèm SQL Server thật (Testcontainers), database mới cho mỗi factory: chạy migration và seed
/// (vai trò hệ thống, tài khoản quản trị, khoa và tài khoản demo).
/// </summary>
public sealed class DatabaseApiFactory(SqlServerFixture sql, IReadOnlyDictionary<string, string?>? overrides = null)
    : ConsentApiFactory
{
    public string DatabaseName { get; } = "consent_test_" + Guid.NewGuid().ToString("N");

    public string ConnectionString => sql.CreateConnectionString(DatabaseName);

    /// <summary>Thư mục lưu PDF đã ký riêng cho factory này (xóa khi factory bị hủy).</summary>
    public string DocumentsPath { get; } = Path.Combine(Path.GetTempPath(), "consent-test-docs-" + Guid.NewGuid().ToString("N"));

    protected override IReadOnlyDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>(TestSettings.RequiredSecrets())
            {
                ["ConnectionStrings:Default"] = ConnectionString,
                ["Storage:DocumentsPath"] = DocumentsPath,
                ["Database:ApplyMigrationsOnStartup"] = "true",
                ["Seed:Enabled"] = "true",
                ["Seed:AdminPassword"] = TestSettings.AdminPassword,
                ["Seed:DemoData"] = "true",
                ["Seed:DemoPassword"] = TestSettings.DemoPassword,
                ["RateLimiting:AuthPermitLimit"] = "1000",
                ["RateLimiting:RefreshPermitLimit"] = "1000",
                // Kiểm thử phát hiện dùng lại token ngay lập tức (không có thời gian ân hạn).
                ["Auth:RefreshReuseGraceSeconds"] = "0",
            };
            foreach (var (key, value) in overrides ?? new Dictionary<string, string?>())
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (Directory.Exists(DocumentsPath))
        {
            Directory.Delete(DocumentsPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }
}
