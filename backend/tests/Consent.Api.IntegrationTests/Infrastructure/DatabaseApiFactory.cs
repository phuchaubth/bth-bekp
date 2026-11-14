using Consent.Domain.ConsentTemplates;
using Consent.Persistence;
using Consent.Tests.Samples;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Consent.Api.IntegrationTests.Infrastructure;

/// <summary>
/// API kèm SQL Server thật (Testcontainers), database mới cho mỗi factory: chạy migration và seed
/// (vai trò hệ thống, tài khoản quản trị, khoa và tài khoản demo, bốn mẫu thật). Thêm ba mẫu minh họa
/// (<see cref="SampleConsentTemplates"/>, đã phát hành) mà các kịch bản kiểm thử dùng.
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddHostedService<SampleTemplateSeeder>());
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

/// <summary>Chạy sau migration và seed của ứng dụng (khi host khởi động): thêm mẫu minh họa còn thiếu.</summary>
internal sealed class SampleTemplateSeeder(IServiceScopeFactory scopes, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ConsentDbContext>();
        var existing = await db.ConsentTemplates.Select(t => t.Code).ToListAsync(cancellationToken);
        var now = time.GetUtcNow();
        foreach (var sample in SampleConsentTemplates.All.Where(t => !existing.Contains(t.Code)))
        {
            var template = ConsentTemplate.Create(sample.Code, sample.Name, sample.HospitalSignerRoleCode, null, sample.ContentJson, now);
            template.Publish(1, now);
            db.ConsentTemplates.Add(template);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
