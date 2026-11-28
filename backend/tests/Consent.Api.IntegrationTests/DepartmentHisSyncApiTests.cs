using System.Net;
using System.Text;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Infrastructure.PatientSource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Danh mục khoa/phòng lấy từ HIS (quyết định 42) qua API thông tin người bệnh giả lập theo đúng hợp đồng
/// <c>GET api/v1/departments</c>, với SQL Server thật (chỉ mục duy nhất trên mã khoa). Dữ liệu giả lập.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class DepartmentHisSyncApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001";
    private readonly FakeDepartmentApi _api = new();
    private DatabaseApiFactory _factory = null!;
    private HttpClient _client = null!;

    public Task InitializeAsync()
    {
        _factory = new DatabaseApiFactory(
            sql,
            new Dictionary<string, string?>
            {
                ["PatientSource:Provider"] = "Api",
                ["PatientSource:BaseUrl"] = "https://thongtinbn.gia-lap.local/",
                ["PatientSource:ApiKey"] = ApiKey,
            },
            services => services.Replace(ServiceDescriptor.Singleton(new PatientSourceHttp(_api))));
        _client = _factory.CreateHttpsClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task<Dictionary<string, (string Id, string Name, bool Active)>> DepartmentsAsync(string token)
    {
        var (status, body) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments?includeInactive=true", token);
        Assert.Equal(HttpStatusCode.OK, status);
        return body.EnumerateArray().ToDictionary(
            d => d.GetProperty("code").GetString()!,
            d => (d.GetProperty("id").GetString()!, d.GetProperty("name").GetString()!, d.GetProperty("isActive").GetBoolean()));
    }

    [Fact]
    public async Task Sync_TakesTheHisCatalog_KeepingDepartmentsMatchedByCodeOrName()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var before = await DepartmentsAsync(admin);
        // Danh mục demo: KKB, NOI, KHTH, PDD, HSH. HIS: hai khoa cùng tên nhưng mã khác, PDD cùng mã, một khoa mới; không có KHTH, HSH.
        _api.Departments =
        [
            new("K01", "Khoa Khám bệnh"),
            new("K02", " khoa nội  tổng hợp "),
            new("PDD", "Phòng Điều dưỡng"),
            new("K05", "Khoa Ngoại (giả lập)"),
        ];

        var (status, result) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/departments/sync-his", admin);

        Assert.True(status == HttpStatusCode.OK, result.ToString());
        Assert.Equal(
            (4, 1, 2, 1),
            (result.GetProperty("total").GetInt32(), result.GetProperty("added").GetInt32(), result.GetProperty("updated").GetInt32(),
                result.GetProperty("unchanged").GetInt32()));
        Assert.Equal(["Khoa Huyết học – Sinh hóa", "Phòng Kế hoạch tổng hợp"], result.GetProperty("deactivated").EnumerateArray().Select(d => d.GetString()));
        Assert.Equal(ApiKey, _api.ApiKey);

        var after = await DepartmentsAsync(admin);
        Assert.Equal(["HSH", "K01", "K02", "K05", "KHTH", "PDD"], after.Keys.Order(StringComparer.Ordinal));
        Assert.Equal((before["KKB"].Id, "Khoa Khám bệnh", true), after["K01"]);
        Assert.Equal((before["NOI"].Id, "khoa nội  tổng hợp", true), after["K02"]);
        Assert.Equal((before["PDD"].Id, "Phòng Điều dưỡng", true), after["PDD"]);
        Assert.Equal((before["KHTH"].Id, "Phòng Kế hoạch tổng hợp", false), after["KHTH"]);
        Assert.Equal(("Khoa Ngoại (giả lập)", true), (after["K05"].Name, after["K05"].Active));

        // Người dùng vẫn gắn với khoa cũ (nay mang mã HIS): bác sĩ demo vẫn đăng nhập và thuộc khoa K02.
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, doctor.Status);
        var (_, me) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/auth/me", doctor.AccessToken);
        Assert.Equal(before["NOI"].Id, me.GetProperty("departmentId").GetString());

        // Lấy lại lần nữa: không đổi gì.
        var (_, again) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/departments/sync-his", admin);
        Assert.Equal((0, 0, 4), (again.GetProperty("added").GetInt32(), again.GetProperty("updated").GetInt32(), again.GetProperty("unchanged").GetInt32()));
        Assert.Empty(again.GetProperty("deactivated").EnumerateArray());
    }

    [Fact]
    public async Task WhenDepartmentsComeFromHis_TheyCannotBeAddedEditedOrDeletedInTheApp()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var departments = await DepartmentsAsync(admin);
        var (_, features) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/features", admin);
        Assert.True(features.GetProperty("departmentsFromHis").GetBoolean());

        var (created, createdBody) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/departments", admin, new { code = "NGOAI", name = "Khoa Ngoại", isActive = true });
        var (updated, _) = await _client.SendJsonAsync(
            HttpMethod.Put, $"/api/v1/departments/{departments["KHTH"].Id}", admin, new { code = "KHTH", name = "Đổi tên", isActive = true });
        var (deleted, _) = await _client.SendJsonAsync(HttpMethod.Delete, $"/api/v1/departments/{departments["KHTH"].Id}", admin);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, created);
        Assert.Equal("departments.managed_by_his", createdBody.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, updated);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, deleted);
        Assert.Equal(departments, await DepartmentsAsync(admin));
    }

    [Fact]
    public async Task Sync_NeedsDepartmentManagement_AndKeepsTheCatalogWhenHisFails()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var before = await DepartmentsAsync(admin);
        var doctor = await _client.LoginAsync("bacsi.demo", TestSettings.DemoPassword);

        var (forbidden, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/departments/sync-his", doctor.AccessToken);
        _api.Down = true;
        var (down, downBody) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/departments/sync-his", admin);
        _api.Down = false;
        _api.Departments = [];
        var (empty, emptyBody) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/departments/sync-his", admin);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, down);
        Assert.Equal("departments.his_unavailable", downBody.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty);
        Assert.Equal("departments.his_empty", emptyBody.GetProperty("code").GetString());
        Assert.Equal(before, await DepartmentsAsync(admin));
    }

    /// <summary>Giả lập <c>GET api/v1/departments</c> của API thông tin người bệnh.</summary>
    private sealed class FakeDepartmentApi : HttpMessageHandler
    {
        public IReadOnlyList<HisDepartment> Departments { get; set; } = [];

        public bool Down { get; set; }

        public string? ApiKey { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("Không kết nối được (giả lập).");
            }

            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://thongtinbn.gia-lap.local/api/v1/departments", request.RequestUri!.ToString());
            ApiKey = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null;
            var items = Departments.Select(d => new { code = d.Code, name = d.Name });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { items }, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record HisDepartment(string Code, string Name);
}
