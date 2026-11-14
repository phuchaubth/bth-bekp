using System.Net;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Domain.Authorization;

namespace Consent.Api.IntegrationTests;

/// <summary>Luồng đăng nhập/phiên với SQL Server thật (Testcontainers).</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AuthFlowTests(SqlServerFixture sql) : IAsyncLifetime
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
    public async Task Login_SetsHardenedRefreshCookie_AndMeReturnsPermissions()
    {
        var login = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        Assert.Equal(HttpStatusCode.OK, login.Status);
        Assert.NotNull(login.AccessToken);
        Assert.False(login.Body.TryGetProperty("refreshToken", out _), "Refresh token không được nằm trong body.");
        var cookie = login.SetCookieHeader!.ToLowerInvariant();
        Assert.Contains("httponly", cookie, StringComparison.Ordinal);
        Assert.Contains("secure", cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=strict", cookie, StringComparison.Ordinal);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.Ordinal);

        var (status, me) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/auth/me", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("bacsi.demo", me.GetProperty("userName").GetString());
        Assert.Equal("Khoa Nội tổng hợp", me.GetProperty("departmentName").GetString());
        var permissions = me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(Permissions.ConsentsCreate, permissions);
        Assert.DoesNotContain(Permissions.UsersManage, permissions);
    }

    [Fact]
    public async Task UnknownUser_AndWrongPassword_GetTheSameGenericError()
    {
        var unknown = await _client.LoginAsync("khong.ton.tai", "Sai-Mat-Khau-1");
        var wrong = await _client.LoginAsync("bacsi.demo", "Sai-Mat-Khau-1");

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.Status);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.Status);
        Assert.Equal("auth.invalid_credentials", unknown.Code);
        Assert.Equal(unknown.Body.GetProperty("title").GetString(), wrong.Body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task FiveWrongPasswords_LockAccount_CorrectPasswordRejected_AndLockoutAudited()
    {
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal("auth.invalid_credentials", (await _client.LoginAsync("tiepdon.demo", "Sai-Mat-Khau-1")).Code);
        }

        var fifth = await _client.LoginAsync("tiepdon.demo", "Sai-Mat-Khau-1");
        var correct = await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword);

        Assert.Equal("auth.locked_out", fifth.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, correct.Status);
        Assert.Equal("auth.locked_out", correct.Code);
        Assert.Contains("Vui lòng thử lại sau", correct.Body.GetProperty("title").GetString(), StringComparison.Ordinal);

        var admin = await _client.LoginAsAdministratorAsync();
        var (_, audit) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/audit-logs?action=Auth.LockedOut", admin);
        Assert.Equal(1, audit.GetProperty("totalCount").GetInt32());
        Assert.Equal("tiepdon.demo", audit.GetProperty("items")[0].GetProperty("userName").GetString());
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndReusingOldTokenRevokesWholeFamily()
    {
        var login = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        var first = await _client.RefreshAsync(login.RefreshCookie);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.NotEqual(login.RefreshCookie, first.RefreshCookie);

        var reuse = await _client.RefreshAsync(login.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.Status);
        Assert.Equal("auth.refresh_invalid", reuse.Code);

        // Token mới nhất cũng bị thu hồi vì cả "họ" token đã bị coi là lộ.
        var afterReuse = await _client.RefreshAsync(first.RefreshCookie);
        Assert.Equal(HttpStatusCode.Unauthorized, afterReuse.Status);

        var admin = await _client.LoginAsAdministratorAsync();
        var (_, audit) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/audit-logs?action=Auth.RefreshTokenReuseDetected", admin);
        Assert.Equal(1, audit.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken_AndClearsCookie()
    {
        var login = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        Assert.Equal(HttpStatusCode.NoContent, await _client.LogoutAsync(login.RefreshCookie!));
        var refresh = await _client.RefreshAsync(login.RefreshCookie);

        Assert.Equal(HttpStatusCode.Unauthorized, refresh.Status);
        Assert.Contains("expires=thu, 01 jan 1970", refresh.SetCookieHeader!.ToLowerInvariant(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialAdministrator_MustChangePassword_BeforeGettingPermissions()
    {
        var login = await _client.LoginAsync("admin", TestSettings.AdminPassword);
        Assert.True(login.Body.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());

        var (before, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users", login.AccessToken);
        var admin = await _client.LoginAsAdministratorAsync();
        var (after, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users", admin);

        Assert.Equal(HttpStatusCode.Forbidden, before);
        Assert.Equal(HttpStatusCode.OK, after);
    }

    [Fact]
    public async Task LockedUser_CannotRefreshOrLogIn()
    {
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);
        var admin = await _client.LoginAsAdministratorAsync();
        var (_, users) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users?keyword=bacsi.demo", admin);
        var doctorId = users.GetProperty("items")[0].GetProperty("id").GetString();

        var (lockStatus, _) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/users/{doctorId}/lock", admin);
        var refresh = await _client.RefreshAsync(doctor.RefreshCookie);
        var login = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        Assert.Equal(HttpStatusCode.NoContent, lockStatus);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.Status);
        Assert.Equal("auth.inactive", login.Code);
    }

    [Fact]
    public async Task UserWithoutPermission_IsForbidden()
    {
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        var (users, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users", doctor.AccessToken);
        var (create, _) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/departments", doctor.AccessToken, new { code = "X", name = "X" });
        var (departments, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", doctor.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, users);
        Assert.Equal(HttpStatusCode.Forbidden, create);
        Assert.Equal(HttpStatusCode.OK, departments);
    }
}
