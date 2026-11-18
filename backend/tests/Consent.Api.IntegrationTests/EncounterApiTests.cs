using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Consent.Infrastructure.PatientSource;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Gắn phiếu với lượt tiếp nhận trên HIS khi ký hoàn tất (quyết định 25), qua API thông tin người bệnh giả lập theo đúng
/// hợp đồng <c>POST api/v1/encounters/search</c> (dữ liệu giả lập), với SQL Server thật. Quản trị bật/tắt ở Cấu hình hệ thống.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class EncounterApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string ApiKey = "khoa-api-gia-lap-chi-dung-trong-kiem-thu-0001";
    private readonly FakeEncounterApi _api = new();
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

    private static string Image() => Convert.ToBase64String(TestPng.Signature());

    /// <summary>Quản trị bật/tắt gắn lượt tiếp nhận ở Cấu hình hệ thống.</summary>
    private async Task<JsonElement> SwitchAsync(bool enabled)
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var (status, body) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/encounters", admin, new { enabled });
        Assert.True(status == HttpStatusCode.OK, body.ToString());
        Assert.Equal(enabled, body.GetProperty("enabled").GetBoolean());
        return body;
    }

    /// <summary>Tiếp đón lập phiếu BM02 cho người bệnh demo (CCCD giả lập 001085000001), người bệnh đã ký; trả về đường dẫn phiếu.</summary>
    private async Task<string> PatientSignedBm02Async(string receptionist)
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", receptionist);
        var patientId = patients.GetProperty("items")[0].GetProperty("id").GetString()!;
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", receptionist);
        var templateId = available.EnumerateArray()
            .Single(t => t.GetProperty("templateCode").GetString() == "QT-KNGT-BM02").GetProperty("templateId").GetString()!;
        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        var form = $"/api/v1/consent-forms/{created.GetProperty("id").GetString()}";
        await _client.SendJsonAsync(HttpMethod.Put, form, receptionist, new { data = new { careScheme = "nonInsurance" }, signerKind = "Patient" });
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist)).Status);
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        var (signedStatus, signed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString()!,
            new { image = Image(), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signedStatus == HttpStatusCode.OK, signed.ToString());
        return form;
    }

    [Fact]
    public async Task Completing_RequiresPickingAnEncounterOfThePatient_ThenTheFormShowsIt()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var (_, initially) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/settings/encounters", admin);
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var form = await PatientSignedBm02Async(receptionist);

        // Đã cấu hình API thông tin người bệnh nhưng quản trị chưa bật: chưa có danh sách lượt.
        Assert.False(initially.GetProperty("enabled").GetBoolean());
        Assert.True(initially.GetProperty("available").GetBoolean());
        var (offStatus, off) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/encounters", receptionist);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, offStatus);
        Assert.Equal("consent_forms.encounters_disabled", off.GetProperty("code").GetString());
        await SwitchAsync(true);

        // Mặc định: các lượt trong ngày (giờ bệnh viện), mới nhất trước.
        var (listStatus, list) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/encounters", receptionist);
        Assert.True(listStatus == HttpStatusCode.OK, list.ToString());
        var today = list.GetProperty("from").GetString()!;
        Assert.Equal(today, list.GetProperty("to").GetString());
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)), DateOnly.Parse(today, CultureInfo.InvariantCulture), new DateOnlyNear());
        var items = list.GetProperty("items").EnumerateArray().ToList();
        var day = today.Replace("-", "", StringComparison.Ordinal);
        Assert.Equal([$"TN-{day}-2", $"TN-{day}-1"], items.Select(i => i.GetProperty("encounterNo").GetString()).ToList());
        Assert.EndsWith("+07:00", items[0].GetProperty("receivedAt").GetString(), StringComparison.Ordinal);
        Assert.Equal("Phòng khám 3", items[0].GetProperty("place").GetString());
        Assert.Equal(0, items[0].GetProperty("forms").GetArrayLength());

        // Chưa chọn lượt tiếp nhận: không hoàn tất được.
        var (missingStatus, missing) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Image() });
        Assert.Equal(HttpStatusCode.BadRequest, missingStatus);
        Assert.True(missing.GetProperty("errors").TryGetProperty("encounterNo", out _), missing.ToString());

        var chosen = items[0].GetProperty("encounterNo").GetString()!;
        var (completedStatus, completed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/complete", receptionist, new { image = Image(), encounterNo = chosen, encounterDate = today });
        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.Equal(chosen, completed.GetProperty("encounter").GetProperty("encounterNo").GetString());
        Assert.Equal("Người tiếp đón B (giả lập)", completed.GetProperty("encounter").GetProperty("receivedBy").GetString());

        // Mở lại phiếu và danh sách lượt: lượt đã chọn kèm phiếu vừa gắn.
        var (_, detail) = await _client.SendJsonAsync(HttpMethod.Get, form, receptionist);
        var (_, again) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/encounters", receptionist);
        Assert.Equal(chosen, detail.GetProperty("encounter").GetProperty("encounterNo").GetString());
        var attached = again.GetProperty("items")[0].GetProperty("forms");
        Assert.Equal(completed.GetProperty("formNo").GetString(), Assert.Single(attached.EnumerateArray()).GetProperty("formNo").GetString());

        // Lưu đúng cột, không mã hóa (để đối soát/thống kê theo lượt tiếp nhận).
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT EncounterNo, EncounterReceivedBy FROM ConsentForms WHERE EncounterNo IS NOT NULL", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal((chosen, "Người tiếp đón B (giả lập)"), (reader.GetString(0), reader.GetString(1)));
        await reader.DisposeAsync();

        // Bật/tắt được ghi nhật ký hệ thống như mọi cấu hình khác.
        await using var audit = new SqlCommand(
            "SELECT COUNT(*) FROM AuditLogs WHERE EntityType = 'SystemSetting' AND UserName = 'admin'", connection);
        Assert.True((int)(await audit.ExecuteScalarAsync())! >= 1);

        // Khóa API đi trong header, CCCD đi trong thân (không nằm trên URL).
        Assert.All(_api.Requests, r =>
        {
            Assert.Equal("https://thongtinbn.gia-lap.local/api/v1/encounters/search", r.Url);
            Assert.Equal(ApiKey, r.ApiKey);
            Assert.Equal("001085000001", r.CitizenId);
        });
    }

    [Fact]
    public async Task EncounterList_TakesADateRange_ValidatesIt_AndNeedsTheSigningPermission()
    {
        await SwitchAsync(true);
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var auditor = (await _client.LoginAsync("kiemtra.demo", TestSettings.DemoPassword)).AccessToken!;
        var form = await PatientSignedBm02Async(receptionist);
        var to = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var from = to.AddDays(-1);

        var (rangeStatus, range) = await _client.SendJsonAsync(
            HttpMethod.Get, $"{form}/encounters?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}", receptionist);
        var (reversedStatus, reversed) = await _client.SendJsonAsync(
            HttpMethod.Get, $"{form}/encounters?from={to:yyyy-MM-dd}&to={from:yyyy-MM-dd}", receptionist);
        var (auditorStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/encounters", auditor);
        _api.Down = true;
        var (downStatus, down) = await _client.SendJsonAsync(HttpMethod.Get, $"{form}/encounters", receptionist);

        Assert.True(rangeStatus == HttpStatusCode.OK, range.ToString());
        Assert.Equal(4, range.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, reversedStatus);
        Assert.True(reversed.GetProperty("errors").TryGetProperty("from", out _), reversed.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, auditorStatus);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, downStatus);
        Assert.Equal("consent_forms.encounters_unavailable", down.GetProperty("code").GetString());
    }

    [Fact]
    public async Task HisDown_AdminSwitchesItOffInSettings_AndStaffCompleteRightAway()
    {
        await SwitchAsync(true);
        var receptionist = (await _client.LoginAsync("tiepdon.demo", TestSettings.DemoPassword)).AccessToken!;
        var form = await PatientSignedBm02Async(receptionist);
        _api.Down = true;

        // HIS lỗi: không có danh sách lượt, chưa chọn lượt thì không hoàn tất được.
        var (blockedStatus, blocked) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Image() });
        Assert.Equal(HttpStatusCode.BadRequest, blockedStatus);
        Assert.True(blocked.GetProperty("errors").TryGetProperty("encounterNo", out _), blocked.ToString());
        // Nhân viên không tự tắt được.
        var (forbiddenStatus, _) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/encounters", receptionist, new { enabled = false });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenStatus);

        // Quản trị tắt: có hiệu lực ngay, không cần khởi động lại.
        await SwitchAsync(false);
        var (completedStatus, completed) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Image() });

        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        Assert.Equal("Completed", completed.GetProperty("status").GetString());
        Assert.True(
            !completed.TryGetProperty("encounter", out var encounter) || encounter.ValueKind == JsonValueKind.Null,
            completed.ToString());
    }

    /// <summary>Ngày "hôm nay" giờ bệnh viện có thể lệch một ngày nếu test chạy đúng lúc nửa đêm.</summary>
    private sealed class DateOnlyNear : IEqualityComparer<DateOnly>
    {
        public bool Equals(DateOnly x, DateOnly y) => Math.Abs(x.DayNumber - y.DayNumber) <= 1;

        public int GetHashCode(DateOnly obj) => 0;
    }

    /// <summary>
    /// Giả lập tra lượt tiếp nhận của API thông tin người bệnh: người bệnh demo (CCCD giả lập 001085000001) có hai lượt mỗi
    /// ngày (07:30 và 10:45); số tiếp nhận chứa ngày để kết quả không phụ thuộc giờ chạy test.
    /// </summary>
    private sealed class FakeEncounterApi : HttpMessageHandler
    {
        public List<(string Url, string? ApiKey, string? CitizenId)> Requests { get; } = [];

        public bool Down { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Down)
            {
                throw new HttpRequestException("Không kết nối được (giả lập).");
            }

            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var citizenId = body.RootElement.TryGetProperty("citizenId", out var id) ? id.GetString() : null;
            Requests.Add((request.RequestUri!.ToString(), request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null, citizenId));
            var from = DateOnly.Parse(body.RootElement.GetProperty("from").GetString()!, CultureInfo.InvariantCulture);
            var to = DateOnly.Parse(body.RootElement.GetProperty("to").GetString()!, CultureInfo.InvariantCulture);
            var items = new List<object>();
            for (var day = to; citizenId == "001085000001" && day >= from && day >= to.AddDays(-30); day = day.AddDays(-1))
            {
                var code = day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                items.Add(new { encounterNo = $"TN-{code}-2", receivedAt = day.ToDateTime(new TimeOnly(10, 45)), place = "Phòng khám 3", receivedBy = "Người tiếp đón B (giả lập)" });
                items.Add(new { encounterNo = $"TN-{code}-1", receivedAt = day.ToDateTime(new TimeOnly(7, 30)), place = "Phòng khám 1", receivedBy = "Người tiếp đón A (giả lập)" });
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { items }, JsonSerializerOptions.Web), Encoding.UTF8, "application/json"),
            };
        }
    }
}
