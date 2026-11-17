using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>Xuất dữ liệu, báo cáo và trang tổng quan với SQL Server thật: quyền, lịch sử xuất, audit, số liệu khớp bộ lọc, hiệu năng.</summary>
[Collection(SqlServerCollection.Name)]
public sealed class ExportsAndReportsApiTests(SqlServerFixture sql) : IAsyncLifetime
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

    private async Task<string> LoginDemoAsync(string userName)
    {
        var login = await _client.LoginAsync(userName, TestSettings.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return login.AccessToken!;
    }

    private async Task<string> DemoPatientIdAsync(string token, string citizenId = "001085000001")
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/patients?keyword={citizenId}", token);
        return patients.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    private async Task<string> TemplateIdAsync(string token, string code)
    {
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", token);
        return available.EnumerateArray().Single(t => t.GetProperty("templateCode").GetString() == code).GetProperty("templateId").GetString()!;
    }

    private static object Ck01Data() => new
    {
        examType = "bhyt",
        insuranceNumber = "DN4790000000001",
        insuranceValidFrom = "2026-01-01",
        insuranceValidTo = "2026-12-31",
        primaryCareFacility = "Trạm Y tế Phường Mẫu",
        referralPaper = "no",
        acknowledged = true,
    };

    /// <summary>Tiếp đón lập một phiếu CK-01 hoàn tất (người bệnh ký tay, tiếp đón xác nhận).</summary>
    private async Task<string> CompletedCk01Async(string receptionist, string patientId, string templateId, string decision = "Agreed")
    {
        var id = await SignedCk01Async(receptionist, patientId, templateId, decision);
        var (completed, completedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{id}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(completed == HttpStatusCode.OK, completedBody.ToString());
        return id;
    }

    /// <summary>Tiếp đón lập một phiếu CK-01, người bệnh đã ký tay, chờ tiếp đón xác nhận.</summary>
    private async Task<string> SignedCk01Async(string receptionist, string patientId, string templateId, string decision = "Agreed")
    {
        var (_, created) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId });
        var id = created.GetProperty("id").GetString()!;
        var form = $"/api/v1/consent-forms/{id}";
        await _client.SendJsonAsync(HttpMethod.Put, form, receptionist, new { data = Ck01Data(), signerKind = "Patient" });
        var (submitted, body) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist);
        Assert.True(submitted == HttpStatusCode.OK, body.ToString());
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        var (signed, signedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString(),
            new { decision, image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signed == HttpStatusCode.OK, signedBody.ToString());
        return id;
    }

    private async Task<HttpResponseMessage> ExportAsync(string token, object request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exports/consent-forms") { Content = JsonContent.Create(request) };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _client.SendAsync(message);
    }

    [Fact]
    public async Task Export_RequiresPermission_MasksSensitiveData_AndRecordsHistoryAndAudit()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var reporter = await LoginDemoAsync("baocao.demo");
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var admin = await _client.LoginAsAdministratorAsync();
        var patientId = await DemoPatientIdAsync(receptionist);
        var ck01 = await TemplateIdAsync(receptionist, "CK-01");
        var completedId = await CompletedCk01Async(receptionist, patientId, ck01, "Refused");
        var (_, draft) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId = ck01 });

        // Điều dưỡng không có quyền xuất → 403. Nhân viên báo cáo không có quyền xuất dữ liệu nhạy cảm → 403.
        using var forbidden = await ExportAsync(nurse, new { format = "Csv", columns = new[] { "formNo" } });
        using var sensitiveForbidden = await ExportAsync(reporter, new { format = "Csv", columns = new[] { "formNo", "citizenId" }, includeSensitiveData = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, sensitiveForbidden.StatusCode);

        var (columnsStatus, columns) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/exports/consent-forms/columns", reporter);
        Assert.Equal(HttpStatusCode.OK, columnsStatus);
        Assert.Contains(columns.EnumerateArray(), c => c.GetProperty("key").GetString() == "citizenId" && c.GetProperty("sensitive").GetBoolean());

        // CSV che dữ liệu (mặc định).
        using var csv = await ExportAsync(reporter, new
        {
            format = "Csv",
            filter = new { templateId = ck01 },
            columns = new[] { "formNo", "status", "patientName", "citizenId", "decision" },
            includeContent = true,
        });
        var csvText = Encoding.UTF8.GetString(await csv.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType!.MediaType);
        Assert.Equal("no-store", csv.Headers.CacheControl!.ToString());
        Assert.Equal("2", csv.Headers.GetValues("X-Export-Row-Count").Single());
        var jobId = csv.Headers.GetValues("X-Export-Job-Id").Single();
        var lines = csvText.TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("Số phiếu,Trạng thái,Họ tên người bệnh,Số CCCD,Ý kiến,Hình thức khám, chữa bệnh".Replace("Hình thức khám, chữa bệnh", "\"Hình thức khám, chữa bệnh\""), lines[0], StringComparison.Ordinal);
        Assert.Equal(3, lines.Length);
        Assert.Contains("Hoàn tất", lines[1], StringComparison.Ordinal);
        Assert.Contains("Từ chối", lines[1], StringComparison.Ordinal);
        Assert.Contains("0010******01", lines[1], StringComparison.Ordinal);
        Assert.DoesNotContain("001085000001", csvText, StringComparison.Ordinal);
        Assert.Contains("DN**********001", lines[1], StringComparison.Ordinal);
        Assert.Contains("Nháp", lines[2], StringComparison.Ordinal);

        // Excel đầy đủ (quản trị có quyền exports.sensitive): file ZIP hợp lệ, có đúng số dòng.
        using var xlsx = await ExportAsync(admin, new { format = "Xlsx", columns = new[] { "formNo", "citizenId" }, includeSensitiveData = true });
        var xlsxBytes = await xlsx.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", xlsx.Content.Headers.ContentType!.MediaType);
        Assert.Equal([0x50, 0x4B], xlsxBytes[..2]);
        Assert.EndsWith(".xlsx", xlsx.Content.Headers.ContentDisposition!.FileName!.Trim('"'), StringComparison.Ordinal);

        // Không có phiếu khớp → 422.
        using var none = await ExportAsync(reporter, new { format = "Csv", filter = new { status = "Revoked" }, columns = new[] { "formNo" } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, none.StatusCode);

        // Lịch sử: nhân viên báo cáo thấy lần của mình; quản trị (audit.read) thấy cả hai.
        var (_, own) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/exports", reporter);
        var (_, all) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/exports", admin);
        var mine = Assert.Single(own.GetProperty("items").EnumerateArray());
        Assert.Equal(jobId, mine.GetProperty("id").GetString());
        Assert.Equal(2, mine.GetProperty("rowCount").GetInt32());
        Assert.Equal("CK-01", mine.GetProperty("filter").GetProperty("templateCode").GetString());
        Assert.Equal(2, all.GetProperty("totalCount").GetInt32());
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(xlsxBytes)),
            all.GetProperty("items").EnumerateArray().Single(j => j.GetProperty("includesSensitiveData").GetBoolean()).GetProperty("fileSha256").GetString());

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var jobs = new SqlCommand("SELECT COUNT(*) FROM ExportJobs", connection);
        Assert.Equal(2, (int)(await jobs.ExecuteScalarAsync())!);
        await using var audit = new SqlCommand("SELECT Details FROM AuditLogs WHERE Action = 'Exports.ConsentForms' AND EntityId = @id", connection);
        audit.Parameters.AddWithValue("@id", jobId);
        var details = (string)(await audit.ExecuteScalarAsync())!;
        Assert.Contains("\"rows\":2", details, StringComparison.Ordinal);
        Assert.DoesNotContain("001085000001", details, StringComparison.Ordinal);
        Assert.NotNull(completedId);
        Assert.NotEqual(JsonValueKind.Undefined, draft.ValueKind);
    }

    [Fact]
    public async Task Reports_MatchTheFilter_AndRespectDepartmentScope()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var reporter = await LoginDemoAsync("baocao.demo");
        var head = await LoginDemoAsync("truongkhoa.demo");
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var patientId = await DemoPatientIdAsync(receptionist);
        var ck01 = await TemplateIdAsync(receptionist, "CK-01");
        await CompletedCk01Async(receptionist, patientId, ck01, "Agreed");
        await CompletedCk01Async(receptionist, patientId, ck01, "Refused");
        await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId = ck01 });
        var tn01 = await TemplateIdAsync(nurse, "TN-01");
        await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = tn01 });

        var (status, report) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/reports/summary", reporter);
        var (nurseStatus, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/reports/summary", nurse);
        var (headStatus, headReport) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/reports/summary", head);
        var (badRange, _) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/reports/summary?from=2026-12-01&to=2026-11-01", reporter);

        Assert.Equal(HttpStatusCode.OK, status);
        var totals = report.GetProperty("totals");
        Assert.Equal(4, totals.GetProperty("total").GetInt32());
        Assert.Equal(2, totals.GetProperty("completed").GetInt32());
        Assert.Equal(2, totals.GetProperty("draft").GetInt32());
        Assert.Equal(0.5, report.GetProperty("decisions").GetProperty("refusalRate").GetDouble());
        Assert.Equal(2, report.GetProperty("completionTime").GetProperty("count").GetInt32());
        Assert.Equal(["KKB", "NOI"], report.GetProperty("byDepartment").EnumerateArray().Select(d => d.GetProperty("code").GetString()));
        Assert.Equal(HttpStatusCode.Forbidden, nurseStatus);
        // Trưởng khoa Nội chỉ thấy phiếu khoa Nội.
        Assert.Equal(HttpStatusCode.OK, headStatus);
        Assert.Equal(1, headReport.GetProperty("totals").GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, badRange);

        // Trang tổng quan: tiếp đón thấy việc của mình, kể cả phiếu người bệnh đã ký đang chờ tiếp đón xác nhận.
        var waitingId = await SignedCk01Async(receptionist, patientId, ck01);
        var (dashStatus, dashboard) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/dashboard", receptionist);
        Assert.Equal(HttpStatusCode.OK, dashStatus);
        Assert.Equal(2, dashboard.GetProperty("completedToday").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("drafts").GetInt32());
        Assert.Equal(2, dashboard.GetProperty("myOpenForms").GetProperty("total").GetInt32());
        var awaiting = dashboard.GetProperty("awaitingMyConfirmation");
        Assert.Equal(1, awaiting.GetProperty("total").GetInt32());
        Assert.Equal(waitingId, awaiting.GetProperty("items")[0].GetProperty("id").GetString());
        Assert.Equal(14, dashboard.GetProperty("last14Days").GetArrayLength());
        Assert.Equal(4, dashboard.GetProperty("last14Days").EnumerateArray().Last().GetProperty("created").GetInt32());
    }

    /// <summary>
    /// "Chờ tôi xác nhận" theo thứ tự các bước xác nhận (quyết định 13/11/2026), với SQL Server thật và tài khoản demo: tiếp
    /// đón (Khoa Khám bệnh) lập cho Khoa Nội phiếu nhận định BM10 (hướng dẫn → điều dưỡng → bác sĩ) và phiếu tư vấn (người tư
    /// vấn → điều dưỡng khoa). Mỗi người chỉ thấy phiếu đang đến lượt mình, khớp với nút xác nhận trên phiếu.
    /// </summary>
    [Fact]
    public async Task Dashboard_AwaitingMyConfirmation_FollowsTheConfirmationSteps()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        // Điều dưỡng demo có thêm vai trò bác sĩ (ví dụ cấu hình nhầm): xác nhận bước điều dưỡng rồi thì không còn lượt xác nhận cuối.
        await AddRoleAsync(await _client.LoginAsAdministratorAsync(), "dieuduong.demo", "DOCTOR");
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var doctor = await LoginDemoAsync("bacsi.demo");
        var patientId = await DemoPatientIdAsync(receptionist);
        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/departments", receptionist);
        var noi = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "NOI").GetProperty("id").GetString()!;
        var bm10 = await PatientSignedAsync(receptionist, patientId, noi, "QT-KNGT-BM10", Bm10Data());
        var portal = await PatientSignedAsync(receptionist, patientId, noi, "TUVAN-CONGTT");

        // Người bệnh vừa ký: bước đầu của cả hai phiếu là của tiếp đón; điều dưỡng (xác nhận cuối phiếu tư vấn) và bác sĩ
        // (xác nhận cuối BM10) chưa đến lượt.
        Assert.Equal([bm10, portal], await AwaitingMineAsync(receptionist, bm10, portal));
        Assert.Empty(await AwaitingMineAsync(nurse, bm10, portal));
        Assert.Empty(await AwaitingMineAsync(doctor, bm10, portal));

        await ConfirmStepAsync(receptionist, bm10, "screeningStaff");
        await ConfirmStepAsync(receptionist, portal, "consultant");

        // Bước trung gian của điều dưỡng (BM10) và xác nhận cuối của điều dưỡng khoa (phiếu tư vấn).
        Assert.Empty(await AwaitingMineAsync(receptionist, bm10, portal));
        Assert.Equal([bm10, portal], await AwaitingMineAsync(nurse, bm10, portal));
        Assert.Empty(await AwaitingMineAsync(doctor, bm10, portal));

        await ConfirmStepAsync(nurse, bm10, "nurse");

        // Điều dưỡng đã xác nhận một bước của BM10 nên không còn lượt xác nhận cuối, dù có vai trò bác sĩ.
        Assert.Equal([portal], await AwaitingMineAsync(nurse, bm10, portal));
        Assert.Equal([bm10], await AwaitingMineAsync(doctor, bm10, portal));
    }

    /// <summary>Phiếu nhận định BM10 đầy đủ, người lớn, không cấp cứu (giả lập).</summary>
    private static Dictionary<string, string> Bm10Data() => new()
    {
        ["consultingRoom"] = "Phòng 05",
        ["selfFallHistory"] = "no",
        ["selfMobilityAid"] = "no",
        ["infectionSymptoms"] = "no",
        ["staffMobilityAid"] = "no",
        ["staffUnsteady"] = "no",
        ["pulse"] = "80",
        ["temperature"] = "37",
        ["bloodPressure"] = "120/80",
        ["respiratoryRate"] = "18",
        ["height"] = "160",
        ["weight"] = "55",
        ["bmi"] = "21,5",
        ["emergencySigns"] = "yes",
        ["emergencySignsDetail"] = "Khó thở",
        ["nurseEduFall"] = "notNeeded",
        ["nurseEduWound"] = "notNeeded",
        ["dietCounseling"] = "notRequired",
        ["physicianEduFall"] = "notRequired",
        ["physicianEduWound"] = "notRequired",
        ["physicianEduPain"] = "notRequired",
        ["physicianEduNewPatient"] = "notRequired",
        ["physicianEduOther"] = "notRequired",
    };

    /// <summary>
    /// Tiếp đón lập phiếu theo mẫu <paramref name="templateCode"/> cho khoa <paramref name="departmentId"/> (quyết định 19), trình
    /// ký; người bệnh ký trên điện thoại.
    /// </summary>
    private async Task<string> PatientSignedAsync(
        string receptionist, string patientId, string departmentId, string templateCode, object? data = null)
    {
        var templateId = await TemplateIdAsync(receptionist, templateCode);
        var (createdStatus, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", receptionist, new { patientId, templateId, departmentId });
        Assert.True(createdStatus == HttpStatusCode.Created, created.ToString());
        var id = created.GetProperty("id").GetString()!;
        var form = $"/api/v1/consent-forms/{id}";
        if (data is not null)
        {
            var (updated, updatedBody) = await _client.SendJsonAsync(HttpMethod.Put, form, receptionist, new { data, signerKind = "Patient" });
            Assert.True(updated == HttpStatusCode.OK, updatedBody.ToString());
        }

        var (submitted, submittedBody) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", receptionist);
        Assert.True(submitted == HttpStatusCode.OK, submittedBody.ToString());
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", receptionist);
        // BM10 chỉ ký xác nhận (quyết định bị bỏ qua); phiếu tư vấn hỏi Đồng ý/Không đồng ý.
        var (signed, signedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString(),
            new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signed == HttpStatusCode.OK, signedBody.ToString());
        return id;
    }

    private async Task ConfirmStepAsync(string token, string formId, string stepKey)
    {
        var (status, body) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{formId}/confirm-step", token,
            new { stepKey, image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(status == HttpStatusCode.OK, body.ToString());
    }

    /// <summary>
    /// Phiếu trong "Chờ tôi xác nhận" của người dùng. Kiểm tra luôn số đếm khớp danh sách, và mỗi phiếu
    /// <paramref name="formIds"/> có trong danh sách khi và chỉ khi mở phiếu thấy nút xác nhận (bước hoặc xác nhận cuối).
    /// </summary>
    private async Task<List<string>> AwaitingMineAsync(string token, params string[] formIds)
    {
        var (status, dashboard) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/dashboard", token);
        Assert.Equal(HttpStatusCode.OK, status);
        var awaiting = dashboard.GetProperty("awaitingMyConfirmation");
        var listed = awaiting.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();
        Assert.Equal(listed.Count, awaiting.GetProperty("total").GetInt32());
        foreach (var formId in formIds)
        {
            var (formStatus, form) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/consent-forms/{formId}", token);
            Assert.True(formStatus == HttpStatusCode.OK, form.ToString());
            var actions = form.GetProperty("actions");
            Assert.Equal(
                actions.GetProperty("canConfirmStep").GetBoolean() || actions.GetProperty("canConfirm").GetBoolean(),
                listed.Contains(formId));
        }

        return listed;
    }

    /// <summary>Quản trị thêm vai trò <paramref name="roleCode"/> cho tài khoản <paramref name="userName"/>, giữ nguyên thông tin khác.</summary>
    private async Task AddRoleAsync(string admin, string userName, string roleCode)
    {
        var (_, users) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/users?keyword={userName}", admin);
        var id = users.GetProperty("items").EnumerateArray()
            .Single(u => u.GetProperty("userName").GetString() == userName).GetProperty("id").GetString()!;
        var (_, user) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/users/{id}", admin);
        var (_, roles) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/roles", admin);
        var roleIds = user.GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("id").GetString()!)
            .Append(roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == roleCode).GetProperty("id").GetString()!)
            .ToArray();
        var (status, body) = await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/users/{id}", admin, new
        {
            fullName = user.GetProperty("fullName").GetString(),
            title = user.GetProperty("title").GetString(),
            departmentId = user.GetProperty("departmentId").GetString(),
            roleIds,
        });
        Assert.True(status == HttpStatusCode.OK, body.ToString());
    }

    /// <summary>Tiêu chí nghiệm thu: xuất 10.000 phiếu giả lập dưới 30 giây. Phiếu được chèn thẳng vào CSDL (đã mã hóa nội dung).</summary>
    [Fact]
    public async Task Export_TenThousandForms_CompletesUnder30Seconds()
    {
        var receptionist = await LoginDemoAsync("tiepdon.demo");
        var admin = await _client.LoginAsAdministratorAsync();
        var patientId = await DemoPatientIdAsync(receptionist);
        var ck01 = await TemplateIdAsync(receptionist, "CK-01");
        var seedId = await CompletedCk01Async(receptionist, patientId, ck01);

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        // Nhân bản phiếu mẫu (giữ nguyên các cột mã hóa) thành 9.999 phiếu nữa với số phiếu và thời điểm khác nhau.
        await using (var clone = new SqlCommand(
            @"DECLARE @i int = 1;
              WHILE @i < 10000 BEGIN
                INSERT INTO ConsentForms (Id, FormNo, PatientId, TemplateId, TemplateVersionId, DepartmentId, Status, DataEncrypted, SignerKind,
                    RepresentativeId, SnapshotEncrypted, CreatedByUserId, CreatedAtUtc, UpdatedAtUtc, SubmittedAtUtc, CompletedAtUtc,
                    Decision, SignedPdfKey, SignedPdfSha256, SignedPdfSize)
                SELECT NEWID(), CONCAT('PDT-9999-', RIGHT('000000' + CAST(@i AS varchar(6)), 6)), PatientId, TemplateId, TemplateVersionId,
                    DepartmentId, Status, DataEncrypted, SignerKind, RepresentativeId, SnapshotEncrypted, CreatedByUserId,
                    DATEADD(MINUTE, -@i, CreatedAtUtc), UpdatedAtUtc, SubmittedAtUtc, CompletedAtUtc, Decision, SignedPdfKey, SignedPdfSha256, SignedPdfSize
                FROM ConsentForms WHERE Id = @id;
                SET @i += 1;
              END", connection) { CommandTimeout = 300 })
        {
            clone.Parameters.AddWithValue("@id", Guid.Parse(seedId));
            await clone.ExecuteNonQueryAsync();
        }

        var columns = new[]
        {
            "formNo", "status", "templateCode", "templateName", "department", "createdBy", "createdAt", "completedAt", "patientCode",
            "patientName", "dateOfBirth", "gender", "citizenId", "phoneNumber", "address", "signer", "decision", "confirmedBy",
        };
        var timer = Stopwatch.StartNew();
        using var xlsx = await ExportAsync(admin, new { format = "Xlsx", columns, includeContent = true, filter = new { templateId = ck01 } });
        var bytes = await xlsx.Content.ReadAsByteArrayAsync();
        var xlsxElapsed = timer.Elapsed;
        timer.Restart();
        using var csv = await ExportAsync(admin, new { format = "Csv", columns, includeSensitiveData = true });
        await csv.Content.ReadAsByteArrayAsync();
        var csvElapsed = timer.Elapsed;

        Assert.True(xlsx.StatusCode == HttpStatusCode.OK, await xlsx.Content.ReadAsStringAsync());
        Assert.Equal("10000", xlsx.Headers.GetValues("X-Export-Row-Count").Single());
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.True(xlsxElapsed < TimeSpan.FromSeconds(30), $"Xuất Excel 10.000 phiếu mất {xlsxElapsed}.");
        Assert.True(csvElapsed < TimeSpan.FromSeconds(30), $"Xuất CSV 10.000 phiếu mất {csvElapsed}.");
        Assert.InRange(bytes.Length, 100_000, 20_000_000);

        var (_, report) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/reports/summary?from=2026-01-01", admin);
        Assert.Equal(10000, report.GetProperty("totals").GetProperty("completed").GetInt32());
    }
}
