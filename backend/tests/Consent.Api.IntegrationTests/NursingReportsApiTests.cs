using System.Globalization;
using System.Net;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Báo cáo điều dưỡng hằng ngày (quyết định 31) qua API, với SQL Server thật: Phòng Điều dưỡng cài khoa báo cáo, điều dưỡng
/// khoa lập và gửi, Phòng Điều dưỡng tổng hợp, in, xuất Excel, mở lại; phần nhập thêm lưu mã hóa và không lộ trong nhật ký.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class NursingReportsApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Notes = "Người bệnh Nguyễn Văn An (giả lập) sốt về đêm";

    private readonly DatabaseApiFactory _factory = new(sql);
    private HttpClient _client = null!;

    /// <summary>Ngày hôm nay theo giờ bệnh viện (UTC+7), như máy chủ tính.</summary>
    private static string Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    private static Dictionary<string, string> AllOk() => new()
    {
        ["professional"] = "ok", ["drugSupply"] = "issue", ["hospitalFees"] = "ok", ["drugAdministration"] = "ok",
        ["patientFeedback"] = "ok", ["security"] = "ok", ["facilities"] = "ok", ["speakers"] = "ok",
    };

    private async Task<(string Nurse, string Office, string Admin)> LoginAsync() => (
        (await _client.LoginAsync("dieuduong.demo", TestSettings.DemoPassword)).AccessToken!,
        (await _client.LoginAsync("phongdieuduong.demo", TestSettings.DemoPassword)).AccessToken!,
        await _client.LoginAsAdministratorAsync());

    private async Task<string> InternalMedicineIdAsync(string office)
    {
        var (status, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/departments", office);
        Assert.Equal(HttpStatusCode.OK, status);
        return departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "NOI").GetProperty("departmentId").GetString()!;
    }

    [Fact]
    public async Task DepartmentReportsAndTheNursingOfficeSummarises()
    {
        var (nurse, office, admin) = await LoginAsync();

        // Khoa chưa có trong danh sách báo cáo: xem được mẫu trống nhưng chưa lập được.
        var (blankStatus, blank) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/daily?date={Today}", nurse);
        Assert.Equal(HttpStatusCode.OK, blankStatus);
        Assert.Equal("New", blank.GetProperty("status").GetString());
        Assert.False(blank.GetProperty("isReportingDepartment").GetBoolean());
        var (notListed, notListedBody) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new { reportDate = Today, values = new { nurses = 18 } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notListed);
        Assert.Equal("nursing_reports.department_not_reporting", notListedBody.GetProperty("code").GetString());

        // Chỉ Phòng Điều dưỡng cài khoa báo cáo và số giường.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/departments", nurse)).Status);
        var noi = await InternalMedicineIdAsync(office);
        var (configured, departments) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/departments", office, new { items = new[] { new { departmentId = noi, bedCount = 40 } } });
        Assert.Equal(HttpStatusCode.OK, configured);
        Assert.Equal(40, departments.EnumerateArray().Single(d => d.GetProperty("departmentId").GetString() == noi).GetProperty("bedCount").GetInt32());

        // Điều dưỡng lưu nháp rồi gửi; gửi xong thì khóa.
        var values = new Dictionary<string, int?> { ["nurses"] = 18, ["currentPatients"] = 47, ["boneMarrow"] = 2, ["outsideExams"] = 1 };
        var (draftStatus, draft) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new { reportDate = Today, values, notes = Notes });
        Assert.True(draftStatus == HttpStatusCode.OK, draft.ToString());
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        var (incomplete, incompleteBody) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new { reportDate = Today, values, notes = Notes, submit = true });
        Assert.Equal(HttpStatusCode.BadRequest, incomplete);
        Assert.True(incompleteBody.GetProperty("errors").TryGetProperty("checks", out _));
        var (sentStatus, sent) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new { reportDate = Today, values, checks = AllOk(), notes = Notes, submit = true });
        Assert.True(sentStatus == HttpStatusCode.OK, sent.ToString());
        Assert.Equal("Submitted", sent.GetProperty("status").GetString());
        Assert.Equal("Điều dưỡng Demo", sent.GetProperty("submittedBy").GetString());
        var id = sent.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new { reportDate = Today, values })).Status);

        // Phần nhập thêm (có thể có thông tin người bệnh) lưu mã hóa, nhật ký chỉ ghi "***".
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("SELECT NotesEncrypted FROM NursingDailyReports WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@id", Guid.Parse(id));
            var stored = (string)(await command.ExecuteScalarAsync())!;
            Assert.DoesNotContain("Nguyễn Văn An", stored, StringComparison.Ordinal);
        }

        var (_, audit) = await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/audit-logs?entityType=NursingDailyReport&entityId={id}&pageSize=50", admin);
        Assert.NotEmpty(audit.GetProperty("items").EnumerateArray());
        Assert.DoesNotContain("Nguyễn Văn An", audit.ToString(), StringComparison.Ordinal);

        // Điều dưỡng không xem bảng tổng hợp; Phòng Điều dưỡng xem, in PDF, xuất Excel.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", nurse)).Status);
        var (summaryStatus, summary) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
        Assert.Equal(HttpStatusCode.OK, summaryStatus);
        var row = Assert.Single(summary.GetProperty("rows").EnumerateArray());
        Assert.Equal("Submitted", row.GetProperty("status").GetString());
        Assert.Equal(40, row.GetProperty("cells").GetProperty("beds").GetInt32());
        Assert.Equal(47, row.GetProperty("cells").GetProperty("currentPatients").GetInt32());
        Assert.Equal(1, row.GetProperty("cells").GetProperty("outsideVisits").GetInt32());
        Assert.Equal(2, summary.GetProperty("procedures")[0].GetProperty("value").GetInt32());
        Assert.Equal("Không ổn: NOI", summary.GetProperty("situation")[1].GetProperty("text").GetString());
        Assert.Equal(Notes, summary.GetProperty("incidents")[0].GetProperty("text").GetString());
        // Quyết định 44: mã khoa (tên viết tắt), hai cột mới (báo cáo gửi sau khi có hai dòng: bỏ trống là 0), người báo cáo
        // ở cột cuối mục A và dưới mỗi mục B–F.
        Assert.Equal("NOI", row.GetProperty("departmentCode").GetString());
        Assert.Equal("NOI", summary.GetProperty("incidents")[0].GetProperty("departmentCode").GetString());
        Assert.Equal(0, row.GetProperty("cells").GetProperty("foreignPatients").GetInt32());
        Assert.Equal(0, row.GetProperty("cells").GetProperty("transfersOutYesterday").GetInt32());
        Assert.Equal("Điều dưỡng Demo", row.GetProperty("submittedBy").GetString());
        var reporters = summary.GetProperty("reporters");
        Assert.Equal(JsonValueKind.Null, reporters.GetProperty("outpatient").ValueKind);
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("procedures").GetString());
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("situation").GetString());
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("incidents").GetString());

        using (var pdf = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/nursing-reports/summary/pdf?date={Today}", office)))
        {
            Assert.Equal(HttpStatusCode.OK, pdf.StatusCode);
            Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync())[..4], StringComparison.Ordinal);
        }

        using (var xlsx = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/nursing-reports/summary/xlsx?date={Today}", office)))
        {
            Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType?.MediaType);
            Assert.Equal("PK"u8.ToArray(), (await xlsx.Content.ReadAsByteArrayAsync())[..2]);
        }

        using (var reportPdf = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/nursing-reports/{id}/pdf", nurse)))
        {
            Assert.Equal(HttpStatusCode.OK, reportPdf.StatusCode);
            Assert.Equal("application/pdf", reportPdf.Content.Headers.ContentType?.MediaType);
        }

        // Mở lại: chỉ Phòng Điều dưỡng, bắt buộc lý do; khoa sửa được lại.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/nursing-reports/{id}/reopen", nurse, new { reason = "Sai số" })).Status);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/nursing-reports/{id}/reopen", office, new { reason = " " })).Status);
        var (reopenedStatus, reopened) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/nursing-reports/{id}/reopen", office, new { reason = "Sai số NB hiện tại" });
        Assert.Equal(HttpStatusCode.OK, reopenedStatus);
        Assert.Equal("Draft", reopened.GetProperty("status").GetString());
        Assert.Equal("Phòng Điều dưỡng Demo", reopened.GetProperty("reopenedBy").GetString());
        var (fixedStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse,
            new { reportDate = Today, values = new { currentPatients = 46 }, checks = AllOk(), submit = true });
        Assert.Equal(HttpStatusCode.OK, fixedStatus);
    }

    [Fact]
    public async Task HisFigures_AreTypedInByEachDepartment_WhenHisIsUnavailable()
    {
        var (nurse, office, _) = await LoginAsync();
        var noi = await InternalMedicineIdAsync(office);

        var (refreshStatus, refresh) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/nursing-reports/statistics/refresh", office, new { reportDate = Today });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refreshStatus);
        Assert.Equal("nursing_reports.his_disabled", refresh.GetProperty("code").GetString());

        // Phòng Điều dưỡng giao khoa Nội nhập tổng số người bệnh đến khám (quyết định 35); khoa Nội phải báo cáo.
        var (assignStatus, assigned) = await _client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/nursing-reports/departments",
            office,
            new { items = new[] { new { departmentId = noi, bedCount = 40 } }, hisEntry = new Dictionary<string, string?> { ["outpatientVisits"] = noi } });
        Assert.True(assignStatus == HttpStatusCode.OK, assigned.ToString());
        Assert.Equal(
            ["outpatientVisits"],
            assigned.EnumerateArray().Single(d => d.GetProperty("departmentId").GetString() == noi)
                .GetProperty("hisIndicators").EnumerateArray().Select(i => i.GetString()));

        // Phòng Điều dưỡng không nhập thay khoa; lệnh nhập tay của Phòng Điều dưỡng không còn.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/his-entry", office,
            new { departmentId = noi, reportDate = Today, values = new { waitingAdmission = 1 } })).Status);
        var (removedStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/statistics", office, new { reportDate = Today, hospital = new { outpatientVisits = 1 } });
        Assert.Contains(removedStatus, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed });

        var (savedStatus, saved) = await _client.SendJsonAsync(
            HttpMethod.Put,
            "/api/v1/nursing-reports/his-entry",
            nurse,
            new { reportDate = Today, values = new Dictionary<string, int?> { ["waitingAdmission"] = 2, ["outpatientVisits"] = 686 } });
        Assert.True(savedStatus == HttpStatusCode.OK, saved.ToString());
        var entry = saved.GetProperty("hisEntry");
        Assert.Equal(
            [("waitingAdmission", 2), ("outpatientVisits", 686)],
            entry.GetProperty("lines").EnumerateArray().Select(l => (l.GetProperty("key").GetString(), l.GetProperty("value").GetInt32())));
        Assert.Equal("Điều dưỡng Demo", entry.GetProperty("updatedBy").GetString());

        var (summaryStatus, summary) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
        Assert.Equal(HttpStatusCode.OK, summaryStatus);
        Assert.Equal(686, summary.GetProperty("outpatient")[0].GetProperty("value").GetInt32());
        Assert.Equal("NOI", summary.GetProperty("outpatient")[0].GetProperty("enteredBy").GetString());
        // Người báo cáo mục B là người khoa đã nhập tay (quyết định 44).
        Assert.Equal("Điều dưỡng Demo (NOI)", summary.GetProperty("reporters").GetProperty("outpatient").GetString());
        var row = summary.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("departmentId").GetString() == noi);
        Assert.Equal(2, row.GetProperty("cells").GetProperty("waitingAdmission").GetInt32());
        var statistics = summary.GetProperty("statistics");
        Assert.False(statistics.GetProperty("hasValues").GetBoolean());
        Assert.True(statistics.GetProperty("usesDepartmentEntries").GetBoolean());
        Assert.Equal(
            "Điều dưỡng Demo",
            statistics.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("departmentId").GetString() == noi)
                .GetProperty("enteredBy").GetString());
    }

    [Fact]
    public async Task Form_IsAvailableToSignedInUsersOnly()
    {
        var (nurse, _, _) = await LoginAsync();

        var (status, form) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/form", nurse);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(6, form.GetProperty("sections").GetArrayLength());
        Assert.Equal(8, form.GetProperty("checks").GetArrayLength());

        using var anonymous = await _client.GetAsync("/api/v1/nursing-reports/form");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Thiếu ngày thì báo lỗi, không ngầm hiểu là ngày 01/01/0001.
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/daily", nurse)).Status);
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return request;
    }
}
