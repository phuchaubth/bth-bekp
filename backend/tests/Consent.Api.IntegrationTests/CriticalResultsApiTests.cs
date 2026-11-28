using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Kết quả nguy cấp (quyết định 60) qua API, với SQL Server thật: KTV khoa HSH gọi điện xong ghi kết quả kèm lần báo; màn hình
/// theo dõi tại khoa Nội thấy tên viết tắt, không mở được hồ sơ; điều dưỡng khoa Nội xác nhận đã nhận, bác sĩ ghi hướng xử trí bằng
/// tài khoản của mình; KTV hủy hồ sơ báo nhầm; QLCL sửa thông số, xem dashboard; sổ và PDF sổ; số hồ sơ lấy từ sequence; mọi thay
/// đổi có trong nhật ký hệ thống. Dữ liệu giả lập.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class CriticalResultsApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private const string Base = "/api/v1/critical-results";
    private static readonly TimeSpan Vietnam = TimeSpan.FromHours(7);

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

    private async Task<string> LoginAsync(string userName) => (await _client.LoginAsync(userName, TestSettings.DemoPassword)).AccessToken!;

    private async Task<Guid> DepartmentIdAsync(string code)
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Id FROM Departments WHERE Code = @code", connection);
        command.Parameters.Add("@code", System.Data.SqlDbType.NVarChar, 50).Value = code;
        return (Guid)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task LabCallsThenRecords_WardConfirms_DoctorActs_ScreenSeesInitials_QualityReviews_AllAudited()
    {
        var lab = await LoginAsync("ktv.demo");
        var nurse = await LoginAsync("dieuduong.demo");
        var doctor = await LoginAsync("bacsi.demo");
        var screen = await LoginAsync("manhinh.demo");
        var quality = await LoginAsync("qlcl.demo");
        var reception = await LoginAsync("tiepdon.demo");
        var hsh = await DepartmentIdAsync("HSH");
        var noi = await DepartmentIdAsync("NOI");

        // Danh mục demo: bốn mục giả lập; người không có quyền kết quả nguy cấp không xem được.
        var (_, tests) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/tests", lab);
        var catalog = tests.EnumerateArray().ToList();
        Assert.Equal(4, catalog.Count);
        Assert.All(catalog, t => Assert.EndsWith("(giả lập)", t.GetProperty("name").GetString(), StringComparison.Ordinal));
        var potassium = catalog.Single(t => t.GetProperty("name").GetString() == "Kali (K+) (giả lập)").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/tests", reception)).Status);

        // KTV gọi điện xong mới ghi: kết quả 5 phút trước, báo 3 phút trước (hồ sơ báo nhầm có kết quả 4 phút trước, chưa báo).
        var now = DateTimeOffset.UtcNow;
        var resultDay = DateOnly.FromDateTime(now.AddMinutes(-5).ToOffset(Vietnam).DateTime);
        var month = resultDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        object Record(string name, int minutesAgo, bool called) => new
        {
            sendingDepartmentId = hsh,
            testId = potassium,
            patient = new
            {
                code = "NB-GL-0001", fullName = name, dateOfBirth = "1960-01-02", gender = "Female", address = "Ấp Mẫu (giả lập)",
                source = "Manual",
            },
            value = "6,9",
            unit = "mmol/L",
            orderDate = resultDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            resultAtUtc = now.AddMinutes(-minutesAgo),
            urgent = false,
            notification = called
                ? new
                {
                    receivingDepartmentId = noi, method = "Phone", notifiedAtUtc = now.AddMinutes(-3), receiverName = "Điều dưỡng nghe máy (giả lập)",
                    readBackCorrect = true, incorrectReadBacks = 0, repeatedTwice = false,
                }
                : null,
        };

        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Post, Base, nurse, Record("Người Bệnh Giả Lập", 5, true))).Status);
        var (createStatus, created) = await _client.SendJsonAsync(HttpMethod.Post, Base, lab, Record("Người Bệnh Giả Lập", 5, true));
        Assert.True(createStatus == HttpStatusCode.OK, created.ToString());
        var id = created.GetProperty("id").GetString();
        var (_, mistaken) = await _client.SendJsonAsync(HttpMethod.Post, Base, lab, Record("Người Bệnh Nhầm", 4, false));
        var mistakenId = mistaken.GetProperty("id").GetString();
        var year = now.ToOffset(Vietnam).Year;
        Assert.Equal(
            ($"KQNC-{year}-000001", "AwaitingAcknowledgement", $"KQNC-{year}-000002", "AwaitingNotification"),
            (created.GetProperty("number").GetString(), created.GetProperty("status").GetString(),
                mistaken.GetProperty("number").GetString(), mistaken.GetProperty("status").GetString()));
        var (invalidStatus, invalid) = await _client.SendJsonAsync(HttpMethod.Post, Base, lab, Record(" ", 4, false));
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "critical.rule"), (invalidStatus, invalid.GetProperty("code").GetString()));

        // Màn hình theo dõi tại khoa Nội: tên viết tắt, không mở được hồ sơ. KTV thấy một hồ sơ chưa báo.
        var (_, screenAlerts) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts", screen);
        var alert = Assert.Single(screenAlerts.GetProperty("awaitingAcknowledgement").EnumerateArray());
        Assert.Equal((id, "N.B.G.L", "…0001"), (alert.GetProperty("id").GetString(), alert.GetProperty("patientName").GetString(), alert.GetProperty("patientCodeTail").GetString()));
        Assert.Equal(15, screenAlerts.GetProperty("settings").GetProperty("pollSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/{id}", screen)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts", reception)).Status);
        var (_, labAlerts) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts", lab);
        Assert.Equal(1, labAlerts.GetProperty("awaitingNotification").GetInt32());
        // Màn hình theo dõi (quyết định 66): QLCL xem mọi khoa, một khoa; tài khoản màn hình chỉ khoa của mình.
        var (everyStatus, every) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts?allDepartments=true", quality);
        Assert.True(everyStatus == HttpStatusCode.OK, every.ToString());
        Assert.Equal(id, Assert.Single(every.GetProperty("awaitingAcknowledgement").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("Toàn viện", Assert.Single(every.GetProperty("receivingDepartmentNames").EnumerateArray()).GetString());
        var (_, oneWard) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts?departmentId={noi}", quality);
        Assert.Single(oneWard.GetProperty("awaitingAcknowledgement").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts?departmentId={hsh}", screen)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/alerts?allDepartments=true", screen)).Status);

        // Điều dưỡng khoa Nội xác nhận đã nhận; chỉ bác sĩ ghi được hướng xử trí.
        var (ackStatus, acknowledged) = await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/acknowledge", nurse);
        Assert.True(ackStatus == HttpStatusCode.OK, acknowledged.ToString());
        Assert.Equal(
            ("AwaitingAction", "Điều dưỡng Demo"),
            (acknowledged.GetProperty("status").GetString(), acknowledged.GetProperty("acknowledgedByName").GetString()));
        var action = new { summary = "Ngưng thuốc giữ kali, làm lại điện giải đồ (giả lập)", recordedInMedicalRecord = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/action", nurse, action)).Status);
        var (actStatus, acted) = await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/action", doctor, action);
        Assert.True(actStatus == HttpStatusCode.OK, acted.ToString());
        Assert.Equal(
            ("Completed", "Bác sĩ Demo", true),
            (acted.GetProperty("status").GetString(), acted.GetProperty("actionByName").GetString(), acted.GetProperty("actedOnTime").GetBoolean()));

        // KTV hủy hồ sơ báo nhầm (chưa báo khoa nào): bắt buộc lý do.
        Assert.Equal(
            HttpStatusCode.UnprocessableEntity,
            (await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{mistakenId}/cancel", lab, new { reason = " ", receivingDepartmentInformed = false })).Status);
        var (_, cancelled) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{Base}/{mistakenId}/cancel", lab, new { reason = "Nhập nhầm người bệnh (giả lập)", receivingDepartmentInformed = false });
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());

        // Bàn làm việc hai khoa.
        var (_, sent) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/sent", lab);
        Assert.Equal(2, sent.GetProperty("items").GetArrayLength());
        var (_, received) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/received", nurse);
        Assert.Equal(id, Assert.Single(received.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());

        // QLCL sửa thông số (KTV không sửa được; báo sắp quá hạn phải ít hơn thời hạn); hồ sơ đã ghi giữ thời hạn cũ.
        var (_, settings) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/settings", quality);
        var values = JsonNode.Parse(settings.GetProperty("values").GetRawText())!.AsObject();
        values["notifyMinutes"] = 15;
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Put, $"{Base}/settings", lab, values)).Status);
        var (putStatus, updated) = await _client.SendJsonAsync(HttpMethod.Put, $"{Base}/settings", quality, values);
        Assert.True(putStatus == HttpStatusCode.OK, updated.ToString());
        Assert.Equal(
            (15, "QLCL Demo (qlcl.demo)"),
            (updated.GetProperty("values").GetProperty("notifyMinutes").GetInt32(), updated.GetProperty("updatedBy").GetString()));
        values["warnMinutes"] = 20;
        var (badStatus, bad) = await _client.SendJsonAsync(HttpMethod.Put, $"{Base}/settings", quality, values);
        Assert.Equal(HttpStatusCode.BadRequest, badStatus);
        Assert.True(bad.GetProperty("errors").TryGetProperty("warnMinutes", out _), bad.ToString());
        var (_, kept) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/{id}", quality);
        Assert.Equal(10, kept.GetProperty("notifyDeadlineMinutes").GetInt32());

        // Dashboard chỉ cho QLCL: một hồ sơ báo đúng hạn, một hồ sơ hủy đếm riêng.
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}", lab)).Status);
        var (dashboardStatus, dashboard) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}", quality);
        Assert.True(dashboardStatus == HttpStatusCode.OK, dashboard.ToString());
        var kpis = dashboard.GetProperty("kpis");
        Assert.Equal((1, 1), (kpis.GetProperty("total").GetInt32(), kpis.GetProperty("cancelled").GetInt32()));
        Assert.Equal(100.0, kpis.GetProperty("notifyOnTime").GetProperty("percent").GetDouble());
        Assert.Equal(12, dashboard.GetProperty("trend").GetArrayLength());
        // Dashboard một khoa (quyết định 64): khoa Nội chỉ có hồ sơ được báo cho khoa; khoa không có thì báo lỗi ô departmentId.
        var (wardStatus, ward) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={noi}", quality);
        Assert.True(wardStatus == HttpStatusCode.OK, ward.ToString());
        Assert.Equal(noi, ward.GetProperty("departmentId").GetGuid());
        Assert.Equal((1, 0), (ward.GetProperty("kpis").GetProperty("total").GetInt32(), ward.GetProperty("kpis").GetProperty("cancelled").GetInt32()));
        var (unknownStatus, unknown) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={Guid.NewGuid()}", quality);
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus);
        Assert.True(unknown.GetProperty("errors").TryGetProperty("departmentId", out _), unknown.ToString());
        // Tài khoản của khoa (quyết định 65): dashboard của khoa mình; khoa khác, toàn viện thì không có quyền.
        var (ownStatus, own) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={hsh}", lab);
        Assert.True(ownStatus == HttpStatusCode.OK, own.ToString());
        Assert.Equal((hsh, 1), (own.GetProperty("departmentId").GetGuid(), own.GetProperty("kpis").GetProperty("total").GetInt32()));
        var (nurseStatus, nurseView) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={noi}", nurse);
        Assert.True(nurseStatus == HttpStatusCode.OK, nurseView.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={noi}", lab)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/dashboard?month={month}&departmentId={hsh}", nurse)).Status);

        // Sổ ST01 của khoa HSH (dòng hủy đánh dấu); điều dưỡng không xem sổ của khoa HSH.
        var bookUrl = $"{Base}/logbook?form=Laboratory&departmentId={hsh}&month={month}";
        var (_, book) = await _client.SendJsonAsync(HttpMethod.Get, bookUrl, lab);
        Assert.Equal("QT-KQNC/ST01", book.GetProperty("code").GetString());
        Assert.Equal([false, true], book.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("cancelled").GetBoolean()));
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendJsonAsync(HttpMethod.Get, bookUrl, nurse)).Status);

        // PDF sổ ST02 của khoa Nội.
        foreach (var (token, expected) in new[] { (nurse, HttpStatusCode.OK), (reception, HttpStatusCode.Forbidden) })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Base}/logbook/pdf?form=Clinical&departmentId={noi}&month={month}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var pdf = await _client.SendAsync(request);
            Assert.Equal(expected, pdf.StatusCode);
            if (expected == HttpStatusCode.OK)
            {
                Assert.Equal("application/pdf", pdf.Content.Headers.ContentType?.MediaType);
                Assert.Equal($"ket-qua-nguy-cap-so-ST02-{month}.pdf", pdf.Content.Headers.ContentDisposition?.FileNameStar);
                Assert.Equal("%PDF"u8.ToArray(), (await pdf.Content.ReadAsByteArrayAsync())[..4]);
            }
        }

        // Không có lệnh xóa: hồ sơ hủy còn nguyên; mọi lần ghi, báo, xác nhận, xử trí, hủy, sửa thông số, in sổ có trong nhật ký.
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using (var rows = new SqlCommand("SELECT COUNT(*), SUM(CASE WHEN Status = 'Cancelled' THEN 1 ELSE 0 END) FROM CriticalResults", connection))
        await using (var reader = await rows.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal((2, 1), (reader.GetInt32(0), reader.GetInt32(1)));
        }

        await using var audit = new SqlCommand(
            "SELECT COALESCE(EntityType, ''), Action, COUNT(*) FROM AuditLogs "
            + "WHERE EntityType LIKE 'Critical%' OR Action LIKE 'CriticalResults.%' GROUP BY EntityType, Action",
            connection);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using (var auditReader = await audit.ExecuteReaderAsync())
        {
            while (await auditReader.ReadAsync())
            {
                counts[$"{auditReader.GetString(0)}.{auditReader.GetString(1)}"] = auditReader.GetInt32(2);
            }
        }

        Assert.Equal(2, counts.GetValueOrDefault("CriticalResult.Create"));
        Assert.True(counts.GetValueOrDefault("CriticalResult.Update") >= 3, string.Join(", ", counts));
        Assert.Equal(1, counts.GetValueOrDefault("CriticalNotification.Create"));
        Assert.Equal(1, counts.GetValueOrDefault(".CriticalResults.SettingsUpdated"));
        Assert.Equal(1, counts.GetValueOrDefault(".CriticalResults.LogbookPrinted"));
    }

    /// <summary>Khoa nhận trả lại lần báo không đúng khoa; khoa báo báo lại đúng khoa (lần báo 2).</summary>
    [Fact]
    public async Task WrongDepartmentReturnsTheCall_LabCallsAgain()
    {
        var lab = await LoginAsync("ktv.demo");
        var nurse = await LoginAsync("dieuduong.demo");
        var hsh = await DepartmentIdAsync("HSH");
        var noi = await DepartmentIdAsync("NOI");
        var kkb = await DepartmentIdAsync("KKB");
        var (_, tests) = await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/tests", lab);
        var platelet = tests.EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Tiểu cầu (PLT) (giả lập)").GetProperty("id").GetString();
        var now = DateTimeOffset.UtcNow;
        object Call(Guid ward, int minutesAgo) => new
        {
            receivingDepartmentId = ward, method = "Phone", notifiedAtUtc = now.AddMinutes(-minutesAgo), receiverName = "Người nghe máy (giả lập)",
            readBackCorrect = true, incorrectReadBacks = 1, repeatedTwice = false,
        };

        var (status, created) = await _client.SendJsonAsync(HttpMethod.Post, Base, lab, new
        {
            sendingDepartmentId = hsh,
            testId = platelet,
            patient = new
            {
                code = "NB-GL-0002", fullName = "Người Bệnh Chuyển Khoa", dateOfBirth = "1975-05-06", gender = "Male", address = "Ấp Mẫu (giả lập)",
                source = "Manual",
            },
            value = "8",
            unit = "K/µL",
            orderDate = DateOnly.FromDateTime(now.AddMinutes(-6).ToOffset(Vietnam).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            resultAtUtc = now.AddMinutes(-6),
            notification = Call(noi, 5),
        });
        Assert.True(status == HttpStatusCode.OK, created.ToString());
        var id = created.GetProperty("id").GetString();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/reject", nurse, new { reason = "" })).Status);
        var (_, rejected) = await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/reject", nurse, new { reason = "Người bệnh không nằm khoa Nội (giả lập)" });
        Assert.Equal("AwaitingNotification", rejected.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await _client.SendJsonAsync(HttpMethod.Get, $"{Base}/{id}", nurse)).Status);
        var (_, again) = await _client.SendJsonAsync(HttpMethod.Post, $"{Base}/{id}/notifications", lab, Call(kkb, 1));

        Assert.Equal("AwaitingAcknowledgement", again.GetProperty("status").GetString());
        var attempts = again.GetProperty("notifications").EnumerateArray().ToList();
        Assert.Equal([1, 2], attempts.Select(n => n.GetProperty("attemptNo").GetInt32()));
        Assert.Equal("Người bệnh không nằm khoa Nội (giả lập)", attempts[0].GetProperty("rejectReason").GetString());
        Assert.Equal("Khoa Khám bệnh", again.GetProperty("receivingDepartmentName").GetString());
    }
}
