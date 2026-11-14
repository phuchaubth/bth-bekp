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
        var (completed, completedBody) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/complete", receptionist, new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(completed == HttpStatusCode.OK, completedBody.ToString());
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

        // Trang tổng quan: tiếp đón thấy việc của mình.
        var (dashStatus, dashboard) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/dashboard", receptionist);
        Assert.Equal(HttpStatusCode.OK, dashStatus);
        Assert.Equal(2, dashboard.GetProperty("completedToday").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("drafts").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("myOpenForms").GetProperty("total").GetInt32());
        Assert.Equal(14, dashboard.GetProperty("last14Days").GetArrayLength());
        Assert.Equal(3, dashboard.GetProperty("last14Days").EnumerateArray().Last().GetProperty("created").GetInt32());
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
