using Consent.Application.NursingReports;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Authorization;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.NursingReports;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Dashboard điều dưỡng của Phòng Điều dưỡng (quyết định 47). Dữ liệu giả lập.</summary>
public sealed class NursingDashboardTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static readonly Guid Ward = Guid.NewGuid();
    private static readonly Guid Surgery = Guid.NewGuid();
    private static readonly Guid Icu = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Unused = Guid.NewGuid();

    /// <summary>
    /// Ba khoa phải báo cáo (HSTC chưa cài số giường), một khoa không trong danh sách và một khoa không trong danh sách, không gửi
    /// báo cáo nào. Thứ tự Phòng Điều dưỡng đặt khác thứ tự tên.
    /// </summary>
    private static readonly NursingSummaryDepartment[] Departments =
    [
        new(Other, "KHAC", "Khoa Khác (giả lập)", IsReporting: false, BedCount: null),
        new(Icu, "HSTC", "Khoa Hồi sức (giả lập)", IsReporting: true, BedCount: null, SortOrder: 3),
        new(Surgery, "NGOAI", "Khoa Ngoại (giả lập)", IsReporting: true, BedCount: 20, SortOrder: 2),
        new(Unused, "AAA", "Khoa Không dùng (giả lập)", IsReporting: false, BedCount: null),
        new(Ward, "NOI A", "Khoa Nội A (giả lập)", IsReporting: true, BedCount: 40, SortOrder: 1),
    ];

    private static Dictionary<string, string> Checks(params string[] issues) =>
        NursingReportForm.Checks.ToDictionary(
            c => c.Key, c => issues.Contains(c.Key) ? NursingReportForm.CheckIssue : NursingReportForm.CheckOk, StringComparer.Ordinal);

    private static NursingSubmittedFigures Report(
        Guid departmentId, DateOnly date, int form, Dictionary<string, int> values, string[]? issues = null, bool notes = false) =>
        new(departmentId, date, new NursingReportFigures(values, Checks(issues ?? [])) { Version = form }.ToJson(), notes);

    private static readonly DateOnly First = new(2026, 11, 8);
    private static readonly DateOnly Last = new(2026, 11, 10);

    private static readonly NursingSubmittedFigures[] Reports =
    [
        // Trước kỳ: không tính.
        Report(Ward, new(2026, 11, 7), 3, new() { ["previousDayPatients"] = 99, ["currentPatients"] = 99 }, ["professional"]),
        Report(Ward, First, 3, new()
        {
            ["previousDayPatients"] = 30, ["currentPatients"] = 32, ["admissionsFromClinic"] = 3, ["admissionsByTransfer"] = 1,
            ["dischargesYesterday"] = 2, ["transfersOutYesterday"] = 0, ["careLevel1"] = 4, ["ventilated"] = 1,
        }),
        Report(Other, First, 3, new() { ["previousDayPatients"] = 5, ["currentPatients"] = 5 }, ["professional"], notes: true),
        // Ngày 09/11 không khoa nào gửi.
        Report(Ward, Last, 3, new()
        {
            ["previousDayPatients"] = 32, ["currentPatients"] = 30, ["admissionsFromClinic"] = 1, ["dischargesYesterday"] = 3,
            ["careLevel1"] = 2, ["monitored"] = 3, ["noBed"] = 1, ["adr"] = 1,
        }, ["drugAdministration"]),
        // Mẫu 2 (trước quyết định 46): chưa có Số NB ngày hôm trước.
        Report(Surgery, Last, 2, new()
        {
            ["currentPatients"] = 22, ["admissionsFromClinic"] = 2, ["admissionsByTransfer"] = 0, ["dischargesYesterday"] = 1,
            ["transfersOutYesterday"] = 1, ["ventilated"] = 2,
        }, ["speakers", "professional"], notes: true),
        // Mẫu 1 trước quyết định 38: chỉ có số tổng nhập viện cũ.
        Report(Icu, Last, 1, new() { [NursingReportForm.LegacyAdmissionsKey] = 4, ["currentPatients"] = 10 }),
    ];

    private static Dictionary<string, int?> Expected(params (string Key, int? Value)[] values)
    {
        var expected = NursingDashboardBuilder.ValueKeys.ToDictionary(k => k, _ => (int?)0, StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            expected[key] = value;
        }

        return expected;
    }

    [Fact]
    public void Days_SumSubmittedReports_LikeTheSummary_AndLeaveDaysWithoutReportsEmpty()
    {
        var dashboard = NursingDashboardBuilder.Build(First, Last, Departments, Reports);

        Assert.Equal(First, dashboard.From);
        Assert.Equal(Last, dashboard.To);
        Assert.Equal(3, dashboard.ReportingCount);
        Assert.Equal([First, new(2026, 11, 9), Last], dashboard.Days.Select(d => d.Date));

        var first = dashboard.Days[0];
        Assert.Equal(2, first.Submitted);
        Assert.Equal(
            Expected(("previousDayPatients", 35), ("currentPatients", 37), ("admissions", 4), ("admissionsFromClinic", 3),
                ("admissionsByTransfer", 1), ("dischargesYesterday", 2), ("careLevel1", 4), ("ventilated", 1)),
            first.Values);
        Assert.Equal(2, first.PatientChange);
        // Khoa Khác chưa cài số giường: không tính vào công suất.
        Assert.Equal(40, first.Beds);
        Assert.Equal(32, first.BedPatients);
        Assert.Equal(1, first.IssueDepartments);
        Assert.Equal(1, first.Incidents);

        var empty = dashboard.Days[1];
        Assert.Equal(0, empty.Submitted);
        Assert.All(empty.Values.Values, Assert.Null);
        Assert.Equal(NursingDashboardBuilder.ValueKeys, empty.Values.Keys);
        Assert.Null(empty.PatientChange);
        Assert.Null(empty.Beds);
        Assert.Null(empty.BedPatients);
        Assert.Equal(0, empty.IssueDepartments);
        Assert.Equal(0, empty.Incidents);

        var last = dashboard.Days[2];
        Assert.Equal(3, last.Submitted);
        // Số NB ngày hôm trước chỉ có ở báo cáo mẫu 3; nhập khoa của báo cáo cũ lấy số tổng cũ; chuyển khoa có từ mẫu 2.
        Assert.Equal(
            Expected(("previousDayPatients", 32), ("currentPatients", 62), ("admissions", 7), ("admissionsFromClinic", 3),
                ("admissionsByTransfer", 0), ("dischargesYesterday", 4), ("transfersOutYesterday", 1), ("careLevel1", 2),
                ("ventilated", 2), ("monitored", 3), ("noBed", 1), ("adr", 1)),
            last.Values);
        Assert.Equal(-2, last.PatientChange);
        Assert.Equal(60, last.Beds);
        Assert.Equal(52, last.BedPatients);
        Assert.Equal(2, last.IssueDepartments);
        Assert.Equal(1, last.Incidents);
    }

    [Fact]
    public void Departments_FollowTheSummaryOrder_WithLastDayFigures_AndSubmittedDays()
    {
        var dashboard = NursingDashboardBuilder.Build(First, Last, Departments, Reports);

        // Khoa phải báo cáo theo thứ tự đã đặt, rồi khoa đã gửi trong kỳ dù không trong danh sách; khoa không gửi gì thì không có.
        Assert.Equal(["NOI A", "NGOAI", "HSTC", "KHAC"], dashboard.Departments.Select(d => d.Code));

        var ward = dashboard.Departments[0];
        Assert.Equal(Ward, ward.DepartmentId);
        Assert.Equal("Khoa Nội A (giả lập)", ward.Name);
        Assert.True(ward.IsReporting);
        Assert.Equal(40, ward.BedCount);
        Assert.Equal(2, ward.SubmittedDays);
        Assert.True(ward.Submitted);
        Assert.Equal(
            Expected(("previousDayPatients", 32), ("currentPatients", 30), ("admissions", 1), ("admissionsFromClinic", 1),
                ("dischargesYesterday", 3), ("careLevel1", 2), ("monitored", 3), ("noBed", 1), ("adr", 1)),
            ward.Values);
        Assert.Equal(["drugAdministration"], ward.Issues);
        Assert.False(ward.HasIncident);

        var surgery = dashboard.Departments[1];
        Assert.Equal(1, surgery.SubmittedDays);
        Assert.Null(surgery.Values["previousDayPatients"]);
        Assert.Equal(22, surgery.Values["currentPatients"]);
        Assert.Equal(1, surgery.Values["transfersOutYesterday"]);
        // Theo thứ tự phần tick của mẫu.
        Assert.Equal(["professional", "speakers"], surgery.Issues);
        Assert.True(surgery.HasIncident);

        var icu = dashboard.Departments[2];
        Assert.Null(icu.BedCount);
        Assert.Equal(4, icu.Values["admissions"]);
        Assert.Null(icu.Values["admissionsFromClinic"]);
        Assert.Null(icu.Values["admissionsByTransfer"]);
        Assert.Null(icu.Values["transfersOutYesterday"]);
        Assert.Empty(icu.Issues);

        var other = dashboard.Departments[3];
        Assert.False(other.IsReporting);
        Assert.Equal(1, other.SubmittedDays);
        Assert.False(other.Submitted);
        Assert.Empty(other.Values);
        Assert.Empty(other.Issues);
        Assert.False(other.HasIncident);
    }

    [Fact]
    public void Checks_CountIssuesInThePeriod_IncludingDrugAdministration()
    {
        var dashboard = NursingDashboardBuilder.Build(First, Last, Departments, Reports);

        Assert.Equal(NursingReportForm.Checks.Select(c => c.Key), dashboard.Checks.Select(c => c.Key));
        Assert.Equal(NursingReportForm.Checks.Select(c => c.Label), dashboard.Checks.Select(c => c.Label));
        var byKey = dashboard.Checks.ToDictionary(c => c.Key);
        // Báo cáo trước kỳ không tính.
        Assert.Equal(2, byKey["professional"].IssueCount);
        Assert.Equal(["NGOAI"], byKey["professional"].LastDayDepartments);
        Assert.Equal(1, byKey["drugAdministration"].IssueCount);
        Assert.Equal(["NOI A"], byKey["drugAdministration"].LastDayDepartments);
        Assert.Equal(1, byKey["speakers"].IssueCount);
        Assert.Equal(0, byKey["security"].IssueCount);
        Assert.Empty(byKey["security"].LastDayDepartments);
    }

    private sealed class Fixture
    {
        public AppTestContext Ctx { get; } = new();

        public Department Ward { get; }

        public Department Surgery { get; }

        public User Nurse { get; }

        public User SurgeryNurse { get; }

        public Fixture()
        {
            Ward = Ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
            Surgery = Ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
            Ctx.AddDepartment("KKB", "Khoa Khám bệnh (giả lập)");
            Nurse = Ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: Ward.Id);
            SurgeryNurse = Ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: Surgery.Id);
            Ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(Surgery.Id, 25, Ctx.Clock.UtcNow, sortOrder: 1));
            Ctx.Store.NursingDepartments.Add(NursingReportDepartment.Create(Ward.Id, 48, Ctx.Clock.UtcNow, sortOrder: 2));
        }

        public async Task SaveAsync(User nurse, Department department, DateOnly date, Dictionary<string, int?> values, bool submit)
        {
            Ctx.CurrentUser.UserId = nurse.Id;
            Ctx.CurrentUser.UserName = nurse.UserName;
            Ctx.CurrentUser.DepartmentId = nurse.DepartmentId;
            Ctx.CurrentUser.Permissions.Clear();
            Ctx.CurrentUser.Permissions.Add(Permissions.NursingReportsSubmit);
            var checks = NursingReportForm.Checks.ToDictionary(c => c.Key, _ => (string?)NursingReportForm.CheckOk);
            var result = await Ctx.CreateNursingReportService().SaveAsync(
                new SaveNursingReportRequest(department.Id, date, values, checks, null, submit), CancellationToken.None);
            Assert.True(result.IsSuccess, result.Error?.Message);
        }
    }

    [Fact]
    public async Task Service_ReadsSubmittedReportsOnly_EndingToday_ByDefault()
    {
        var fixture = new Fixture();
        await fixture.SaveAsync(fixture.Nurse, fixture.Ward, Today, new() { ["previousDayPatients"] = 40, ["admissionsFromClinic"] = 2 }, submit: true);
        await fixture.SaveAsync(fixture.Nurse, fixture.Ward, Today.AddDays(-7), new() { ["previousDayPatients"] = 70 }, submit: true);
        // Nháp không tính.
        await fixture.SaveAsync(fixture.SurgeryNurse, fixture.Surgery, Today, new() { ["previousDayPatients"] = 20 }, submit: false);

        var result = await fixture.Ctx.CreateNursingDashboardService().GetAsync(null, 7, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var dashboard = result.Value;
        Assert.Equal(Today, dashboard.To);
        Assert.Equal(Today.AddDays(-6), dashboard.From);
        Assert.Equal(7, dashboard.Days.Count);
        Assert.Equal(2, dashboard.ReportingCount);
        // Báo cáo 7 ngày trước nằm ngoài kỳ 7 ngày.
        Assert.Equal([0, 0, 0, 0, 0, 0, 1], dashboard.Days.Select(d => d.Submitted));
        Assert.Equal(42, dashboard.Days[^1].Values["currentPatients"]);
        Assert.Equal(48, dashboard.Days[^1].Beds);
        Assert.Equal(["NGOAIB", "NOIA"], dashboard.Departments.Select(d => d.Code));
        Assert.False(dashboard.Departments[0].Submitted);
        Assert.Equal(0, dashboard.Departments[0].SubmittedDays);
        Assert.True(dashboard.Departments[1].Submitted);

        var longer = await fixture.Ctx.CreateNursingDashboardService().GetAsync(Today, 8, CancellationToken.None);
        Assert.True(longer.IsSuccess, longer.Error?.Message);
        Assert.Equal(70, longer.Value.Days[0].Values["currentPatients"]);
        Assert.Equal(2, longer.Value.Departments[1].SubmittedDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(93)]
    public async Task Service_RejectsPeriodsOutsideOneTo92Days(int days)
    {
        var result = await new Fixture().Ctx.CreateNursingDashboardService().GetAsync(Today, days, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(["Chọn từ 1 đến 92 ngày."], result.Error.FieldErrors!["days"]);
    }

    [Fact]
    public async Task Service_RejectsDaysAfterToday_AndDatesTooEarlyForThePeriod()
    {
        var service = new Fixture().Ctx.CreateNursingDashboardService();

        var tomorrow = await service.GetAsync(Today.AddDays(1), 7, CancellationToken.None);
        Assert.False(tomorrow.IsSuccess);
        Assert.Equal(["Không xem được ngày sau hôm nay."], tomorrow.Error!.FieldErrors!["to"]);

        var tooEarly = await service.GetAsync(DateOnly.MinValue.AddDays(5), 7, CancellationToken.None);
        Assert.False(tooEarly.IsSuccess);
        Assert.Equal(["Ngày không hợp lệ."], tooEarly.Error!.FieldErrors!["to"]);

        var full = await service.GetAsync(Today, 92, CancellationToken.None);
        Assert.True(full.IsSuccess, full.Error?.Message);
        Assert.Equal(92, full.Value.Days.Count);
    }
}
