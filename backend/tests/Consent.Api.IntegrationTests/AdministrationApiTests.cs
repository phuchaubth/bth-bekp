using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Domain.Authorization;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>Quản trị khoa, người dùng, vai trò và nhật ký audit với SQL Server thật.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class AdministrationApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;
    private string _admin = null!;

    public async Task InitializeAsync()
    {
        _client = _factory.CreateHttpsClient();
        _admin = await _client.LoginAsAdministratorAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Department_Create_Update_Delete_AreAudited_WithOldAndNewValues()
    {
        var (created, department) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/departments", _admin, new { code = "cdha", name = "Khoa Chẩn đoán hình ảnh" });
        var id = department.GetProperty("id").GetString();
        var (updated, _) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/departments/{id}", _admin, new { code = "CDHA", name = "Khoa CĐHA", isActive = false });
        var (deleted, _) = await _client.SendJsonAsync(HttpMethod.Delete, $"/api/v1/departments/{id}", _admin);

        Assert.Equal(HttpStatusCode.Created, created);
        Assert.Equal("CDHA", department.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, updated);
        Assert.Equal(HttpStatusCode.NoContent, deleted);

        var (_, audit) = await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/audit-logs?entityType=Department&entityId={id}", _admin);
        var actions = audit.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("action").GetString()).ToList();
        Assert.Equal(["Delete", "Update", "Create"], actions);
        var update = audit.GetProperty("items").EnumerateArray().Single(a => a.GetProperty("action").GetString() == "Update");
        Assert.Equal("admin", update.GetProperty("userName").GetString());
        using var details = JsonDocument.Parse(update.GetProperty("details").GetString()!);
        Assert.Equal("Khoa Chẩn đoán hình ảnh", details.RootElement.GetProperty("Name").GetProperty("old").GetString());
        Assert.Equal("Khoa CĐHA", details.RootElement.GetProperty("Name").GetProperty("new").GetString());
    }

    [Fact]
    public async Task Department_DuplicateCode_Returns409_AndDepartmentInUse_Returns422()
    {
        var (duplicate, problem) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/departments", _admin, new { code = "noi", name = "Trùng mã" });
        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", _admin);
        var noiId = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "NOI").GetProperty("id").GetString();
        var (inUse, inUseProblem) = await _client.SendJsonAsync(HttpMethod.Delete, $"/api/v1/departments/{noiId}", _admin);

        Assert.Equal(HttpStatusCode.Conflict, duplicate);
        Assert.Equal("departments.code_exists", problem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inUse);
        Assert.Equal("departments.in_use", inUseProblem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task User_Create_WithInvalidData_ReturnsVietnameseFieldErrors()
    {
        var (status, problem) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/users", _admin, new
        {
            userName = "có dấu",
            fullName = "",
            email = "sai-email",
            roleIds = Array.Empty<Guid>(),
            password = "123",
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("userName", out _));
        Assert.True(errors.TryGetProperty("fullName", out _));
        Assert.Equal("Email không hợp lệ.", errors.GetProperty("email")[0].GetString());
        Assert.True(errors.TryGetProperty("roleIds", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task User_Created_ByAdmin_MustChangePasswordAtFirstLogin_AndAuditHidesPasswordHash()
    {
        var nurseRoleId = await RoleIdAsync("NURSE");
        var (status, user) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/users", _admin, new
        {
            userName = "dieuduong.moi",
            fullName = "Điều dưỡng Mới",
            title = "Điều dưỡng",
            roleIds = new[] { nurseRoleId },
            password = "Mat-Khau-Tam-2026",
        });
        var login = await _client.LoginAsync("dieuduong.moi", "Mat-Khau-Tam-2026");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(user.GetProperty("mustChangePassword").GetBoolean());
        Assert.True(login.Body.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        Assert.Empty(login.Body.GetProperty("user").GetProperty("permissions").EnumerateArray());

        var (_, audit) = await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/audit-logs?entityType=User&entityId={user.GetProperty("id").GetString()}&action=Create", _admin);
        var details = audit.GetProperty("items")[0].GetProperty("details").GetString()!;
        Assert.Contains("\"PasswordHash\":\"***\"", details, StringComparison.Ordinal);
        Assert.DoesNotContain("AQAAAA", details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task User_Search_IsAccentInsensitive()
    {
        var (_, withAccents) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users?keyword=%C4%91i%E1%BB%81u%20d%C6%B0%E1%BB%A1ng", _admin);
        var (_, withoutAccents) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/users?keyword=dieu%20duong", _admin);

        Assert.Equal("Điều dưỡng Demo", withAccents.GetProperty("items")[0].GetProperty("fullName").GetString());
        Assert.Equal("Điều dưỡng Demo", withoutAccents.GetProperty("items")[0].GetProperty("fullName").GetString());
    }

    [Fact]
    public async Task Admin_CannotLockSelf()
    {
        var (_, me) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/auth/me", _admin);

        var (status, problem) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/users/{me.GetProperty("id").GetString()}/lock", _admin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("users.cannot_lock_self", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RolePermissions_Update_AppliesToNewTokens_AndIsAudited()
    {
        var doctorRoleId = await RoleIdAsync("DOCTOR");

        var (status, role) = await _client.SendJsonAsync(
            HttpMethod.Put,
            $"/api/v1/roles/{doctorRoleId}/permissions",
            _admin,
            new { permissions = new[] { Permissions.ConsentsRead, Permissions.PatientsRead } });
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, role.GetProperty("permissions").GetArrayLength());
        var permissions = doctor.Body.GetProperty("user").GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Equal([Permissions.ConsentsRead, Permissions.PatientsRead], permissions);

        var (_, audit) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/audit-logs?entityType=RolePermission&action=Delete", _admin);
        Assert.True(audit.GetProperty("totalCount").GetInt32() >= 4);
    }

    [Fact]
    public async Task AdministratorRole_PermissionsCannotBeChanged()
    {
        var adminRoleId = await RoleIdAsync("ADMIN");

        var (status, problem) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/roles/{adminRoleId}/permissions", _admin, new { permissions = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Equal("roles.admin_immutable", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AuditLogs_HaveNoModifyApi_AndDatabaseRejectsUpdatesAndDeletes()
    {
        var (_, audit) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/audit-logs?pageSize=1", _admin);
        var id = audit.GetProperty("items")[0].GetProperty("id").GetString();

        var (deleteStatus, _) = await _client.SendJsonAsync(HttpMethod.Delete, $"/api/v1/audit-logs/{id}", _admin);
        var (putStatus, _) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/audit-logs/{id}", _admin, new { action = "x" });
        Assert.Contains(deleteStatus, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });
        Assert.Contains(putStatus, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        foreach (var sqlText in new[] { "UPDATE AuditLogs SET Action = N'Sua'", "DELETE FROM AuditLogs" })
        {
            await using var command = new SqlCommand(sqlText, connection);
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("append-only", error.Message, StringComparison.Ordinal);
        }
    }

    private async Task<string> RoleIdAsync(string code)
    {
        var (_, roles) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/roles", _admin);
        return roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == code).GetProperty("id").GetString()!;
    }
}

/// <summary>Giới hạn tần suất đăng nhập theo địa chỉ IP.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class RateLimitingTests(SqlServerFixture sql) : IAsyncLifetime
{
    private readonly DatabaseApiFactory _factory = new(sql, new Dictionary<string, string?> { ["RateLimiting:AuthPermitLimit"] = "2" });

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task TooManyLoginAttempts_Return429_WithVietnameseMessage()
    {
        using var client = _factory.CreateHttpsClient();

        await client.LoginAsync("bacsi.demo", "Sai-Mat-Khau-1");
        await client.LoginAsync("bacsi.demo", "Sai-Mat-Khau-1");
        var third = await client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.Status);
        Assert.Equal("rate_limited", third.Code);
        Assert.Equal("Bạn thao tác quá nhiều lần. Vui lòng thử lại sau ít phút.", third.Body.GetProperty("title").GetString());
    }
}
