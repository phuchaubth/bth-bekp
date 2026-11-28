using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Consent.Api.Infrastructure;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Domain.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Phiên bản bản đang chạy (quyết định 67): API đọc version.json do quy trình đóng gói ghi, chỉ người có quyền bảo trì hệ thống
/// xem; không có file hoặc file hỏng thì các giá trị là null, API vẫn chạy.
/// </summary>
public sealed class VersionApiTests : IDisposable
{
    private const string Commit = "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "consent-version-" + Guid.NewGuid().ToString("N"));

    public VersionApiTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private AppVersion LoadFile(string content)
    {
        File.WriteAllText(Path.Combine(_directory, AppVersion.FileName), content);
        return AppVersion.Load(_directory);
    }

    private static async Task<(HttpStatusCode Status, string Body)> GetAsync(ConsentApiFactory factory, string? token)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/version");
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        using var response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OnlyMaintenancePermission_SeesTheVersion()
    {
        await using var factory = new VersionFactory(new AppVersion(97, Commit, new DateTimeOffset(2026, 11, 28, 7, 5, 0, TimeSpan.Zero)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAsync(factory, token: null)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await GetAsync(factory, factory.CreateAccessToken(Permissions.SettingsManage, Permissions.UsersManage))).Status);

        var (status, body) = await GetAsync(factory, factory.CreateAccessToken(Permissions.SystemMaintenance));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(97, json.RootElement.GetProperty("package").GetInt32());
        Assert.Equal(Commit, json.RootElement.GetProperty("commit").GetString());
        Assert.Equal(
            new DateTimeOffset(2026, 11, 28, 7, 5, 0, TimeSpan.Zero),
            json.RootElement.GetProperty("builtAtUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task ReadsVersionFileNextToTheApi_OnceAtStartup()
    {
        // Cùng lớp với WithoutVersionFile_ValuesAreNull nên hai test không chạy song song; file luôn được xóa.
        var file = Path.Combine(AppContext.BaseDirectory, AppVersion.FileName);
        File.WriteAllText(file, $"{{\"package\":97,\"commit\":\"{Commit}\",\"builtAtUtc\":\"2026-11-28T07:05:09Z\"}}\n");
        try
        {
            await using var factory = new ConsentApiFactory();
            _ = factory.Services;
            // Chép đè file khi API đang chạy (chưa khởi động lại): API vẫn báo phiên bản của mã đang chạy.
            File.WriteAllText(file, $"{{\"package\":98,\"commit\":\"{Commit}\"}}\n");

            var (status, body) = await GetAsync(factory, factory.CreateAccessToken(Permissions.SystemMaintenance));

            Assert.Equal(HttpStatusCode.OK, status);
            using var json = JsonDocument.Parse(body);
            Assert.Equal(97, json.RootElement.GetProperty("package").GetInt32());
            Assert.Equal(
                new DateTimeOffset(2026, 11, 28, 7, 5, 9, TimeSpan.Zero),
                json.RootElement.GetProperty("builtAtUtc").GetDateTimeOffset());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task WithoutVersionFile_ValuesAreNull()
    {
        // Thư mục chạy test không có version.json (giống bản phát triển, bản cài từ gói chưa ghi phiên bản).
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, AppVersion.FileName)));
        await using var factory = new ConsentApiFactory();

        var (status, body) = await GetAsync(factory, factory.CreateAccessToken(Permissions.SystemMaintenance));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("package").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("commit").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("builtAtUtc").ValueKind);
    }

    [Fact]
    public void Load_ReadsTheFileWrittenByThePackageWorkflow()
    {
        // Đúng dòng lệnh printf trong .github/workflows/package-iis.yml (có xuống dòng cuối).
        var version = LoadFile($"{{\"package\":97,\"commit\":\"{Commit}\",\"builtAtUtc\":\"2026-11-28T07:05:09Z\"}}\n");

        Assert.Equal(new AppVersion(97, Commit, new DateTimeOffset(2026, 11, 28, 7, 5, 9, TimeSpan.Zero)), version);
    }

    [Fact]
    public void Load_MissingFile_IsUnknown() => Assert.Equal(AppVersion.Unknown, AppVersion.Load(_directory));

    [Fact]
    public void Load_FileLargerThanTheLimit_IsNotRead()
    {
        var padding = new string(' ', AppVersion.MaxFileBytes);

        Assert.Equal(AppVersion.Unknown, LoadFile($"{{\"package\":97,\"commit\":\"{Commit}\"}}{padding}"));
        Assert.Equal(97, LoadFile($"{{\"package\":97}}{padding[..(AppVersion.MaxFileBytes - 14)]}").Package);
    }

    [Theory]
    [InlineData("")]
    [InlineData("không phải JSON")]
    [InlineData("[97]")]
    [InlineData("97")]
    [InlineData("{\"package\":97")]
    public void Parse_NotAJsonObject_IsUnknown(string content) => Assert.Equal(AppVersion.Unknown, LoadFile(content));

    [Theory]
    [InlineData("\"package\":0")]
    [InlineData("\"package\":-3")]
    [InlineData("\"package\":97.5")]
    [InlineData("\"package\":\"97\"")]
    [InlineData("\"package\":3000000000")]
    [InlineData("\"package\":null")]
    public void Parse_InvalidPackage_IsNull_OtherValuesKept(string package)
    {
        var version = LoadFile($"{{{package},\"commit\":\"{Commit}\",\"builtAtUtc\":\"2026-11-28T07:05:09Z\"}}");

        Assert.Null(version.Package);
        Assert.Equal(Commit, version.Commit);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 7, 5, 9, TimeSpan.Zero), version.BuiltAtUtc);
    }

    [Theory]
    // Mã viết hoa, mã rút gọn, dài hơn 40 ký tự, ký tự ngoài 0-9a-f, không phải chuỗi.
    [InlineData("\"1A2B3C4D5E6F708192A3B4C5D6E7F8091A2B3C4D\"")]
    [InlineData("\"1a2b3c4\"")]
    [InlineData("\"1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d0\"")]
    [InlineData("\"1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4g\"")]
    [InlineData("\"<script>alert(1)</script>00000000000000000\"")]
    [InlineData("12345")]
    public void Parse_InvalidCommit_IsNull(string commit)
    {
        var version = LoadFile($"{{\"package\":97,\"commit\":{commit}}}");

        Assert.Equal(97, version.Package);
        Assert.Null(version.Commit);
    }

    [Theory]
    [InlineData("\"28/11/2026 07:05\"")]
    [InlineData("\"2026-11-28T07:05:09+07:00\"")]
    [InlineData("\"2026-11-28\"")]
    [InlineData("\"2026-13-28T07:05:09Z\"")]
    [InlineData("1764313509")]
    public void Parse_InvalidBuildTime_IsNull(string builtAt)
    {
        var version = LoadFile($"{{\"package\":97,\"builtAtUtc\":{builtAt}}}");

        Assert.Equal(97, version.Package);
        Assert.Null(version.BuiltAtUtc);
    }

    private sealed class VersionFactory(AppVersion version) : ConsentApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(version)));
        }
    }
}
