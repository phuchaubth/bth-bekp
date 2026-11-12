using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>Cấu hình hệ thống (màn hình 19) và các kiểm tra siết cấu hình của Sprint 6 với SQL Server thật.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class SettingsApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _client = _factory.CreateHttpsClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task HospitalName_IsSavedByAdministrators_Audited_AndPrintedOnPdfs()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var nurse = (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!;

        var (defaultsStatus, defaults) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/settings", admin);
        var (nurseStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/settings", nurse);
        var (invalidStatus, invalid) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/settings/hospital", admin, new { parentAgency = "SỞ Y TẾ THỬ NGHIỆM", name = "", place = "Thành phố Mẫu" });
        var (savedStatus, saved) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/settings/hospital", admin,
            new { parentAgency = "SỞ Y TẾ THỬ NGHIỆM", name = "BỆNH VIỆN GIẢ LẬP SPRINT 6", place = "Thành phố Mẫu" });

        Assert.Equal(HttpStatusCode.OK, defaultsStatus);
        Assert.Equal("BỆNH VIỆN [TÊN BỆNH VIỆN]", defaults.GetProperty("hospital").GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, nurseStatus);
        Assert.Equal(HttpStatusCode.BadRequest, invalidStatus);
        Assert.True(invalid.GetProperty("errors").TryGetProperty("name", out _));
        Assert.Equal(HttpStatusCode.OK, savedStatus);
        Assert.Equal("BỆNH VIỆN GIẢ LẬP SPRINT 6", saved.GetProperty("hospital").GetProperty("name").GetString());
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, saved.GetProperty("hospital").GetProperty("updatedAtUtc").ValueKind);

        // Bản xem trước PDF của một phiếu mới dùng tên vừa lưu.
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", nurse);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString();
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", nurse);
        var templateId = available.EnumerateArray().First().GetProperty("templateId").GetString();
        var (_, form) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId });
        using var pdfRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/consent-forms/{form.GetProperty("id").GetString()}/pdf");
        pdfRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", nurse);
        using var pdf = await _client.SendAsync(pdfRequest);
        Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
        // Phông được nhúng theo bảng mã riêng nên không tìm chuỗi trực tiếp; kiểm tra bằng số byte đủ lớn và đầu file.
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.Latin1.GetString(bytes, 0, 4), StringComparison.Ordinal);

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var count = new SqlCommand("SELECT COUNT(*) FROM SystemSettings", connection);
        Assert.Equal(3, (int)(await count.ExecuteScalarAsync())!);
        await using var audit = new SqlCommand(
            "SELECT COUNT(*) FROM AuditLogs WHERE EntityType = 'SystemSetting' AND Action = 'Create' AND UserName = 'admin'", connection);
        Assert.Equal(3, (int)(await audit.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task ApiResponses_AreNotCacheable_AndOversizedBodiesAreRejected()
    {
        var admin = await _client.LoginAsAdministratorAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/settings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var response = await _client.SendAsync(request);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("camera=(), microphone=(), geolocation=()", response.Headers.GetValues("Permissions-Policy").Single());

        // Thân yêu cầu 3 MB vượt giới hạn 2 MB của Kestrel: bị từ chối ngay (không vào controller).
        using var big = new HttpRequestMessage(HttpMethod.Put, "/api/v1/settings/hospital")
        {
            Content = new StringContent("{\"name\":\"" + new string('x', 3 * 1024 * 1024) + "\"}", Encoding.UTF8, "application/json"),
        };
        big.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin);
        using var bigResponse = await _client.SendAsync(big);
        Assert.True(
            bigResponse.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest,
            $"Mã trả về: {bigResponse.StatusCode}");
    }

    [Fact]
    public async Task DemoData_IsRefusedInProduction()
    {
        await using var factory = new ProductionDemoFactory();

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Seed:DemoData", error.ToString(), StringComparison.Ordinal);
    }

    private sealed class ProductionDemoFactory : ConsentApiFactory
    {
        protected override IReadOnlyDictionary<string, string?> Settings =>
            new Dictionary<string, string?>(base.Settings) { ["Seed:DemoData"] = "true" };

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseEnvironment("Production");
        }
    }
}
