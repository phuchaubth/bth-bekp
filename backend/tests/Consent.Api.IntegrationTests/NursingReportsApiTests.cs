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
        // Số NB hiện tại máy chủ tính (quyết định 46): 46 + 2 nhập khoa từ PK − 1 xuất viện = 47; số gửi lên bị bỏ qua.
        var values = new Dictionary<string, int?>
        {
            ["nurses"] = 18, ["previousDayPatients"] = 46, ["admissionsFromClinic"] = 2, ["dischargesYesterday"] = 1, ["currentPatients"] = 5,
            ["boneMarrow"] = 2, ["outsideExams"] = 1, ["privateRoomPatients"] = 3,
        };
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

            // Số liệu lưu kèm phiên bản mẫu 4 (quyết định 54: thêm "Số NB bao phòng").
            await using var figures = new SqlCommand("SELECT FiguresJson FROM NursingDailyReports WHERE Id = @id", connection);
            figures.Parameters.AddWithValue("@id", Guid.Parse(id));
            using var json = JsonDocument.Parse((string)(await figures.ExecuteScalarAsync())!);
            Assert.Equal(4, json.RootElement.GetProperty("form").GetInt32());
            Assert.Equal(3, json.RootElement.GetProperty("values").GetProperty("privateRoomPatients").GetInt32());
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
        // Quyết định 54: cột "Bao phòng" ngay sau "Người nước ngoài".
        Assert.Equal(3, row.GetProperty("cells").GetProperty("privateRoomPatients").GetInt32());
        var columns = summary.GetProperty("columns").EnumerateArray().Select(c => c.GetProperty("key").GetString()).ToList();
        Assert.Equal("privateRoomPatients", columns[columns.IndexOf("foreignPatients") + 1]);
        Assert.Equal(
            "Bao phòng",
            summary.GetProperty("columns").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "privateRoomPatients")
                .GetProperty("header").GetString());
        Assert.Equal("Điều dưỡng Demo", row.GetProperty("submittedBy").GetString());
        var reporters = summary.GetProperty("reporters");
        Assert.Equal(JsonValueKind.Null, reporters.GetProperty("outpatient").ValueKind);
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("procedures").GetString());
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("situation").GetString());
        Assert.Equal("Điều dưỡng Demo (NOI)", reporters.GetProperty("incidents").GetString());

        // Dashboard (quyết định 47): chỉ người xem được bảng tổng hợp; số liệu lấy từ báo cáo đã gửi, chỉ biết báo cáo có phần
        // nhập thêm, không đọc nội dung.
        var dashboardUrl = $"/api/v1/nursing-reports/dashboard?to={Today}&days=7";
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, dashboardUrl, nurse)).Status);
        var (dashboardStatus, dashboard) = await _client.SendJsonAsync(HttpMethod.Get, dashboardUrl, office);
        Assert.Equal(HttpStatusCode.OK, dashboardStatus);
        Assert.Equal(1, dashboard.GetProperty("reportingCount").GetInt32());
        var dashboardDays = dashboard.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(7, dashboardDays.Count);
        Assert.Equal(JsonValueKind.Null, dashboardDays[0].GetProperty("values").GetProperty("currentPatients").ValueKind);
        var lastDay = dashboardDays[^1];
        Assert.Equal(Today, lastDay.GetProperty("date").GetString());
        Assert.Equal(1, lastDay.GetProperty("submitted").GetInt32());
        Assert.Equal(47, lastDay.GetProperty("values").GetProperty("currentPatients").GetInt32());
        Assert.Equal(2, lastDay.GetProperty("values").GetProperty("admissions").GetInt32());
        Assert.Equal(1, lastDay.GetProperty("patientChange").GetInt32());
        Assert.Equal(40, lastDay.GetProperty("beds").GetInt32());
        Assert.Equal(47, lastDay.GetProperty("bedPatients").GetInt32());
        Assert.Equal(1, lastDay.GetProperty("issueDepartments").GetInt32());
        Assert.Equal(1, lastDay.GetProperty("incidents").GetInt32());
        var dashboardRow = Assert.Single(dashboard.GetProperty("departments").EnumerateArray());
        Assert.Equal("NOI", dashboardRow.GetProperty("code").GetString());
        Assert.Equal(1, dashboardRow.GetProperty("submittedDays").GetInt32());
        Assert.True(dashboardRow.GetProperty("hasIncident").GetBoolean());
        Assert.Equal(new[] { "drugSupply" }, dashboardRow.GetProperty("issues").EnumerateArray().Select(i => i.GetString()).ToArray());
        Assert.Equal(1, dashboard.GetProperty("checks").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "drugSupply")
            .GetProperty("issueCount").GetInt32());
        Assert.DoesNotContain("Nguyễn Văn An", dashboard.ToString(), StringComparison.Ordinal);
        var (defaultStatus, defaultDashboard) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/dashboard", office);
        Assert.Equal(HttpStatusCode.OK, defaultStatus);
        Assert.Equal(30, defaultDashboard.GetProperty("days").GetArrayLength());
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/dashboard?days=93", office)).Status);

        using (var pdf =await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/nursing-reports/summary/pdf?date={Today}", office)))
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
        // Báo cáo bị trả lại (nháp) không còn được dashboard tính.
        var (_, afterReopen) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/dashboard?to={Today}&days=1", office);
        Assert.Equal(0, afterReopen.GetProperty("days")[0].GetProperty("submitted").GetInt32());
        var (fixedStatus, _) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse,
            new { reportDate = Today, values = new { previousDayPatients = 46 }, checks = AllOk(), submit = true });
        Assert.Equal(HttpStatusCode.OK, fixedStatus);
    }

    [Fact]
    public async Task DelegatedReporting_WorksOnlyWithinTheDates_AndIsShownOnTheSummary()
    {
        var (nurse, office, admin) = await LoginAsync();
        var noi = await InternalMedicineIdAsync(office);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/departments", office, new { items = new[] { new { departmentId = noi, bedCount = 40 } } })).Status);

        // Điều dưỡng của khoa khác (giả lập) sẽ được ủy quyền.
        var (_, roles) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/roles", admin);
        var nurseRole = roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == "NURSE").GetProperty("id").GetString();
        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", admin);
        var kkb = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "KKB").GetProperty("id").GetString();
        var (created, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/users", admin, new
        {
            userName = "dieuduong.kkb",
            fullName = "Điều dưỡng Khám bệnh (giả lập)",
            departmentId = kkb,
            roleIds = new[] { nurseRole },
            password = "Mat-Khau-Tam-2026",
        });
        Assert.Equal(HttpStatusCode.Created, created);
        var first = await _client.LoginAsync("dieuduong.kkb", "Mat-Khau-Tam-2026");
        var (_, session) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/auth/change-password", first.AccessToken,
            new { currentPassword = "Mat-Khau-Tam-2026", newPassword = "Dieu-Duong-Gia-Lap-2026!" });
        var colleague = session.GetProperty("accessToken").GetString()!;
        var dailyUrl = $"/api/v1/nursing-reports/daily?date={Today}&departmentId={noi}";
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, dailyUrl, colleague)).Status);

        // Người ủy quyền chọn người trong danh sách, ủy quyền báo cáo của khoa từ hôm nay đến ngày mai.
        var (_, candidates) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/delegations/candidates", nurse);
        var delegateId = candidates.EnumerateArray()
            .Single(c => c.GetProperty("fullName").GetString() == "Điều dưỡng Khám bệnh (giả lập)").GetProperty("userId").GetString();
        var tomorrow = DateOnly.Parse(Today, CultureInfo.InvariantCulture).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (delegated, delegation) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/nursing-reports/delegations", nurse, new
        {
            departmentId = noi,
            delegateUserId = delegateId,
            startDate = Today,
            endDate = tomorrow,
            report = true,
            hisEntry = false,
            reason = "Nghỉ phép (giả lập)",
        });
        Assert.True(delegated == HttpStatusCode.OK, delegation.ToString());
        Assert.Equal("Active", delegation.GetProperty("status").GetString());
        Assert.Equal(new[] { "report" }, delegation.GetProperty("contents").EnumerateArray().Select(c => c.GetString()).ToArray());
        var delegationId = delegation.GetProperty("id").GetString();

        // Người được ủy quyền thấy ủy quyền giao cho mình (không hủy được).
        var (_, received) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/delegations", colleague);
        var mine = Assert.Single(received.EnumerateArray());
        Assert.Equal(delegationId, mine.GetProperty("id").GetString());
        Assert.False(mine.GetProperty("canRevoke").GetBoolean());

        // Người được ủy quyền lập và gửi báo cáo khoa Nội hôm nay; ngày trước thời gian ủy quyền thì không.
        var (sentStatus, sent) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/nursing-reports/daily", colleague, new
        {
            departmentId = noi,
            reportDate = Today,
            values = new { nurses = 18, previousDayPatients = 40, admissionsFromClinic = 2 },
            checks = AllOk(),
            submit = true,
        });
        Assert.True(sentStatus == HttpStatusCode.OK, sent.ToString());
        Assert.Equal("Điều dưỡng Khám bệnh (giả lập) (được Điều dưỡng Demo ủy quyền)", sent.GetProperty("submittedBy").GetString());
        var yesterday = DateOnly.Parse(Today, CultureInfo.InvariantCulture).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/daily", colleague, new { departmentId = noi, reportDate = yesterday, values = new { nurses = 18 } })).Status);

        // Bảng tổng hợp ghi người được ủy quyền và việc ủy quyền, kèm ủy quyền có hiệu lực trong ngày.
        var (_, summary) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
        var row = Assert.Single(summary.GetProperty("rows").EnumerateArray());
        Assert.Equal("Điều dưỡng Khám bệnh (giả lập) (được Điều dưỡng Demo ủy quyền)", row.GetProperty("submittedBy").GetString());
        Assert.Equal("Điều dưỡng Khám bệnh (giả lập) (NOI, được Điều dưỡng Demo ủy quyền)",
            summary.GetProperty("reporters").GetProperty("situation").GetString());
        var active = Assert.Single(summary.GetProperty("delegations").EnumerateArray());
        Assert.Equal("Điều dưỡng Khám bệnh (giả lập)", active.GetProperty("delegateName").GetString());
        Assert.Equal("Điều dưỡng Demo", active.GetProperty("delegatorName").GetString());

        // Hủy ủy quyền: chỉ người ủy quyền (hoặc Phòng Điều dưỡng); người được ủy quyền không làm được nữa, báo cáo đã gửi vẫn ghi
        // ủy quyền.
        var revokeUrl = $"/api/v1/nursing-reports/delegations/{delegationId}/revoke";
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Post, revokeUrl, colleague)).Status);
        var (revokedStatus, revoked) = await _client.SendJsonAsync(HttpMethod.Post, revokeUrl, nurse);
        Assert.Equal(HttpStatusCode.OK, revokedStatus);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, dailyUrl, colleague)).Status);
        var (_, after) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
        Assert.Equal("Điều dưỡng Khám bệnh (giả lập) (được Điều dưỡng Demo ủy quyền)", after.GetProperty("rows")[0].GetProperty("submittedBy").GetString());
        Assert.Equal(0, after.GetProperty("delegations").GetArrayLength());
        var (_, all) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/delegations?all=true", office);
        Assert.Equal("Revoked", Assert.Single(all.EnumerateArray()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/delegations?all=true", nurse)).Status);

        // Ủy quyền đã hết hạn (đặt lùi ngày trực tiếp trong CSDL): không làm được báo cáo của ngày trong thời gian ủy quyền nữa,
        // không còn trong danh sách ủy quyền của bảng tổng hợp hôm nay.
        var (_, again) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/nursing-reports/delegations", nurse, new
        {
            departmentId = noi,
            delegateUserId = delegateId,
            startDate = Today,
            endDate = Today,
            report = true,
            hisEntry = false,
        });
        var past = DateOnly.Parse(Today, CultureInfo.InvariantCulture);
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "UPDATE NursingReportDelegations SET StartDate = @start, EndDate = @end WHERE Id = @id", connection);
            command.Parameters.AddWithValue("@start", past.AddDays(-3).ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@end", past.AddDays(-2).ToDateTime(TimeOnly.MinValue));
            command.Parameters.AddWithValue("@id", Guid.Parse(again.GetProperty("id").GetString()!));
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var inWindow = past.AddDays(-2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(
            HttpMethod.Get, $"/api/v1/nursing-reports/daily?date={inWindow}&departmentId={noi}", colleague)).Status);
        // Ủy quyền từ ngày mai: chưa có trong bảng tổng hợp hôm nay.
        var (upcomingStatus, _) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/nursing-reports/delegations", nurse, new
        {
            departmentId = noi,
            delegateUserId = delegateId,
            startDate = tomorrow,
            endDate = tomorrow,
            report = true,
            hisEntry = false,
        });
        Assert.Equal(HttpStatusCode.OK, upcomingStatus);
        var (_, later) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
        Assert.Equal(0, later.GetProperty("delegations").GetArrayLength());
    }

    [Fact]
    public async Task ReportingDepartments_KeepTheOfficeOrder_AndShortNames_OnTheSummary()
    {
        var (_, office, _) = await LoginAsync();
        var (_, listed) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/departments", office);
        var ids = listed.EnumerateArray().ToDictionary(d => d.GetProperty("code").GetString()!, d => d.GetProperty("departmentId").GetString()!);
        Assert.All(listed.EnumerateArray(), d => Assert.Equal(JsonValueKind.Null, d.GetProperty("shortName").ValueKind));

        async Task<(HttpStatusCode Status, JsonElement Body)> SaveAsync(string[] order, Dictionary<string, string?> shortNames) =>
            await _client.SendJsonAsync(
                HttpMethod.Put,
                "/api/v1/nursing-reports/departments",
                office,
                new
                {
                    items = order.Select(code => new { departmentId = ids[code], bedCount = 30 }),
                    shortNames = shortNames.ToDictionary(p => ids[p.Key], p => p.Value),
                });
        async Task<string[]> SummaryLabelsAsync()
        {
            var (status, summary) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/summary?date={Today}", office);
            Assert.Equal(HttpStatusCode.OK, status);
            return [.. summary.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("departmentCode").GetString()!)];
        }

        // Quyết định 45: thứ tự trên bảng tổng hợp là thứ tự Phòng Điều dưỡng gửi lên (không theo mã); tên viết tắt in thay mã.
        var (savedStatus, saved) = await SaveAsync(["NOI", "KKB"], new() { ["NOI"] = " Nội ", ["KKB"] = "KB" });
        Assert.True(savedStatus == HttpStatusCode.OK, saved.ToString());
        Assert.Equal(
            [("NOI", "Nội", true), ("KKB", "KB", true), ("PDD", null, false), ("KHTH", null, false)],
            saved.EnumerateArray().Select(d => (
                d.GetProperty("code").GetString(), d.GetProperty("shortName").GetString(), d.GetProperty("isReporting").GetBoolean())));
        Assert.Equal(["Nội", "KB"], await SummaryLabelsAsync());

        // Tên viết tắt trùng tên viết tắt hoặc mã của khoa khác (không phân biệt hoa thường), quá dài: không lưu gì.
        var (duplicate, duplicateBody) = await SaveAsync(["KKB", "NOI"], new() { ["KHTH"] = "kb", ["PDD"] = new string('x', 21) });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate);
        var errors = duplicateBody.GetProperty("errors");
        Assert.Equal(
            "Tên viết tắt \"kb\" trùng với Khoa Khám bệnh (KKB).",
            errors.GetProperty($"shortNames.{ids["KHTH"]}")[0].GetString());
        Assert.Equal("Tên viết tắt tối đa 20 ký tự.", errors.GetProperty($"shortNames.{ids["PDD"]}")[0].GetString());
        Assert.Equal(["Nội", "KB"], await SummaryLabelsAsync());

        // Đổi thứ tự; bỏ tên viết tắt thì in lại mã khoa.
        var (reorderedStatus, reordered) = await SaveAsync(["KKB", "NOI"], new() { ["NOI"] = null });
        Assert.True(reorderedStatus == HttpStatusCode.OK, reordered.ToString());
        Assert.Equal(["KB", "NOI"], await SummaryLabelsAsync());
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
        // Khoa đang dùng có isActive = true; khoa ngừng sử dụng còn trong danh sách thì false (giao diện bỏ khoa đó khi lưu).
        Assert.True(assigned.EnumerateArray().Single(d => d.GetProperty("departmentId").GetString() == noi).GetProperty("isActive").GetBoolean());

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
        // Quyết định 46: giao diện tính Số NB hiện tại theo công thức máy chủ gửi kèm; dòng khác không có công thức.
        var items = form.GetProperty("sections")[5].GetProperty("items").EnumerateArray().ToList();
        var formula = items.Single(i => i.GetProperty("key").GetString() == "currentPatients").GetProperty("formula");
        Assert.Equal("previousDayPatients", formula.GetProperty("base").GetString());
        Assert.Equal(["admissionsFromClinic", "admissionsByTransfer"], formula.GetProperty("add").EnumerateArray().Select(k => k.GetString()));
        Assert.Equal(["dischargesYesterday", "transfersOutYesterday"], formula.GetProperty("subtract").EnumerateArray().Select(k => k.GetString()));
        Assert.Equal(
            JsonValueKind.Null,
            items.Single(i => i.GetProperty("key").GetString() == "previousDayPatients").GetProperty("formula").ValueKind);
        // Quyết định 54: dòng "Số NB bao phòng" ngay sau "Số NB người nước ngoài", thêm ở mẫu phiên bản 4.
        var keys = items.Select(i => i.GetProperty("key").GetString()).ToList();
        var privateRoom = items[keys.IndexOf("foreignPatients") + 1];
        Assert.Equal("privateRoomPatients", privateRoom.GetProperty("key").GetString());
        Assert.Equal("Số NB bao phòng", privateRoom.GetProperty("label").GetString());
        Assert.Equal(4, privateRoom.GetProperty("since").GetInt32());

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
