using Consent.Application.QualityControl;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Authorization;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.QualityControl;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Dashboard giám sát nội kiểm đường huyết (quyết định 59): tỷ lệ thực hiện tính trên mọi ngày trong tháng (hôm nay chỉ tính khi
/// đã có kết quả), không đạt và chạy lại, tiến độ xem xét, sổ tháng, cảnh báo lô và máy, xu hướng 6 tháng, phạm vi người xem.
/// Hôm nay là 11/11/2026 (giờ Việt Nam); kết quả ghi lúc 8 giờ sáng đúng ngày thực hiện, trừ khi test ghi muộn. Dữ liệu giả lập.
/// </summary>
public sealed class QualityControlDashboardTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateOnly Expiry = new(2027, 6, 30);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    private readonly AppTestContext _ctx = new();
    private readonly Department _wardA;
    private readonly Department _wardB;
    private readonly Department _lab;
    private readonly User _nurseA;
    private readonly User _nurseB;
    private readonly User _labStaff;
    private QcLotDto _strip = null!;
    private QcLotDto _level1 = null!;
    private QcLotDto _level2 = null!;

    public QualityControlDashboardTests()
    {
        _wardA = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _wardB = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        _lab = _ctx.AddDepartment("HSH", "Khoa Huyết học – Sinh hóa (giả lập)");
        _nurseA = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _wardA.Id);
        _nurseB = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _wardB.Id);
        _labStaff = _ctx.AddUser("ktv.hsh", SystemRoles.LabQualityControl, departmentId: _lab.Id);
        _ctx.Clock.UtcNow = Now;
    }

    private QcCatalogService Catalog => _ctx.CreateQcCatalogService();

    private QcBookService Books => _ctx.CreateQcBookService();

    private QcDashboardService Dashboard => _ctx.CreateQcDashboardService();

    private static DateOnly Nov(int day) => new(2026, 11, day);

    private void ActAs(User user)
    {
        _ctx.CurrentUser.UserId = user.Id;
        _ctx.CurrentUser.UserName = user.UserName;
        _ctx.CurrentUser.DepartmentId = user.DepartmentId;
        _ctx.CurrentUser.Permissions.Clear();
        foreach (var permission in _ctx.Store.Roles.Where(r => user.RoleIds.Contains(r.Id)).SelectMany(r => r.Permissions).Select(p => p.PermissionCode))
        {
            _ctx.CurrentUser.Permissions.Add(permission);
        }
    }

    /// <summary>Đồng hồ ở 8 giờ sáng (giờ Việt Nam) của ngày <paramref name="date"/>.</summary>
    private void At(DateOnly date) => _ctx.Clock.UtcNow = new DateTimeOffset(date.ToDateTime(new TimeOnly(1, 0)), TimeSpan.Zero);

    private static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static void Fails<T>(Result<T> result, ErrorType type)
    {
        Assert.True(result.IsFailure, "Lẽ ra phải lỗi.");
        Assert.Equal(type, result.Error!.Type);
    }

    /// <summary>Khoa HSH khai báo (ngày <paramref name="declaredOn"/>) một máy của khoa Nội A và ba lô dùng chung.</summary>
    private async Task<QcMeterDto> CatalogAsync(DateOnly declaredOn)
    {
        At(declaredOn);
        ActAs(_labStaff);
        var meter = Ok(await Catalog.CreateMeterAsync(new SaveQcMeterRequest(_wardA.Id, "Máy đo (giả lập)", "GL-0001", null), CancellationToken.None));
        _strip = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử A (giả lập)", "QT-01", Expiry, null, null, null, null, null), CancellationToken.None));
        _level1 = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng A", "DC-01", Expiry, 1, 40, 70, QcUnit.MgPerDl, null), CancellationToken.None));
        _level2 = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng A", "DC-02", Expiry, 2, 100, 140, QcUnit.MgPerDl, null), CancellationToken.None));
        _ctx.Clock.UtcNow = Now;
        return meter;
    }

    private async Task<QcMeterDto> MeterAsync(Guid departmentId, string serialNumber, DateOnly declaredOn)
    {
        At(declaredOn);
        ActAs(_labStaff);
        var meter = Ok(await Catalog.CreateMeterAsync(new SaveQcMeterRequest(departmentId, "Máy đo (giả lập)", serialNumber, null), CancellationToken.None));
        _ctx.Clock.UtcNow = Now;
        return meter;
    }

    private async Task<QcBookDto> OpenAsync(QcMeterDto meter, string month) =>
        Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, month), CancellationToken.None));

    /// <summary>
    /// Người đang đăng nhập ghi kết quả ngày <paramref name="date"/> (mức 1: 40–70, mức 2: 100–140 mg/dL) lúc 8 giờ sáng ngày
    /// <paramref name="recordedOn"/> (mặc định đúng ngày thực hiện); trả về kết quả vừa ghi.
    /// </summary>
    private async Task<QcRunDto> RecordAsync(
        Guid bookId, DateOnly date, decimal value1 = 55, decimal value2 = 120, DateOnly? recordedOn = null, Guid? level1LotId = null)
    {
        var opened = new DateOnly(date.Year, date.Month, 1);
        At(recordedOn ?? date);
        try
        {
            var book = Ok(await Books.RecordRunAsync(
                bookId,
                new QcRunRequest(date, _strip.Id, opened, level1LotId ?? _level1.Id, opened, value1, _level2.Id, opened, value2),
                CancellationToken.None));
            return book.Days.Single(d => d.Date == date).Run!;
        }
        finally
        {
            _ctx.Clock.UtcNow = Now;
        }
    }

    private async Task<QcDashboardDto> GetAsync(string? month = null) => Ok(await Dashboard.GetAsync(month, CancellationToken.None));

    [Fact]
    public async Task Compliance_CountsEveryDayOfTheMonth_TodayOnlyOnceRecorded_WithLateEntries()
    {
        var meter = await CatalogAsync(new DateOnly(2026, 10, 1));
        ActAs(_nurseA);
        var book = await OpenAsync(meter, "2026-11");
        foreach (var day in new[] { 1, 2, 4, 6, 7, 8, 9 })
        {
            await RecordAsync(book.Id, Nov(day));
        }

        await RecordAsync(book.Id, Nov(3), recordedOn: Nov(4));

        // Đến hôm qua (ngày 10): 10 ngày phải thực hiện, chưa ghi ngày 5 và ngày 10; hôm nay chưa ghi thì chưa tính.
        ActAs(_labStaff);
        var dashboard = await GetAsync();
        Assert.Equal(("2026-11", Today), (dashboard.Month, dashboard.Today));
        var department = Assert.Single(dashboard.Departments);
        Assert.Equal((_wardA.Id, "Khoa Nội A (giả lập)"), (department.DepartmentId, department.DepartmentName));
        var row = Assert.Single(department.Meters);
        Assert.Equal((10, 8, 2, 1), (row.Figures.RequiredDays, row.Figures.RecordedDays, row.Figures.MissingDays, row.Figures.LateDays));
        Assert.Equal([Nov(5), Nov(10)], row.MissingDates);
        Assert.Equal((book.Id, (QcBookStatus?)QcBookStatus.Open, false, true, true), (row.BookId, row.Status, row.Returned, row.CanOpen, row.MeterActive));
        Assert.Equal("Máy đo (giả lập) GL-0001", row.MeterLabel);
        Assert.Equal(row.Figures, department.Figures);
        Assert.Equal(row.Figures, dashboard.Totals.Figures);
        Assert.Equal((1, 1), (dashboard.Totals.Departments, dashboard.Totals.Meters));

        // Hôm nay đã ghi thì tính cả hôm nay.
        ActAs(_nurseA);
        await RecordAsync(book.Id, Today);
        ActAs(_labStaff);
        var after = Assert.Single(Assert.Single((await GetAsync("2026-11")).Departments).Meters);
        Assert.Equal((11, 9, 2), (after.Figures.RequiredDays, after.Figures.RecordedDays, after.Figures.MissingDays));
        Assert.Equal([Nov(5), Nov(10)], after.MissingDates);
    }

    [Fact]
    public async Task Failures_AreSplitByRepeat_AndTheLatestCauseIsShown()
    {
        var meter = await CatalogAsync(new DateOnly(2026, 10, 1));
        ActAs(_nurseA);
        var book = await OpenAsync(meter, "2026-11");
        await RecordAsync(book.Id, Nov(1));
        var repeatPassed = await RecordAsync(book.Id, Nov(2), value1: 80);
        Ok(await Books.RecordFollowUpAsync(book.Id, repeatPassed.Id, new QcFollowUpRequest("Lọ dung dịch chứng để ngoài tủ mát", 60, 118), CancellationToken.None));
        var reasonOnly = await RecordAsync(book.Id, Nov(4), value1: 80);
        Ok(await Books.RecordFollowUpAsync(book.Id, reasonOnly.Id, new QcFollowUpRequest("Hết dung dịch chứng, chưa chạy lại", null, null), CancellationToken.None));
        var repeatFailed = await RecordAsync(book.Id, Nov(5), value2: 150);
        Ok(await Books.RecordFollowUpAsync(book.Id, repeatFailed.Id, new QcFollowUpRequest("Que thử ẩm", 75, 120), CancellationToken.None));
        await RecordAsync(book.Id, Nov(6), value1: 80);

        ActAs(_labStaff);
        var department = Assert.Single((await GetAsync()).Departments);
        var figures = department.Figures;
        Assert.Equal(
            (5, 4, 1, 1, 2),
            (figures.RecordedDays, figures.FailedDays, figures.RepeatPassedDays, figures.RepeatFailedDays, figures.NotRepeatedDays));

        // Lần không đạt gần nhất (ngày 6): khoa chưa ghi nguyên nhân.
        Assert.Equal(new QcDashboardFailureDto(Nov(6), "Máy đo (giả lập) GL-0001", null), department.LatestFailure);
        Assert.Equal(department.LatestFailure, Assert.Single(department.Meters).LatestFailure);

        ActAs(_nurseA);
        var latest = (await Books.GetAsync(book.Id, CancellationToken.None)).Value.Days.Single(d => d.Date == Nov(6)).Run!;
        Ok(await Books.RecordFollowUpAsync(book.Id, latest.Id, new QcFollowUpRequest("Máy báo lỗi que thử", null, null), CancellationToken.None));
        Assert.Equal("Máy báo lỗi que thử", Assert.Single((await GetAsync()).Departments).LatestFailure!.Reason);
    }

    [Fact]
    public async Task Reviews_BookStatuses_AndEarlierBooksNotYetConfirmed()
    {
        var meter = await CatalogAsync(new DateOnly(2026, 5, 1));
        ActAs(_nurseA);
        var november = await OpenAsync(meter, "2026-11");
        await RecordAsync(november.Id, Nov(1));
        await RecordAsync(november.Id, Nov(2));
        var october = await OpenAsync(meter, "2026-10");
        Ok(await Books.SubmitAsync(october.Id, CancellationToken.None));
        await OpenAsync(meter, "2026-09");
        var august = await OpenAsync(meter, "2026-08");
        Ok(await Books.SubmitAsync(august.Id, CancellationToken.None));
        var july = await OpenAsync(meter, "2026-07");
        Ok(await Books.SubmitAsync(july.Id, CancellationToken.None));

        // Quá 5 tháng trước tháng này: không báo nữa.
        await OpenAsync(meter, "2026-05");

        ActAs(_labStaff);
        Ok(await Books.ReturnAsync(august.Id, new QcReasonRequest("Thiếu xác nhận của khoa (giả lập)"), CancellationToken.None));
        var julyDays = Ok(await Books.GetAsync(july.Id, CancellationToken.None)).Days;
        Ok(await Books.ReviewDaysAsync(
            july.Id, new QcReviewDaysRequest([.. julyDays.Select(d => new QcReviewDayRequest(d.Date, d.SuggestedOutcome, null))]), CancellationToken.None));
        Ok(await Books.ConfirmAsync(july.Id, CancellationToken.None));
        Ok(await Books.ReviewDaysAsync(
            november.Id,
            new QcReviewDaysRequest([new QcReviewDayRequest(Nov(1), QcReviewOutcome.Passed, null), new QcReviewDayRequest(Nov(3), QcReviewOutcome.NoQc, null)]),
            CancellationToken.None));

        // Tháng này: đã xem xét 2 ngày, còn 9 ngày đến hôm nay chưa xem xét (như danh sách sổ).
        var dashboard = await GetAsync();
        var row = Assert.Single(Assert.Single(dashboard.Departments).Meters);
        Assert.Equal((2, 9), (row.Figures.ReviewedDays, row.Figures.DaysToReview));
        Assert.Equal(new QcDashboardBookCountsDto(1, 0, 0, 0, 0), dashboard.Totals.Books);
        Assert.Equal("2026-11", dashboard.Alerts.CurrentMonth);
        Assert.Equal(
            [("2026-08", QcBookStatus.Open, true), ("2026-09", QcBookStatus.Open, false), ("2026-10", QcBookStatus.Submitted, false)],
            dashboard.Alerts.PendingBooks.Select(b => (b.Month, b.Status, b.Returned)));
        Assert.All(dashboard.Alerts.PendingBooks, b => Assert.Equal(("Khoa Nội A (giả lập)", true), (b.DepartmentName, b.CanOpen)));
        Assert.Equal(august.Id, dashboard.Alerts.PendingBooks[0].BookId);

        Assert.Equal(new QcDashboardBookCountsDto(0, 1, 0, 0, 0), (await GetAsync("2026-08")).Totals.Books);
        Assert.Equal(new QcDashboardBookCountsDto(0, 0, 1, 0, 0), (await GetAsync("2026-10")).Totals.Books);
        var confirmed = await GetAsync("2026-07");
        Assert.Equal(new QcDashboardBookCountsDto(0, 0, 0, 1, 0), confirmed.Totals.Books);
        Assert.Equal((31, 0), (confirmed.Totals.Figures.ReviewedDays, confirmed.Totals.Figures.DaysToReview));
    }

    [Fact]
    public async Task NewMovedAndRetiredMeters_CountOnlyTheDaysOfTheirDepartment()
    {
        var meterA = await CatalogAsync(new DateOnly(2026, 10, 1));
        var retired = await MeterAsync(_wardA.Id, "GL-0003", new DateOnly(2026, 10, 1));
        var moved = await MeterAsync(_wardA.Id, "GL-0004", new DateOnly(2026, 10, 1));
        var fresh = await MeterAsync(_wardB.Id, "GL-0002", Nov(6));

        ActAs(_nurseA);
        var retiredBook = await OpenAsync(retired, "2026-11");
        var movedBook = await OpenAsync(moved, "2026-11");
        for (var day = 1; day <= 4; day++)
        {
            await RecordAsync(retiredBook.Id, Nov(day));
            if (day <= 3)
            {
                await RecordAsync(movedBook.Id, Nov(day));
            }
        }

        ActAs(_labStaff);
        Ok(await Catalog.UpdateMeterAsync(retired.Id, new SaveQcMeterRequest(_wardA.Id, retired.Model, retired.SerialNumber, false), CancellationToken.None));
        Ok(await Catalog.UpdateMeterAsync(moved.Id, new SaveQcMeterRequest(_wardB.Id, moved.Model, moved.SerialNumber, true), CancellationToken.None));

        // Khoa Ngoại B chưa mở sổ máy mới và máy nhận từ khoa Nội A; khoa Nội A chưa mở sổ máy GL-0001.
        var before = await GetAsync();
        Assert.Equal(
            [(_wardB.Id, fresh.Id), (_wardB.Id, moved.Id), (_wardA.Id, meterA.Id)],
            before.Alerts.MetersWithoutBook.Select(m => (m.DepartmentId, m.MeterId)));

        // Máy khai báo ngày 6, chưa mở sổ: tính từ ngày 6 đến hôm qua.
        var wardB = before.Departments.Single(d => d.DepartmentId == _wardB.Id);
        var freshRow = Assert.Single(wardB.Meters);
        Assert.Equal((fresh.Id, (Guid?)null, 5, 5, false), (freshRow.MeterId, freshRow.BookId, freshRow.Figures.RequiredDays, freshRow.Figures.MissingDays, freshRow.CanOpen));
        Assert.Equal([Nov(6), Nov(7), Nov(8), Nov(9), Nov(10)], freshRow.MissingDates);
        Assert.Equal(new QcDashboardBookCountsDto(0, 0, 0, 0, 1), wardB.Books);

        // Máy ngưng sử dụng, máy chuyển đi: sổ của khoa Nội A chỉ tính đến kết quả cuối, không có ngày chưa ghi.
        var wardA = before.Departments.Single(d => d.DepartmentId == _wardA.Id);
        var retiredRow = wardA.Meters.Single(m => m.MeterId == retired.Id);
        Assert.Equal((4, 4, false), (retiredRow.Figures.RequiredDays, retiredRow.Figures.RecordedDays, retiredRow.MeterActive));
        var movedRow = wardA.Meters.Single(m => m.MeterId == moved.Id);
        Assert.Equal((3, 3, 0), (movedRow.Figures.RequiredDays, movedRow.Figures.RecordedDays, movedRow.Figures.MissingDays));
        Assert.Equal((10, 0), (wardA.Meters.Single(m => m.MeterId == meterA.Id).Figures.RequiredDays, wardA.Meters.Single(m => m.MeterId == meterA.Id).Figures.RecordedDays));

        // Khoa Ngoại B mở sổ, ghi từ ngày 6 cho máy nhận về; ghi bù ngày 2 cho máy mới (trước ngày khai báo, ghi muộn).
        ActAs(_nurseB);
        var movedIn = await OpenAsync(moved, "2026-11");
        for (var day = 6; day <= 10; day++)
        {
            await RecordAsync(movedIn.Id, Nov(day));
        }

        var freshBook = await OpenAsync(fresh, "2026-11");
        await RecordAsync(freshBook.Id, Nov(2), recordedOn: Today);

        ActAs(_labStaff);
        var after = await GetAsync();
        var wardBAfter = after.Departments.Single(d => d.DepartmentId == _wardB.Id);

        // Máy nhận về: tính từ ngày sau kết quả cuối của khoa Nội A (ngày 4).
        var movedInRow = wardBAfter.Meters.Single(m => m.MeterId == moved.Id);
        Assert.Equal((movedIn.Id, 7, 5), (movedInRow.BookId!.Value, movedInRow.Figures.RequiredDays, movedInRow.Figures.RecordedDays));
        Assert.Equal([Nov(4), Nov(5)], movedInRow.MissingDates);
        var freshAfter = wardBAfter.Meters.Single(m => m.MeterId == fresh.Id);
        Assert.Equal((9, 1, 1), (freshAfter.Figures.RequiredDays, freshAfter.Figures.RecordedDays, freshAfter.Figures.LateDays));
        Assert.Equal([(_wardA.Id, meterA.Id)], after.Alerts.MetersWithoutBook.Select(m => (m.DepartmentId, m.MeterId)));

        // Máy chuyển khoa có một dòng ở mỗi khoa, đếm là một máy.
        Assert.Equal((2, 4), (after.Totals.Departments, after.Totals.Meters));
        Assert.Equal(5, after.Departments.Sum(d => d.Meters.Count));
    }

    [Fact]
    public async Task Scope_DepartmentsSeeTheirOwn_MonitorsSeeAllWithoutOpeningBooks()
    {
        var meterA = await CatalogAsync(new DateOnly(2026, 10, 1));
        var meterB = await MeterAsync(_wardB.Id, "GL-0002", new DateOnly(2026, 10, 1));
        ActAs(_nurseA);
        var bookA = await OpenAsync(meterA, "2026-11");
        var october = await OpenAsync(meterA, "2026-10");

        var mine = await GetAsync();
        Assert.Equal([_wardA.Id], mine.Departments.Select(d => d.DepartmentId));
        Assert.True(Assert.Single(mine.Departments[0].Meters).CanOpen);
        Assert.Empty(mine.Alerts.MetersWithoutBook);
        Assert.Equal([october.Id], mine.Alerts.PendingBooks.Select(b => b.BookId));

        ActAs(_nurseB);
        var theirs = await GetAsync();
        Assert.Equal([_wardB.Id], theirs.Departments.Select(d => d.DepartmentId));
        Assert.Empty(theirs.Alerts.PendingBooks);
        Assert.Equal([meterB.Id], theirs.Alerts.MetersWithoutBook.Select(m => m.MeterId));
        Assert.Equal(1, theirs.Totals.Meters);

        // Không có quyền nội kiểm nào: không xem được.
        var leader = _ctx.AddUser("lanhdao", SystemRoles.Auditor, departmentId: _lab.Id);
        ActAs(leader);
        Fails(await Dashboard.GetAsync(null, CancellationToken.None), ErrorType.Forbidden);

        // Quyền chỉ xem dashboard: mọi khoa, không mở được sổ, không vào được danh sách sổ.
        _ctx.CurrentUser.Permissions.Add(Permissions.QcMonitor);
        var all = await GetAsync();
        Assert.Equal([_wardB.Id, _wardA.Id], all.Departments.Select(d => d.DepartmentId));
        Assert.All(all.Departments.SelectMany(d => d.Meters), m => Assert.False(m.CanOpen));
        Assert.False(Assert.Single(all.Alerts.PendingBooks).CanOpen);
        Fails(await Books.GetAsync(bookA.Id, CancellationToken.None), ErrorType.Forbidden);
        Fails(await Books.ListAsync(null, null, CancellationToken.None), ErrorType.Forbidden);

        // Khoa HSH và người ghi kết quả cho mọi khoa (scope.all_departments) mở được sổ mọi khoa.
        ActAs(_labStaff);
        Assert.True((await GetAsync()).Departments.Single(d => d.DepartmentId == _wardA.Id).Meters.Single().CanOpen);
        ActAs(_nurseB);
        _ctx.CurrentUser.Permissions.Add(Permissions.ScopeAllDepartments);
        var everywhere = await GetAsync();
        Assert.Equal(2, everywhere.Departments.Count);
        Assert.True(everywhere.Departments.Single(d => d.DepartmentId == _wardA.Id).Meters.Single().CanOpen);
    }

    [Fact]
    public async Task Month_NotInTheFuture_AtMostTwentyFourMonthsBack()
    {
        await CatalogAsync(new DateOnly(2026, 10, 1));
        ActAs(_labStaff);
        Fails(await Dashboard.GetAsync("2026-12", CancellationToken.None), ErrorType.Validation);
        Fails(await Dashboard.GetAsync("2024-10", CancellationToken.None), ErrorType.Validation);
        Fails(await Dashboard.GetAsync("11/2026", CancellationToken.None), ErrorType.Validation);

        // Tháng trước khi khai báo máy: không có số liệu.
        var oldest = await GetAsync("2024-11");
        Assert.Empty(oldest.Departments);
        Assert.Equal(0, oldest.Totals.Figures.RequiredDays);
        Assert.Equal(["2024-06", "2024-07", "2024-08", "2024-09", "2024-10", "2024-11"], oldest.Trend.Select(t => t.Month));
    }

    [Fact]
    public async Task Trend_ShowsSixMonths_OldestFirst()
    {
        var meter = await CatalogAsync(new DateOnly(2026, 9, 1));
        ActAs(_nurseA);
        var october = await OpenAsync(meter, "2026-10");
        for (var day = 1; day <= 20; day++)
        {
            await RecordAsync(october.Id, new DateOnly(2026, 10, day), value1: day <= 2 ? 80 : 55);
        }

        var november = await OpenAsync(meter, "2026-11");
        await RecordAsync(november.Id, Nov(1), recordedOn: Nov(3));

        // Tháng 6–8: chưa khai báo máy; tháng 9: chưa mở sổ (30 ngày chưa ghi); tháng 10: 20/31 ngày, 2 ngày không đạt.
        ActAs(_labStaff);
        Assert.Equal(
            [
                new QcDashboardMonthDto("2026-06", 0, 0, 0, 0),
                new QcDashboardMonthDto("2026-07", 0, 0, 0, 0),
                new QcDashboardMonthDto("2026-08", 0, 0, 0, 0),
                new QcDashboardMonthDto("2026-09", 30, 0, 0, 0),
                new QcDashboardMonthDto("2026-10", 31, 20, 2, 0),
                new QcDashboardMonthDto("2026-11", 10, 1, 0, 1),
            ],
            (await GetAsync()).Trend);
        Assert.Equal(["2026-05", "2026-06", "2026-07", "2026-08", "2026-09", "2026-10"], (await GetAsync("2026-10")).Trend.Select(t => t.Month));
    }

    [Fact]
    public async Task LotAlerts_ExpiredOrWithinThirtyDays_WithTheDepartmentsUsingThem_AndUsableLevels()
    {
        var meter = await CatalogAsync(new DateOnly(2026, 10, 1));
        At(new DateOnly(2026, 10, 1));
        ActAs(_labStaff);
        var soon = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng B", "DC-B1", Nov(20), 1, 40, 70, QcUnit.MgPerDl, null), CancellationToken.None));
        var expired = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng B", "DC-B3", Nov(5), 3, 200, 300, QcUnit.MgPerDl, null), CancellationToken.None));
        var boundary = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử B", "QT-B1", Today.AddDays(30), null, null, null, null, null), CancellationToken.None));
        Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử B", "QT-B2", Today.AddDays(31), null, null, null, null, null), CancellationToken.None));
        var stopped = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử C", "QT-C1", Nov(15), null, null, null, null, null), CancellationToken.None));
        Ok(await Catalog.UpdateLotAsync(
            stopped.Id, new SaveQcLotRequest(null, "Que thử C", "QT-C1", Nov(15), null, null, null, null, false), CancellationToken.None));
        _ctx.Clock.UtcNow = Now;

        // Khoa Nội A đang dùng lô DC-B1 cho mức 1.
        ActAs(_nurseA);
        var book = await OpenAsync(meter, "2026-11");
        await RecordAsync(book.Id, Nov(8));
        await RecordAsync(book.Id, Nov(9), level1LotId: soon.Id);

        ActAs(_labStaff);
        var alerts = (await GetAsync()).Alerts;
        Assert.Equal([(expired.Id, -6), (soon.Id, 9), (boundary.Id, 30)], alerts.Lots.Select(l => (l.LotId, l.DaysLeft)));
        Assert.Equal(
            (QcLotKind.ControlSolution, "DC-B3", (int?)3, Nov(5)),
            (alerts.Lots[0].Kind, alerts.Lots[0].LotNumber, alerts.Lots[0].Level, alerts.Lots[0].ExpiryDate));
        Assert.Empty(alerts.Lots[0].UsedBy);
        Assert.Equal(["Khoa Nội A (giả lập)"], alerts.Lots[1].UsedBy);
        Assert.Empty(alerts.Lots[2].UsedBy);

        // Còn hạn, đang dùng: dung dịch chứng mức 1 và 2 (lô mức 3 đã hết hạn), 3 lô que thử (lô ngưng sử dụng không tính).
        Assert.Equal([1, 2], alerts.ControlLevels);
        Assert.Equal(3, alerts.StripLots);

        // Khoa khác cũng thấy cảnh báo lô (lô dùng chung) nhưng không thấy khoa nào đang dùng ngoài khoa mình.
        ActAs(_nurseB);
        var other = (await GetAsync()).Alerts;
        Assert.Equal(3, other.Lots.Count);
        Assert.All(other.Lots, l => Assert.Empty(l.UsedBy));
    }
}
