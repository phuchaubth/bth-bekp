using System.Data;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Consent.Api.IntegrationTests.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Consent.Api.IntegrationTests;

/// <summary>
/// Script xóa dữ liệu chạy thử (deploy/sql/06-xoa-du-lieu-chay-thu.sql, quyết định 52) trên SQL Server thật, gọi đúng như
/// script PowerShell xoa-du-lieu-chay-thu.ps1: cả file là một lô lệnh, tham số truyền qua SqlCommand.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class TrialDataPurgeScriptTests(SqlServerFixture sql) : IAsyncLifetime
{
    /// <summary>Bảng script xóa hết dòng (hồ sơ người bệnh, người đại diện chỉ khi không có @GiuNguoiBenh).</summary>
    private static readonly string[] PurgedTables =
    [
        "ConsentForms", "ConsentSignatures", "RemoteSigningLinks", "Patients", "LegalRepresentatives",
        "NursingDailyReports", "NursingDailyStatistics", "NursingDepartmentStatistics",
    ];

    /// <summary>Bảng script giữ nguyên. Thêm bảng mới thì phải quyết định xóa hay giữ (test bên dưới báo).</summary>
    private static readonly string[] KeptTables =
    [
        "Users", "UserRoles", "UserDepartments", "Roles", "RolePermissions", "RefreshTokens", "Departments",
        "ConsentTemplates", "ConsentTemplateVersions", "SystemSettings", "NursingReportDepartments",
        "NursingReportDelegations", "TestAssignments", "ExportJobs", "__EFMigrationsHistory",
        // Sổ nội kiểm đường huyết (quyết định 57): danh mục máy, lô và sổ đã xác nhận không phải dữ liệu chạy thử của phiếu.
        "QcMeters", "QcLots", "QcBooks", "QcRuns", "QcDayReviews",
        // Kết quả nguy cấp (quyết định 60): danh mục và hồ sơ là sổ điện tử thay sổ giấy ST01, ST02, sổ Vi sinh; chạy thử ở môi
        // trường UAT. Hồ sơ chép thông tin người bệnh lúc ghi, không tham chiếu bảng Patients.
        "CriticalTests", "CriticalResults", "CriticalNotifications",
    ];

    /// <summary>Đếm mọi bảng (câu lệnh cố định, không ghép chuỗi).</summary>
    private const string CountAllTables = """
        SELECT 'AuditLogs', COUNT(*) FROM dbo.AuditLogs UNION ALL
        SELECT 'ConsentForms', COUNT(*) FROM dbo.ConsentForms UNION ALL
        SELECT 'ConsentSignatures', COUNT(*) FROM dbo.ConsentSignatures UNION ALL
        SELECT 'RemoteSigningLinks', COUNT(*) FROM dbo.RemoteSigningLinks UNION ALL
        SELECT 'Patients', COUNT(*) FROM dbo.Patients UNION ALL
        SELECT 'LegalRepresentatives', COUNT(*) FROM dbo.LegalRepresentatives UNION ALL
        SELECT 'NursingDailyReports', COUNT(*) FROM dbo.NursingDailyReports UNION ALL
        SELECT 'NursingDailyStatistics', COUNT(*) FROM dbo.NursingDailyStatistics UNION ALL
        SELECT 'NursingDepartmentStatistics', COUNT(*) FROM dbo.NursingDepartmentStatistics UNION ALL
        SELECT 'Users', COUNT(*) FROM dbo.Users UNION ALL
        SELECT 'UserRoles', COUNT(*) FROM dbo.UserRoles UNION ALL
        SELECT 'UserDepartments', COUNT(*) FROM dbo.UserDepartments UNION ALL
        SELECT 'Roles', COUNT(*) FROM dbo.Roles UNION ALL
        SELECT 'RolePermissions', COUNT(*) FROM dbo.RolePermissions UNION ALL
        SELECT 'RefreshTokens', COUNT(*) FROM dbo.RefreshTokens UNION ALL
        SELECT 'Departments', COUNT(*) FROM dbo.Departments UNION ALL
        SELECT 'ConsentTemplates', COUNT(*) FROM dbo.ConsentTemplates UNION ALL
        SELECT 'ConsentTemplateVersions', COUNT(*) FROM dbo.ConsentTemplateVersions UNION ALL
        SELECT 'SystemSettings', COUNT(*) FROM dbo.SystemSettings UNION ALL
        SELECT 'NursingReportDepartments', COUNT(*) FROM dbo.NursingReportDepartments UNION ALL
        SELECT 'NursingReportDelegations', COUNT(*) FROM dbo.NursingReportDelegations UNION ALL
        SELECT 'TestAssignments', COUNT(*) FROM dbo.TestAssignments UNION ALL
        SELECT 'ExportJobs', COUNT(*) FROM dbo.ExportJobs UNION ALL
        SELECT 'QcMeters', COUNT(*) FROM dbo.QcMeters UNION ALL
        SELECT 'QcLots', COUNT(*) FROM dbo.QcLots UNION ALL
        SELECT 'QcBooks', COUNT(*) FROM dbo.QcBooks UNION ALL
        SELECT 'QcRuns', COUNT(*) FROM dbo.QcRuns UNION ALL
        SELECT 'QcDayReviews', COUNT(*) FROM dbo.QcDayReviews UNION ALL
        SELECT 'CriticalTests', COUNT(*) FROM dbo.CriticalTests UNION ALL
        SELECT 'CriticalResults', COUNT(*) FROM dbo.CriticalResults UNION ALL
        SELECT 'CriticalNotifications', COUNT(*) FROM dbo.CriticalNotifications UNION ALL
        SELECT '__EFMigrationsHistory', COUNT(*) FROM dbo.__EFMigrationsHistory
        """;

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

    private sealed record PurgeResult(IReadOnlyDictionary<string, int> Rows, IReadOnlyList<string> PdfKeys);

    /// <summary>Nội dung file script trong mã nguồn (nhúng vào bản build test).</summary>
    private static string Script()
    {
        using var stream = typeof(TrialDataPurgeScriptTests).Assembly.GetManifestResourceStream("Deploy.06-xoa-du-lieu-chay-thu.sql")
            ?? throw new InvalidOperationException("Thiếu file deploy/sql/06-xoa-du-lieu-chay-thu.sql trong bản build test.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static async Task<PurgeResult> PurgeAsync(
        string connectionString, bool execute, bool keepPatients = false, bool restartNumbers = false, string? by = @"BV\quantri")
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(Script(), connection) { CommandTimeout = 120 };
        command.Parameters.Add("@ThucHien", SqlDbType.Bit).Value = execute;
        command.Parameters.Add("@GiuNguoiBenh", SqlDbType.Bit).Value = keepPatients;
        command.Parameters.Add("@DanhSoLaiTu1", SqlDbType.Bit).Value = restartNumbers;
        command.Parameters.Add("@NguoiThucHien", SqlDbType.NVarChar, 100).Value = (object?)by ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0), reader.GetInt32(1));
        }

        Assert.True(await reader.NextResultAsync());
        var keys = new List<string>();
        while (await reader.ReadAsync())
        {
            keys.Add(reader.GetString(0));
        }

        // Đọc tới hết lô lệnh: lỗi ở phần xóa (nếu có) được báo ra ở đây.
        while (await reader.NextResultAsync())
        {
        }

        return new PurgeResult(rows, keys);
    }

    private async Task<Dictionary<string, int>> CountsAsync()
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(CountAllTables, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync())
        {
            counts.Add(reader.GetString(0), reader.GetInt32(1));
        }

        return counts;
    }

    private async Task<T> ScalarAsync<T>(string query)
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(query, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string statement)
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(statement, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> LoginDemoAsync(string userName)
    {
        var login = await _client.LoginAsync(userName, TestSettings.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, login.Status);
        return login.AccessToken!;
    }

    private async Task<string> DemoPatientIdAsync(string token)
    {
        var (_, patients) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/patients?keyword=001085000001", token);
        return patients.GetProperty("items")[0].GetProperty("id").GetString()!;
    }

    private async Task<string> TemplateIdAsync(string token, string code)
    {
        var (_, available) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-templates/available", token);
        return available.EnumerateArray().Single(t => t.GetProperty("templateCode").GetString() == code).GetProperty("templateId").GetString()!;
    }

    private static object MorseData() => new
    {
        assessmentReason = "initial",
        morseFallHistory = "no",
        morseSecondaryDiagnosis = "yes",
        morseAmbulatoryAid = "crutches",
        morseIntravenous = "no",
        morseGait = "weak",
        morseMentalStatus = "oriented",
        measuresStandard = new[] { "callBell", "lowBed" },
        measuresMedium = new[] { "marker", "assist" },
        reassessAt = "2026-11-12T08:00",
    };

    private static Dictionary<string, string> AllOk() => new()
    {
        ["professional"] = "ok", ["drugSupply"] = "ok", ["hospitalFees"] = "ok", ["drugAdministration"] = "ok",
        ["patientFeedback"] = "ok", ["security"] = "ok", ["facilities"] = "ok", ["speakers"] = "ok",
    };

    /// <summary>Phiếu TN-01 người bệnh ký tay trên máy tính bảng, điều dưỡng xác nhận: phiếu hoàn tất có PDF đã lưu.</summary>
    private async Task<(string Id, string FormNo)> CompletedFormAsync(string nurse, string patientId)
    {
        var (_, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse, new { patientId, templateId = await TemplateIdAsync(nurse, "TN-01") });
        var id = created.GetProperty("id").GetString()!;
        var form = $"/api/v1/consent-forms/{id}";
        await _client.SendJsonAsync(HttpMethod.Put, form, nurse, new { data = MorseData(), signerKind = "Patient" });
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(HttpMethod.Post, $"{form}/submit", nurse)).Status);
        var (_, session) = await _client.SendJsonAsync(HttpMethod.Post, $"{form}/signing-session", nurse);
        var (signed, signedBody) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/patient-signature", session.GetProperty("token").GetString(),
            new { decision = "Agreed", image = Convert.ToBase64String(TestPng.Signature()), statement = "Tôi đã nghe, hiểu và đồng ý" });
        Assert.True(signed == HttpStatusCode.OK, signedBody.ToString());
        var (completedStatus, completed) = await _client.SendJsonAsync(
            HttpMethod.Post, $"{form}/complete", nurse, new { image = Convert.ToBase64String(TestPng.Signature()) });
        Assert.True(completedStatus == HttpStatusCode.OK, completed.ToString());
        return (id, completed.GetProperty("formNo").GetString()!);
    }

    /// <summary>Phiếu CK-01 chờ ký của tiếp đón, mở liên kết ký qua mã QR.</summary>
    private async Task RemoteSigningLinkAsync(string reception, string patientId)
    {
        var (_, created) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", reception, new { patientId, templateId = await TemplateIdAsync(reception, "CK-01") });
        var id = created.GetProperty("id").GetString()!;
        var data = new
        {
            examType = "bhyt",
            insuranceNumber = "DN4790000000001",
            insuranceValidFrom = "2026-01-01",
            insuranceValidTo = "2026-12-31",
            primaryCareFacility = "Trạm Y tế Phường Mẫu",
            referralPaper = "no",
            acknowledged = true,
        };
        await _client.SendJsonAsync(HttpMethod.Put, $"/api/v1/consent-forms/{id}", reception, new { data, signerKind = "Patient" });
        var (submitted, submittedBody) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{id}/submit", reception);
        Assert.True(submitted == HttpStatusCode.OK, submittedBody.ToString());
        var (linked, link) = await _client.SendJsonAsync(HttpMethod.Post, $"/api/v1/consent-forms/{id}/remote-signing", reception);
        Assert.True(linked == HttpStatusCode.OK, link.ToString());
    }

    /// <summary>
    /// Dữ liệu chạy thử qua API, như người dùng làm: phiếu hoàn tất có PDF, thu hồi và lập phiếu thay thế, phiếu chờ ký có
    /// liên kết QR, người bệnh mới có người đại diện, báo cáo điều dưỡng đã gửi, số liệu HIS khoa nhập. Kèm dữ liệu phải giữ:
    /// logo (file nằm cùng thư mục PDF), khoa báo cáo và số giường, ủy quyền, phân công kiểm thử, lần xuất dữ liệu.
    /// </summary>
    private async Task<(string CompletedFormNo, string Logo)> SeedTrialDataAsync()
    {
        var admin = await _client.LoginAsAdministratorAsync();
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var head = await LoginDemoAsync("truongkhoa.demo");
        var reception = await LoginDemoAsync("tiepdon.demo");
        var office = await LoginDemoAsync("phongdieuduong.demo");
        var reporter = await LoginDemoAsync("baocao.demo");
        var patientId = await DemoPatientIdAsync(nurse);

        var (completedId, completedNo) = await CompletedFormAsync(nurse, patientId);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/consent-forms/{completedId}/revoke", head, new { reason = "Chạy thử" })).Status);
        var (replaced, replacement) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse,
            new { patientId, templateId = await TemplateIdAsync(nurse, "TN-01"), replacesFormId = completedId });
        Assert.True(replaced == HttpStatusCode.Created, replacement.ToString());
        // Phiếu điền sẵn theo phiếu lần trước cũng tham chiếu phiếu khác.
        await ExecuteAsync(
            $"UPDATE ConsentForms SET InheritedFromFormId = '{completedId}' WHERE Id = '{replacement.GetProperty("id").GetString()}'");
        await RemoteSigningLinkAsync(reception, patientId);

        var (patientCreated, patient) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", nurse, new
        {
            fullName = "Trần Thị Chạy Thử (giả lập)",
            dateOfBirth = "1990-02-03",
            gender = "Female",
            citizenId = "036190000052",
            permanentAddress = "Số 1 Đường Thử Nghiệm, Phường Mẫu",
        });
        Assert.True(patientCreated == HttpStatusCode.Created, patient.ToString());
        var (representativeAdded, representative) = await _client.SendJsonAsync(
            HttpMethod.Post, $"/api/v1/patients/{patient.GetProperty("id").GetString()}/representatives", nurse,
            new { fullName = "Trần Văn Đại Diện (giả lập)", relationship = "Spouse" });
        Assert.True(representativeAdded is HttpStatusCode.OK or HttpStatusCode.Created, representative.ToString());

        var (_, departments) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/nursing-reports/departments", office);
        var noi = departments.EnumerateArray().Single(d => d.GetProperty("code").GetString() == "NOI").GetProperty("departmentId").GetString()!;
        var (configured, configuredBody) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/departments", office,
            new { items = new[] { new { departmentId = noi, bedCount = 40 } }, hisEntry = new Dictionary<string, string?> { ["outpatientVisits"] = noi } });
        Assert.True(configured == HttpStatusCode.OK, configuredBody.ToString());
        var (sent, sentBody) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/nursing-reports/daily", nurse, new
        {
            reportDate = Today,
            values = new { nurses = 18, previousDayPatients = 40, admissionsFromClinic = 2 },
            checks = AllOk(),
            submit = true,
        });
        Assert.True(sent == HttpStatusCode.OK, sentBody.ToString());
        var (entered, enteredBody) = await _client.SendJsonAsync(
            HttpMethod.Put, "/api/v1/nursing-reports/his-entry", nurse,
            new { reportDate = Today, values = new Dictionary<string, int?> { ["waitingAdmission"] = 2, ["outpatientVisits"] = 686 } });
        Assert.True(entered == HttpStatusCode.OK, enteredBody.ToString());
        // Số liệu HIS toàn viện: máy thử không nối HIS, ghi thẳng một dòng như khi bấm Lấy số liệu HIS.
        await ExecuteAsync(
            "INSERT INTO NursingDailyStatistics (Id, ReportDate, ValuesJson, Source, UpdatedByUserId, UpdatedAtUtc) " +
            $"SELECT NEWID(), '{Today}', N'{{\"outpatientVisits\":700}}', 'His', Id, SYSDATETIMEOFFSET() FROM Users WHERE UserName = 'phongdieuduong.demo'");

        // Phải giữ: ủy quyền (khoa Nội chỉ có một người lập báo cáo nên ghi thẳng), phân công kiểm thử, lần xuất dữ liệu, logo.
        var nurseId = await ScalarAsync<Guid>("SELECT Id FROM Users WHERE UserName = 'dieuduong.demo'");
        await ExecuteAsync(
            "INSERT INTO NursingReportDelegations (Id, DepartmentId, DelegatorUserId, DelegateUserId, StartDate, EndDate, Scope, Reason, CreatedAtUtc) " +
            $"SELECT NEWID(), '{noi}', '{nurseId}', Id, '{Today}', '{Today}', 1, N'Nghỉ phép (giả lập)', SYSDATETIMEOFFSET() " +
            "FROM Users WHERE UserName = 'truongkhoa.demo'");
        var (assigned, assignment) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/testing/assignments", admin, new
        {
            version = "Gói 75 (giả lập)",
            feature = "Báo cáo điều dưỡng",
            assignees = new[] { new { userId = nurseId, startDate = Today, dueDate = Today } },
        });
        Assert.True(assigned == HttpStatusCode.OK, assignment.ToString());
        using (var export = new HttpRequestMessage(HttpMethod.Post, "/api/v1/exports/consent-forms"))
        {
            export.Content = System.Net.Http.Json.JsonContent.Create(new { format = "Csv", columns = new[] { "formNo" } });
            export.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", reporter);
            using var exported = await _client.SendAsync(export);
            Assert.True(exported.StatusCode == HttpStatusCode.OK, await exported.Content.ReadAsStringAsync());
        }

        const string Png = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAIAAAAlC+aJAAABq0lEQVR4nO1aQRLCMAhUx3949gX+/+gLPPuSeogzZhJCIJAAtXt1ZHdDk9LAedu2U2RcrAVIcVWP+LzdkV8f75cu3VnlEcJFt6BiRmRgTHcNiZNBA1rSc4zZYBvoSsd1CP9eg2egRT+2eCrRGAZAPvlGFIYlGZgkXYWib6AOrX6WS7g6BrgRiacTEoTLiJUSK9ceocAXhVELLVA/QNQ0UPheph6kQ5IAG7BVD5K2PAAGPKgHqUEPnT1gqJ4ooDQwo0pTRC0Py4D58icw3gPOlz+hENnMgJPlT0DEhP+o/xnIU+Nq+RNySbnUHWUgKPr3QjOOJlZM0jHqfAMkgNsg/CN0GLDGYcAahwFrHAas8TXQqvVcASwXwmegX8yxSiP53SgX4TPwM+B8G7Tq5R1loICrJFAvd91+yuQoRPIaHCbgNTicJ6GW19nE5knoCgAM0Lsjs0HpVMAZ8OCB2GehXu4u9kDvEjFeZMs8sIh4DY4FHjQb3eCfp3rQHzVoxaWEZmHisIecY2rYfxp4wlmJ9MYjZ3QdA1g09Jcj8NhlgaiDryBCjh4bIvxH/Qc4GQtKXacLhQAAAABJRU5ErkJggg==";
        var (logoSaved, logoBody) = await _client.SendJsonAsync(HttpMethod.Put, "/api/v1/settings/logo", admin, new { image = Png });
        Assert.True(logoSaved == HttpStatusCode.OK, logoBody.ToString());
        var logoKey = await ScalarAsync<string>(
            "SELECT TOP (1) Value FROM SystemSettings WHERE Value LIKE '[0-9][0-9][0-9][0-9]/[0-9][0-9]/%.pdf.enc'");
        return (completedNo, logoKey);
    }

    private string DocumentFile(string key) => Path.Combine(_factory.DocumentsPath, key.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task PreviewChangesNothing_ThenDeletingRemovesTrialData_KeepsSettingsAndHistory_AndTheAppStillWorks()
    {
        var (completedNo, logoKey) = await SeedTrialDataAsync();
        var before = await CountsAsync();
        foreach (var table in PurgedTables)
        {
            Assert.True(before[table] > 0, $"Chưa có dữ liệu chạy thử trong {table}.");
        }

        var pdfKey = await ScalarAsync<string>("SELECT SignedPdfKey FROM ConsentForms WHERE SignedPdfKey IS NOT NULL");
        Assert.True(File.Exists(DocumentFile(pdfKey)));
        Assert.True(File.Exists(DocumentFile(logoKey)));

        // Chỉ đếm: số dòng đúng từng bảng, khóa file PDF đúng; không thay đổi gì, kể cả nhật ký.
        var preview = await PurgeAsync(_factory.ConnectionString, execute: false);
        Assert.Equal(PurgedTables, preview.Rows.Keys);
        foreach (var table in PurgedTables)
        {
            Assert.Equal(before[table], preview.Rows[table]);
        }

        Assert.Equal(new[] { pdfKey }, preview.PdfKeys);
        Assert.Equal(before, await CountsAsync());

        // Xóa thật.
        var purged = await PurgeAsync(_factory.ConnectionString, execute: true);
        Assert.Equal(preview.Rows, purged.Rows);
        Assert.Equal(new[] { pdfKey }, purged.PdfKeys);
        var after = await CountsAsync();
        foreach (var table in PurgedTables)
        {
            Assert.Equal(0, after[table]);
        }

        foreach (var table in KeptTables)
        {
            Assert.Equal(before[table], after[table]);
        }

        Assert.True(before["NursingReportDelegations"] > 0 && before["TestAssignments"] > 0 && before["ExportJobs"] > 0);

        // Script không đụng tới file: PowerShell chuyển file PDF của các khóa trả về; logo giữ nguyên.
        Assert.True(File.Exists(DocumentFile(pdfKey)));
        Assert.True(File.Exists(DocumentFile(logoKey)));

        // Nhật ký: các dòng cũ còn nguyên, thêm đúng một dòng ghi người chạy và số dòng đã xóa.
        Assert.Equal(before["AuditLogs"] + 1, after["AuditLogs"]);
        var details = await ScalarAsync<string>(
            "SELECT Details FROM AuditLogs WHERE Action = 'Maintenance.TrialDataPurged' AND UserName = N'BV\\quantri' AND UserId IS NULL");
        using (var json = JsonDocument.Parse(details))
        {
            Assert.False(json.RootElement.GetProperty("keepPatients").GetBoolean());
            Assert.False(json.RootElement.GetProperty("restartFormNumbers").GetBoolean());
            var deleted = json.RootElement.GetProperty("deleted").EnumerateArray()
                .ToDictionary(d => d.GetProperty("table").GetString()!, d => d.GetProperty("rows").GetInt32(), StringComparer.Ordinal);
            Assert.Equal(preview.Rows, deleted);
        }

        // Ứng dụng vẫn chạy: lập được người bệnh, phiếu mới (số phiếu chạy tiếp, không trùng số phiếu chạy thử), báo cáo mới.
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var (patientCreated, patient) = await _client.SendJsonAsync(HttpMethod.Post, "/api/v1/patients", nurse, new
        {
            fullName = "Lê Văn Dùng Thật (giả lập)",
            dateOfBirth = "1980-07-08",
            gender = "Male",
            citizenId = "036080000052",
        });
        Assert.True(patientCreated == HttpStatusCode.Created, patient.ToString());
        var (formCreated, form) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse,
            new { patientId = patient.GetProperty("id").GetString(), templateId = await TemplateIdAsync(nurse, "TN-01") });
        Assert.True(formCreated == HttpStatusCode.Created, form.ToString());
        Assert.True(
            string.CompareOrdinal(form.GetProperty("formNo").GetString(), completedNo) > 0,
            $"{form.GetProperty("formNo").GetString()} phải sau {completedNo}.");
        var (_, daily) = await _client.SendJsonAsync(HttpMethod.Get, $"/api/v1/nursing-reports/daily?date={Today}", nurse);
        Assert.Equal("New", daily.GetProperty("status").GetString());
        var (_, forms) = await _client.SendJsonAsync(HttpMethod.Get, "/api/v1/consent-forms", nurse);
        Assert.Equal(1, forms.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task KeepPatients_KeepsPatientsAndRepresentatives_AndRestartingNumbersStartsAgainFromOne()
    {
        await SeedTrialDataAsync();
        var before = await CountsAsync();

        var preview = await PurgeAsync(_factory.ConnectionString, execute: false, keepPatients: true, restartNumbers: true);
        Assert.DoesNotContain("Patients", preview.Rows.Keys);
        Assert.DoesNotContain("LegalRepresentatives", preview.Rows.Keys);
        await PurgeAsync(_factory.ConnectionString, execute: true, keepPatients: true, restartNumbers: true, by: null);

        var after = await CountsAsync();
        Assert.Equal(0, after["ConsentForms"]);
        Assert.Equal(0, after["NursingDailyReports"]);
        Assert.Equal(before["Patients"], after["Patients"]);
        Assert.Equal(before["LegalRepresentatives"], after["LegalRepresentatives"]);
        Assert.Equal(1, await ScalarAsync<int>(
            "SELECT COUNT(*) FROM AuditLogs WHERE Action = 'Maintenance.TrialDataPurged' AND UserName IS NULL " +
            "AND JSON_VALUE(Details, '$.keepPatients') = 'true' AND JSON_VALUE(Details, '$.restartFormNumbers') = 'true'"));

        // Người bệnh còn: lập phiếu ngay; số phiếu bắt đầu lại từ 000001.
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var (created, form) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse,
            new { patientId = await DemoPatientIdAsync(nurse), templateId = await TemplateIdAsync(nurse, "TN-01") });
        Assert.True(created == HttpStatusCode.Created, form.ToString());
        Assert.EndsWith("-000001", form.GetProperty("formNo").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnErrorAtTheLastStep_RollsBackEveryDeletionAndTheNumbering()
    {
        var (completedNo, _) = await SeedTrialDataAsync();
        // Ghi nhật ký (bước cuối, sau mọi lệnh xóa và đặt lại số phiếu) bị từ chối: phải hoàn tác tất cả.
        await ExecuteAsync("ALTER TABLE dbo.AuditLogs ADD CONSTRAINT CK_Test_NoPurge CHECK (Action <> N'Maintenance.TrialDataPurged')");
        var before = await CountsAsync();

        var error = await Assert.ThrowsAsync<SqlException>(() => PurgeAsync(_factory.ConnectionString, execute: true, restartNumbers: true));
        Assert.Contains("CK_Test_NoPurge", error.Message, StringComparison.Ordinal);

        Assert.Equal(before, await CountsAsync());
        await ExecuteAsync("ALTER TABLE dbo.AuditLogs DROP CONSTRAINT CK_Test_NoPurge");
        var nurse = await LoginDemoAsync("dieuduong.demo");
        var (created, form) = await _client.SendJsonAsync(
            HttpMethod.Post, "/api/v1/consent-forms", nurse,
            new { patientId = await DemoPatientIdAsync(nurse), templateId = await TemplateIdAsync(nurse, "TN-01") });
        Assert.True(created == HttpStatusCode.Created, form.ToString());
        Assert.True(
            string.CompareOrdinal(form.GetProperty("formNo").GetString(), completedNo) > 0,
            $"Số phiếu {form.GetProperty("formNo").GetString()} phải chạy tiếp sau {completedNo} (không đặt lại).");
    }

    [Fact]
    public async Task EveryTableIsEitherPurgedOrKept_AndOnlyPurgedTablesReferencePurgedTables()
    {
        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = new SqlCommand("SELECT name FROM sys.tables WHERE is_ms_shipped = 0", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        // Bảng mới (migration mới) phải được xếp vào nhóm xóa (sửa script) hoặc nhóm giữ.
        Assert.Equal(
            PurgedTables.Concat(KeptTables).Append("AuditLogs").Order(StringComparer.Ordinal).ToArray(),
            tables.Order(StringComparer.Ordinal).ToArray());

        var references = new List<(string Child, string Parent)>();
        await using (var command = new SqlCommand(
            "SELECT OBJECT_NAME(parent_object_id), OBJECT_NAME(referenced_object_id) FROM sys.foreign_keys", connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                references.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        // Bảng giữ lại không được tham chiếu bảng bị xóa (nếu có, xóa sẽ lỗi khóa ngoại hoặc mất dữ liệu đang giữ).
        Assert.DoesNotContain(references, r => PurgedTables.Contains(r.Parent) && !PurgedTables.Contains(r.Child));
        // Kiểm tra trên có ý nghĩa: các bảng bị xóa đúng là có tham chiếu nhau.
        Assert.Contains(references, r => r.Child == "ConsentSignatures" && r.Parent == "ConsentForms");
    }

    [Fact]
    public async Task WithoutParameters_OrOnAnotherDatabase_NothingIsDeleted()
    {
        var before = await CountsAsync();

        // Chạy thẳng file (không khai báo tham số): SQL Server báo lỗi trước khi chạy.
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(Script(), connection);
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Contains("@ThucHien", error.Message, StringComparison.Ordinal);
        }

        // Tham số rỗng: báo lỗi 51021.
        await using (var connection = new SqlConnection(_factory.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand(Script(), connection);
            command.Parameters.Add("@ThucHien", SqlDbType.Bit).Value = DBNull.Value;
            command.Parameters.Add("@GiuNguoiBenh", SqlDbType.Bit).Value = DBNull.Value;
            command.Parameters.Add("@DanhSoLaiTu1", SqlDbType.Bit).Value = DBNull.Value;
            command.Parameters.Add("@NguoiThucHien", SqlDbType.NVarChar, 100).Value = DBNull.Value;
            var error = await Assert.ThrowsAsync<SqlException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(51021, error.Number);
        }

        // CSDL khác (không có bảng của phần mềm): báo lỗi 51020.
        var other = new SqlConnectionStringBuilder(_factory.ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        var wrongDatabase = await Assert.ThrowsAsync<SqlException>(() => PurgeAsync(other, execute: true));
        Assert.Equal(51020, wrongDatabase.Number);

        Assert.Equal(before, await CountsAsync());
    }
}
