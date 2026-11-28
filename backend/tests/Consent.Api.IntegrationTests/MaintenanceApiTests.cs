using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Consent.Api.Infrastructure;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Application.Abstractions;
using Consent.Application.Maintenance;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Chế độ bảo trì (quyết định 53) qua API với SQL Server thật: quản trị bật, ghi lý do và giờ dự kiến xong; người không có quyền
/// bảo trì (bác sĩ, kiosk, người bệnh ký trên máy tính bảng hoặc qua mã QR) không đăng nhập, không thao tác được; quản trị vẫn làm
/// việc; tắt bảo trì thì mọi người làm tiếp bằng phiên cũ.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class MaintenanceApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string TemporaryPassword = "Kiosk@TamThoi2026";
    private const string KioskPassword = "Kiosk@KiemThu2026";
    private DatabaseApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        // Không giữ trạng thái trong bộ nhớ để test thấy ngay thay đổi thẳng trong CSDL (tắt khẩn cấp bằng SQL).
        _factory = new DatabaseApiFactory(
            sql,
            testServices: services => services.Replace(ServiceDescriptor.Singleton(new MaintenanceStateCache(TimeSpan.Zero))));
        _client = _factory.CreateHttpsClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<JsonElement> SwitchAsync(string admin, bool enabled, string? reason = null, DateTimeOffset? expectedEndAtUtc = null)
    {
        var (status, body) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/maintenance/settings", admin, new { enabled, reason, expectedEndAtUtc });
        Assert.True(status == HttpStatusCode.OK, body.ToString());
        Assert.Equal(enabled, body.GetProperty("enabled").GetBoolean());
        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(body);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>Phản hồi 503 "đang bảo trì": mã lỗi, lý do, giờ dự kiến xong, gợi ý hỏi lại sau 30 giây.</summary>
    private static async Task AssertBlockedAsync(HttpResponseMessage response, string reason, DateTimeOffset? expectedEnd)
    {
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(TimeSpan.FromSeconds(MaintenanceModeMiddleware.RetryAfterSeconds), response.Headers.RetryAfter?.Delta);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(MaintenanceService.ErrorCode, body.RootElement.GetProperty("code").GetString());
        Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
        if (expectedEnd is null)
        {
            Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("expectedEndAtUtc").ValueKind);
        }
        else
        {
            Assert.Equal(expectedEnd, body.RootElement.GetProperty("expectedEndAtUtc").GetDateTimeOffset());
        }
    }

    /// <summary>Tài khoản kiosk mới của Khoa Khám bệnh (đã đổi mật khẩu lần đầu).</summary>
    private async Task<string> NewKioskAccountAsync(string admin)
    {
        var (_, roles) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/roles", admin);
        var kioskRole = roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "KIOSK").GetProperty("id").GetString();
        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", admin);
        var kkb = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "KKB").GetProperty("id").GetString();
        var userName = $"kiosk.{Guid.NewGuid():N}"[..20];
        var (created, user) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/users", admin, new
        {
            userName,
            fullName = "Kiosk Khoa Khám bệnh",
            departmentId = kkb,
            roleIds = new[] { kioskRole },
            password = TemporaryPassword,
        });
        Assert.True(created == HttpStatusCode.Created, user.ToString());
        var login = await _client.LoginAsync(userName, TemporaryPassword);
        var (changed, session) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/auth/change-password", login.AccessToken,
            new { currentPassword = TemporaryPassword, newPassword = KioskPassword });
        Assert.True(changed == HttpStatusCode.OK, session.ToString());
        return session.GetProperty("accessToken").GetString()!;
    }

    private async Task<int> CountAuditAsync(string action, string? detailsContains = null)
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM AuditLogs WHERE Action = @action AND (@details IS NULL OR Details LIKE '%' + @details + '%')", connection);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@details", (object?)detailsContains ?? DBNull.Value);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task Status_IsPublic_AndOffByDefault_SettingsNeedTheMaintenancePermission()
    {
        var (status, body) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance", null);
        var doctor = (await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword)).AccessToken;
        var admin = await _client.LoginAsAdministratorAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("expectedEndAtUtc").ValueKind);
        Assert.False(body.TryGetProperty("startedBy", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance/settings", null)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance/settings", doctor)).Status);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/maintenance/settings", doctor, new { enabled = true, reason = "Thử" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance/settings", admin)).Status);
        var (invalid, problem) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/maintenance/settings", admin, new { enabled = true, reason = " " });
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.True(problem.GetProperty("errors").TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task UnderMaintenance_OnlyMaintenancePermissionGetsIn_EverythingElseWaits_AndResumesAfterwardsWithTheSameSessions()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var doctorLogin = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);
        var doctor = doctorLogin.AccessToken!;
        var kiosk = await NewKioskAccountAsync(admin);
        var signingToken = _factory.Services.GetRequiredService<ITokenService>().CreateSigningToken(
            new SigningTokenClaims(Guid.NewGuid(), "dieuduong.demo", [Guid.NewGuid()], Guid.NewGuid())).Value;
        var expectedEnd = DateTimeOffset.UtcNow.AddHours(1);

        var enabled = await SwitchAsync(admin, true, "Nâng cấp phần mềm", expectedEnd);

        Assert.Contains("(admin)", enabled.GetProperty("startedBy").GetString(), StringComparison.Ordinal);
        var (_, status) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance", null);
        Assert.True(status.GetProperty("enabled").GetBoolean());
        Assert.Equal("Nâng cấp phần mềm", status.GetProperty("reason").GetString());

        // Bác sĩ đang đăng nhập: token còn hạn nhưng mọi thao tác đều chờ; làm mới phiên bị từ chối nhưng cookie được giữ.
        using (var list = await SendAsync(HttpMethod.Get, "/api/v1/consent-forms", doctor))
        {
            await AssertBlockedAsync(list, "Nâng cấp phần mềm", expectedEnd);
        }

        using (var write = await SendAsync(HttpMethod.Post, "/api/v1/patients", doctor, new { fullName = "Người Bệnh Giả Lập" }))
        {
            await AssertBlockedAsync(write, "Nâng cấp phần mềm", expectedEnd);
        }

        var refresh = await _client.RefreshAsync(doctorLogin.RefreshCookie);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, refresh.Status);
        Assert.Equal(MaintenanceService.ErrorCode, refresh.Body.GetProperty("code").GetString());
        Assert.Null(refresh.SetCookieHeader);

        // Đăng nhập mới: đúng mật khẩu thì báo đang bảo trì, sai mật khẩu vẫn báo sai.
        var login = await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, login.Status);
        Assert.Equal(MaintenanceService.ErrorCode, login.Body.GetProperty("code").GetString());
        Assert.Equal("Nâng cấp phần mềm", login.Body.GetProperty("reason").GetString());
        Assert.Null(login.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.LoginAsync("dieuduong.demo", "Sai@MatKhau2026")).Status);

        // Kiosk, người bệnh ký trên máy tính bảng (token phiên ký), người bệnh ký qua mã QR (không đăng nhập): đều chờ.
        using (var kioskConfig = await SendAsync(HttpMethod.Get, "/api/v1/kiosk/config", kiosk))
        {
            await AssertBlockedAsync(kioskConfig, "Nâng cấp phần mềm", expectedEnd);
        }

        using (var tablet = await SendAsync(HttpMethod.Post, $"/api/v1/consent-forms/{Guid.NewGuid()}/patient-signature", signingToken, new { }))
        {
            await AssertBlockedAsync(tablet, "Nâng cấp phần mềm", expectedEnd);
        }

        using (var qr = await SendAsync(HttpMethod.Post, "/api/v1/remote-signing/claim", null, new { code = "123456", dateOfBirth = "1985-03-12" }))
        {
            await AssertBlockedAsync(qr, "Nâng cấp phần mềm", expectedEnd);
        }

        // Token sai hoặc hết hạn: 401 như thường để giao diện làm mới phiên (làm mới phiên tự quyết cho vào hay không).
        using (var bogus = await SendAsync(HttpMethod.Get, "/api/v1/consent-forms", "khong.phai.token"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, bogus.StatusCode);
        }

        // Vẫn trả lời: kiểm tra sức khỏe (script cài đặt), nhãn môi trường.
        foreach (var path in new[] { "/health", "/health/ready", "/api/v1/environment" })
        {
            using var response = await _client.GetAsync(new Uri(path, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Quản trị vẫn làm việc: đọc, sửa cấu hình, đăng nhập lại, làm mới phiên.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates", admin)).Status);
        Assert.Equal(
            HttpStatusCode.OK,
            (await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/form-inheritance", admin, new { days = 7 })).Status);
        var adminAgain = await _client.LoginAsync("admin", TestSettings.AdminNewPassword);
        Assert.Equal(HttpStatusCode.OK, adminAgain.Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(adminAgain.RefreshCookie)).Status);
        var updated = await SwitchAsync(admin, true, "Nâng cấp phần mềm, xong lúc 11 giờ", expectedEnd.AddHours(1));
        Assert.Equal(enabled.GetProperty("startedAtUtc").GetDateTimeOffset(), updated.GetProperty("startedAtUtc").GetDateTimeOffset());

        await SwitchAsync(admin, false);

        // Làm tiếp bằng phiên cũ: token còn hạn dùng được ngay, refresh token cũ vẫn làm mới được phiên.
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", doctor)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.RefreshAsync(doctorLogin.RefreshCookie)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/kiosk/config", kiosk)).Status);
        Assert.Equal(HttpStatusCode.OK, (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).Status);
        var (_, off) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/maintenance", null);
        Assert.False(off.GetProperty("enabled").GetBoolean());

        Assert.Equal(1, await CountAuditAsync("Maintenance.Enabled", "Nâng cấp phần mềm"));
        Assert.Equal(1, await CountAuditAsync("Maintenance.Updated", "xong lúc 11 giờ"));
        Assert.Equal(1, await CountAuditAsync("Maintenance.Disabled"));
        Assert.Equal(1, await CountAuditAsync("Auth.LoginFailed", "maintenance"));
    }

    [Fact]
    public async Task UnderMaintenance_LoggingOutStillWorks()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);
        await SwitchAsync(admin, true, "Sửa cấu hình");

        Assert.Equal(HttpStatusCode.NoContent, await _client.LogoutAsync(doctor.RefreshCookie!));

        await SwitchAsync(admin, false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.RefreshAsync(doctor.RefreshCookie)).Status);
    }

    [Fact]
    public async Task EmergencyOff_DeletingTheMaintenanceRowsInTheDatabase_LetsEveryoneBackIn()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var doctor = (await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword)).AccessToken;
        await SwitchAsync(admin, true, "Sửa CSDL", DateTimeOffset.UtcNow.AddMinutes(30));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", doctor)).Status);

        // Lệnh tắt khẩn cấp trong docs/trien-khai.md (khi không còn tài khoản quản trị nào đăng nhập được).
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("DELETE FROM dbo.SystemSettings WHERE [Key] LIKE N'Maintenance.%';", connection);
            Assert.Equal(4, await command.ExecuteNonQueryAsync());
        }

        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", doctor)).Status);
    }
}

/// <summary>Danh sách endpoint vẫn trả lời khi đang bảo trì.</summary>
public sealed class MaintenanceEndpointTests(ConsentApiFactory factory) : IClassFixture<ConsentApiFactory>
{
    [Fact]
    public void OnlyHealthEnvironmentStatusAndSignInEndpoints_AnswerDuringMaintenance()
    {
        using var client = factory.CreateClient();
        var allowed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<AllowDuringMaintenanceAttribute>() is not null)
            .Select(e => string.Join(",", e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["*"]) + " " + e.RoutePattern.RawText!.Trim('/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            [
                "* health",
                "* health/ready",
                "GET api/v1/environment",
                "GET api/v1/maintenance",
                "POST api/v1/auth/login",
                "POST api/v1/auth/logout",
                "POST api/v1/auth/refresh",
            ],
            allowed);
    }
}
