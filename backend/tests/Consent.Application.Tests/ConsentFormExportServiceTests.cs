using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Exports;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ConsentFormExportServiceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    private static readonly string[] SomeColumns =
        ["formNo", "patientName", "citizenId", "phoneNumber", "status", "decision", "confirmedBy", "createdAt", "dateOfBirth"];

    /// <summary>
    /// Khoa Khám bệnh: một phiếu CK-01 hoàn tất (người bệnh đồng ý) và một phiếu CK-01 nháp. Khoa Nội: một phiếu TN-01 đã
    /// trình ký (điểm Morse 40 – nguy cơ trung bình). Các phiếu lập cách nhau 1 giờ.
    /// </summary>
    private static async Task<(ConsentScenario S, Guid Completed, Guid Draft, Guid Tn01)> Scenario()
    {
        var ct = CancellationToken.None;
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var completed = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct)).Value.Id;
        await s.Service.UpdateAsync(completed, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), ct);
        Assert.True((await s.Service.SubmitAsync(completed, ct)).IsSuccess);
        Assert.True((await s.Signing.StartSigningAsync(completed, ct)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        Assert.True((await s.Signing.SignAsPatientAsync(completed, new(ConsentDecision.Agreed, SignaturePng), ct)).IsSuccess);
        staffLogsInAgain();
        Assert.True((await s.Signing.CompleteAsync(completed, new(SignaturePng), ct)).IsSuccess);

        s.Ctx.Clock.Advance(TimeSpan.FromHours(1));
        var draft = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), ct)).Value.Id;

        s.Ctx.Clock.Advance(TimeSpan.FromHours(1));
        s.LogIn(s.Noi);
        var tn01 = (await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), ct)).Value.Id;
        await s.Service.UpdateAsync(tn01, new(Tn01Data(), ConsentSignerKind.Representative, s.Patient.Representatives.Single().Id), ct);
        Assert.True((await s.Service.SubmitAsync(tn01, ct)).IsSuccess);
        return (s, completed, draft, tn01);
    }

    /// <summary>Tiền sử té ngã (25 điểm) và có từ 2 chẩn đoán (15 điểm): tổng 40, nguy cơ trung bình.</summary>
    private static JsonObject Tn01Data() => new()
    {
        ["bed"] = "B12",
        ["assessmentReason"] = "initial",
        ["morseFallHistory"] = "yes",
        ["morseSecondaryDiagnosis"] = "yes",
        ["morseAmbulatoryAid"] = "none",
        ["morseIntravenous"] = "no",
        ["morseGait"] = "normal",
        ["morseMentalStatus"] = "oriented",
        ["measuresStandard"] = new JsonArray("callBell", "lowBed"),
        ["measuresMedium"] = new JsonArray("marker"),
        ["reassessAt"] = "2026-11-12T08:00",
    };

    private static ConsentFormExportRequest Request(
        ConsentFormExportFilter? filter = null,
        IReadOnlyList<string>? columns = null,
        ExportFormat format = ExportFormat.Xlsx,
        bool content = false,
        bool sensitive = false) =>
        new(format, filter, columns ?? SomeColumns, content, sensitive);

    /// <summary>Phòng KHTH (xem toàn viện, được xuất dữ liệu).</summary>
    private static void LogInAsReporter(ConsentScenario s, params string[] extra) =>
        s.LogInAs(s.Kkb, SystemRoles.Reporter, [Permissions.ExportsRun, Permissions.ScopeAllDepartments, .. extra]);

    [Fact]
    public async Task Export_UsesVietnameseHeaders_MasksSensitiveData_AndRecordsTheJobAndAudit()
    {
        var (s, completed, _, _) = await Scenario();
        LogInAsReporter(s);

        var result = await s.Ctx.CreateConsentFormExportService().ExportAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var writer = s.Ctx.XlsxWriter;
        // Theo thứ tự của danh mục cột, không theo thứ tự gửi lên.
        Assert.Equal(
            ["Số phiếu", "Trạng thái", "Thời điểm lập", "Họ tên người bệnh", "Ngày sinh", "Số CCCD", "Số điện thoại", "Ý kiến", "Người xác nhận"],
            writer.Headers);
        Assert.Equal(3, writer.Rows.Count);
        Assert.Equal(s.Ctx.Store.ConsentForms.Single(f => f.Id == completed).FormNo, writer.Cell(0, "Số phiếu"));
        Assert.Equal("Hoàn tất", writer.Cell(0, "Trạng thái"));
        Assert.Equal("Đồng ý", writer.Cell(0, "Ý kiến"));
        Assert.NotNull(writer.Cell(0, "Người xác nhận"));
        Assert.Equal("Nháp", writer.Cell(1, "Trạng thái"));
        Assert.Null(writer.Cell(1, "Ý kiến"));
        // Giờ Việt Nam; ngày sinh là ngày (để Excel định dạng).
        Assert.Equal(new DateTime(2026, 11, 11, 15, 0, 0), writer.Cell(0, "Thời điểm lập"));
        Assert.Equal(new DateOnly(1988, 4, 5), writer.Cell(0, "Ngày sinh"));
        Assert.All(writer.Rows.Select((_, i) => writer.Cell(i, "Số CCCD")), cccd => Assert.Equal("0361******25", cccd));
        Assert.All(writer.Rows.Select((_, i) => writer.Cell(i, "Số điện thoại")), phone => Assert.Equal("090*****25", phone));

        var file = result.Value;
        var job = Assert.Single(s.Ctx.Store.ExportJobs);
        Assert.Equal(file.JobId, job.Id);
        Assert.Equal(3, job.RowCount);
        Assert.Equal(ExportFormat.Xlsx, job.Format);
        Assert.False(job.IncludesSensitiveData);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(file.Content)), job.FileSha256);
        Assert.Equal(file.Content.LongLength, job.FileSizeBytes);
        Assert.EndsWith(".xlsx", file.FileName, StringComparison.Ordinal);
        Assert.Equal("formNo,status,createdAt,patientName,dateOfBirth,citizenId,phoneNumber,decision,confirmedBy", job.Columns);
        var audit = Assert.Single(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormsExported);
        Assert.Equal(job.Id.ToString(), audit.EntityId);
        Assert.Contains("\"rows\":3", audit.Details, StringComparison.Ordinal);
        Assert.Contains("\"sensitive\":false", audit.Details, StringComparison.Ordinal);
        Assert.Contains(writer.Properties, p => p.Key == "Mã lần xuất" && p.Value == job.Id.ToString());
        Assert.Contains(writer.Properties, p => p.Key == "Dữ liệu nhạy cảm" && p.Value.StartsWith("Đã che", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SensitiveData_NeedsThePermission_AndIsThenExportedInFull()
    {
        var (s, _, _, _) = await Scenario();
        LogInAsReporter(s);
        var service = s.Ctx.CreateConsentFormExportService();

        var forbidden = await service.ExportAsync(Request(sensitive: true), CancellationToken.None);

        Assert.Equal(ErrorType.Forbidden, forbidden.Error!.Type);
        Assert.Equal("exports.sensitive_forbidden", forbidden.Error.Code);
        Assert.Empty(s.Ctx.Store.ExportJobs);

        s.Ctx.CurrentUser.Permissions.Add(Permissions.ExportsSensitive);
        var full = await service.ExportAsync(
            Request(sensitive: true, columns: ["formNo", "citizenId", "representativeCitizenId"]), CancellationToken.None);

        Assert.True(full.IsSuccess);
        var writer = s.Ctx.XlsxWriter;
        Assert.All(writer.Rows.Select((_, i) => writer.Cell(i, "Số CCCD")), cccd => Assert.Equal("036188000125", cccd));
        // Chỉ phiếu TN-01 (người đại diện ký) có người đại diện.
        Assert.Equal("036085000777", writer.Cell(2, "Số CCCD người đại diện"));
        Assert.Null(writer.Cell(0, "Số CCCD người đại diện"));
        Assert.True(Assert.Single(s.Ctx.Store.ExportJobs).IncludesSensitiveData);
        Assert.Contains(writer.Properties, p => p.Key == "Dữ liệu nhạy cảm" && p.Value.StartsWith("Đầy đủ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DepartmentStaff_ExportOnlyTheirScope_AndCannotAskForAnotherDepartment()
    {
        var (s, completed, draft, _) = await Scenario();
        s.LogInAs(s.Kkb, SystemRoles.DepartmentHead, Permissions.ExportsRun);
        var service = s.Ctx.CreateConsentFormExportService();

        var own = await service.ExportAsync(Request(), CancellationToken.None);
        var other = await service.ExportAsync(Request(new ConsentFormExportFilter(DepartmentId: s.Noi.Id)), CancellationToken.None);

        Assert.True(own.IsSuccess);
        var formNos = s.Ctx.XlsxWriter.Rows.Select((_, i) => s.Ctx.XlsxWriter.Cell(i, "Số phiếu")).ToList();
        Assert.Equal(
            [s.Ctx.Store.ConsentForms.Single(f => f.Id == completed).FormNo, s.Ctx.Store.ConsentForms.Single(f => f.Id == draft).FormNo],
            formNos);
        Assert.Contains(s.Ctx.XlsxWriter.Properties, p => p.Key == "Phạm vi dữ liệu" && p.Value.StartsWith("Khoa của người xuất", StringComparison.Ordinal));
        Assert.Equal("exports.department_forbidden", other.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, other.Error.Type);
    }

    [Fact]
    public async Task Filters_ByStatusTemplateAndDate_AreApplied_AndAnEmptyResultIsRejected()
    {
        var (s, completed, _, tn01) = await Scenario();
        LogInAsReporter(s);
        var service = s.Ctx.CreateConsentFormExportService();
        var today = DateOnly.FromDateTime(s.Ctx.Clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);
        string FormNo(Guid id) => s.Ctx.Store.ConsentForms.Single(f => f.Id == id).FormNo;
        List<object?> Exported() => [.. s.Ctx.CsvWriter.Rows.Select((_, i) => s.Ctx.CsvWriter.Cell(i, "Số phiếu"))];

        await service.ExportAsync(Request(new(Status: ConsentFormStatus.Completed), format: ExportFormat.Csv), CancellationToken.None);
        Assert.Equal([FormNo(completed)], Exported());

        await service.ExportAsync(Request(new(TemplateId: s.Tn01.Id, From: today, To: today), format: ExportFormat.Csv), CancellationToken.None);
        Assert.Equal([FormNo(tn01)], Exported());

        var none = await service.ExportAsync(Request(new(From: today.AddDays(1))), CancellationToken.None);
        Assert.Equal(ErrorType.BusinessRule, none.Error!.Type);
        Assert.Equal("exports.no_rows", none.Error.Code);
        Assert.Equal(2, s.Ctx.Store.ExportJobs.Count);
        Assert.All(s.Ctx.Store.ExportJobs, j => Assert.Equal(ExportFormat.Csv, j.Format));
        Assert.Contains("\"templateCode\":\"TN-01\"", s.Ctx.Store.ExportJobs[1].FiltersJson, StringComparison.Ordinal);
    }

    public static TheoryData<ConsentFormExportRequest, string> InvalidRequests() => new()
    {
        { new(null, null, ["formNo"]), "format" },
        { new(ExportFormat.Xlsx, null, []), "columns" },
        { new(ExportFormat.Xlsx, null, ["formNo", "khongCo"]), "columns" },
        { new(ExportFormat.Xlsx, new(From: new DateOnly(2026, 11, 2), To: new DateOnly(2026, 11, 1)), ["formNo"]), "filter.to" },
        { new(ExportFormat.Xlsx, null, ["formNo"], IncludeContent: true), "includeContent" },
        { new((ExportFormat)9, null, ["formNo"]), "format" },
    };

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task InvalidRequests_AreRejectedWithFieldErrors(ConsentFormExportRequest request, string field)
    {
        var (s, _, _, _) = await Scenario();
        LogInAsReporter(s);

        var result = await s.Ctx.CreateConsentFormExportService().ExportAsync(request, CancellationToken.None);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Contains(field, result.Error.FieldErrors!.Keys);
        Assert.Empty(s.Ctx.Store.ExportJobs);
    }

    [Fact]
    public async Task Content_AddsTheTemplateFields_WithOptionLabelsAndScores()
    {
        var (s, _, _, _) = await Scenario();
        LogInAsReporter(s);

        var result = await s.Ctx.CreateConsentFormExportService().ExportAsync(
            Request(new(TemplateId: s.Tn01.Id), columns: ["formNo"], content: true), CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var writer = s.Ctx.XlsxWriter;
        Assert.Contains("Tổng điểm Morse (0–125)", writer.Headers);
        Assert.Equal("Lần đầu (tiếp nhận/nhập viện)", writer.Cell(0, "Thời điểm đánh giá"));
        Assert.Equal("B12", writer.Cell(0, "Buồng/giường"));
        Assert.Equal(40, writer.Cell(0, "Tổng điểm Morse (0–125)"));
        Assert.Equal("Nguy cơ trung bình", writer.Cell(0, "Tổng điểm Morse (0–125) – Mức"));
        Assert.Equal(
            "Hướng dẫn cách gọi nhân viên y tế (chuông gọi); Giường ở vị trí thấp, khóa bánh xe; vật dụng cá nhân trong tầm với",
            writer.Cell(0, "Biện pháp chuẩn (mọi người bệnh)"));
        Assert.Equal(new DateTime(2026, 11, 12, 8, 0, 0), writer.Cell(0, "Hẹn đánh giá lại"));
        // Trường ẩn (nguy cơ cao) để trống.
        Assert.Null(writer.Cell(0, "Nguy cơ cao (thêm)"));
        Assert.True(Assert.Single(s.Ctx.Store.ExportJobs).IncludesContent);
    }

    [Fact]
    public async Task Content_MasksPrefilledInsuranceNumbers_UnlessSensitiveDataIsExported()
    {
        var (s, _, _, _) = await Scenario();
        LogInAsReporter(s, Permissions.ExportsSensitive);
        var service = s.Ctx.CreateConsentFormExportService();
        var request = Request(new(Status: ConsentFormStatus.Completed, TemplateId: s.Ck01.Id), columns: ["formNo"], content: true);

        await service.ExportAsync(request, CancellationToken.None);
        var masked = s.Ctx.XlsxWriter.Cell(0, "Số thẻ BHYT");
        Assert.Equal("Khám, chữa bệnh BHYT", s.Ctx.XlsxWriter.Cell(0, "Hình thức khám, chữa bệnh"));
        Assert.Equal("Đã xác nhận", s.Ctx.XlsxWriter.Cell(0, "Tôi đã được giải thích, đọc và hiểu nội dung cam kết trên."));
        Assert.Equal(new DateOnly(2026, 1, 1), s.Ctx.XlsxWriter.Cell(0, "Thẻ có giá trị từ ngày"));

        await service.ExportAsync(request with { IncludeSensitiveData = true }, CancellationToken.None);
        var full = s.Ctx.XlsxWriter.Cell(0, "Số thẻ BHYT");

        Assert.Equal("GD**********125", masked);
        Assert.Equal("GD4790000000125", full);
    }

    [Fact]
    public async Task SubmittedForms_UseThePatientDetailsFrozenAtSubmission()
    {
        var (s, completed, draft, _) = await Scenario();
        // Hồ sơ người bệnh được sửa sau khi phiếu đã ký.
        var p = s.Patient;
        s.Patient.Update(
            new PatientDetails(
                "Phạm Thị Dung Mới (giả lập)", p.DateOfBirth, p.Gender, p.CitizenId, p.OldIdNumber, p.CitizenIdIssueDate,
                p.PermanentAddress, p.CurrentAddress, p.PhoneNumber, p.Ethnicity, p.Occupation, p.Nationality, p.HealthInsuranceNumber,
                p.PatientCode),
            s.Ctx.Clock.UtcNow);
        LogInAsReporter(s);

        await s.Ctx.CreateConsentFormExportService().ExportAsync(Request(columns: ["formNo", "patientName"]), CancellationToken.None);

        var writer = s.Ctx.XlsxWriter;
        Assert.Equal("Phạm Thị Dung (giả lập)", writer.Cell(0, "Họ tên người bệnh"));
        Assert.Equal("Phạm Thị Dung Mới (giả lập)", writer.Cell(1, "Họ tên người bệnh"));
        Assert.Equal(s.Ctx.Store.ConsentForms.Single(f => f.Id == completed).FormNo, writer.Cell(0, "Số phiếu"));
        Assert.Equal(s.Ctx.Store.ConsentForms.Single(f => f.Id == draft).FormNo, writer.Cell(1, "Số phiếu"));
    }

    [Fact]
    public async Task History_ShowsOwnExports_AndEveryExportToAuditors()
    {
        var (s, _, _, _) = await Scenario();
        LogInAsReporter(s);
        await s.Ctx.CreateConsentFormExportService().ExportAsync(Request(), CancellationToken.None);
        var reporter = s.Ctx.CurrentUser.UserId;
        s.LogInAs(s.Kkb, SystemRoles.DepartmentHead, Permissions.ExportsRun);
        await s.Ctx.CreateConsentFormExportService().ExportAsync(Request(format: ExportFormat.Csv), CancellationToken.None);

        var own = await s.Ctx.CreateConsentFormExportService().HistoryAsync(new(), CancellationToken.None);
        s.Ctx.CurrentUser.Permissions.Add(Permissions.AuditRead);
        var all = await s.Ctx.CreateConsentFormExportService().HistoryAsync(new(), CancellationToken.None);

        var mine = Assert.Single(own.Items);
        Assert.Equal(ExportFormat.Csv, mine.Format);
        Assert.Equal(2, mine.RowCount);
        Assert.Equal(SomeColumns.Length, mine.Columns.Count);
        Assert.Equal(2, all.TotalCount);
        Assert.Contains(all.Items, j => j.RequestedByUserId == reporter);
    }

    [Fact]
    public void ColumnCatalog_HasUniqueKeys_AndMarksSensitiveColumns()
    {
        var columns = ConsentFormExportService.Columns();

        Assert.Equal(columns.Count, columns.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            ["citizenId", "phoneNumber", "representativeCitizenId"],
            columns.Where(c => c.Sensitive).Select(c => c.Key).ToArray());
        Assert.DoesNotContain(columns, c => c.Sensitive && c.Default);
        Assert.Contains(columns, c => c is { Key: "formNo", Default: true, Header: "Số phiếu" });
    }
}
