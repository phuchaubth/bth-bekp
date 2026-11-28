using Consent.Application.QualityControl;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Authorization;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.QualityControl;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Sổ theo dõi kết quả nội kiểm đường huyết (quyết định 57): danh mục do Khoa HSH khai báo, khoa ghi kết quả của khoa mình, Khoa
/// HSH xem xét và xác nhận. Đồng hồ giả lập: 15:00 ngày 11/11/2026 giờ Việt Nam. Dữ liệu giả lập.
/// </summary>
public sealed class QualityControlServiceTests
{
    private static readonly DateOnly Today = new(2026, 11, 11);
    private static readonly DateOnly Expiry = new(2027, 6, 30);

    private readonly AppTestContext _ctx = new();
    private readonly Department _wardA;
    private readonly Department _wardB;
    private readonly Department _lab;
    private readonly User _nurseA;
    private readonly User _nurseB;
    private readonly User _labStaff;
    private readonly User _admin;

    public QualityControlServiceTests()
    {
        _wardA = _ctx.AddDepartment("NOIA", "Khoa Nội A (giả lập)");
        _wardB = _ctx.AddDepartment("NGOAIB", "Khoa Ngoại B (giả lập)");
        _lab = _ctx.AddDepartment("HSH", "Khoa Huyết học – Sinh hóa (giả lập)");
        _nurseA = _ctx.AddUser("dieuduong.noia", SystemRoles.Nurse, departmentId: _wardA.Id);
        _nurseB = _ctx.AddUser("dieuduong.ngoaib", SystemRoles.Nurse, departmentId: _wardB.Id);
        _labStaff = _ctx.AddUser("ktv.hsh", SystemRoles.LabQualityControl, departmentId: _lab.Id);
        _admin = _ctx.AddUser("quantri", SystemRoles.Administrator);
    }

    private QcCatalogService Catalog => _ctx.CreateQcCatalogService();

    private QcBookService Books => _ctx.CreateQcBookService();

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

    private static T Ok<T>(Result<T> result)
    {
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static void Fails<T>(Result<T> result, ErrorType type, string? messagePart = null)
    {
        Assert.True(result.IsFailure, "Lẽ ra phải lỗi.");
        Assert.Equal(type, result.Error!.Type);
        if (messagePart is not null)
        {
            Assert.Contains(messagePart, result.Error.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>Khoa HSH khai báo một máy của khoa Nội A và ba lô: que thử, dung dịch chứng mức 1 (40–70) và mức 2 (100–140 mg/dL).</summary>
    private async Task<(QcMeterDto Meter, QcLotDto Strip, QcLotDto Level1, QcLotDto Level2)> CatalogAsync()
    {
        ActAs(_labStaff);
        var meter = Ok(await Catalog.CreateMeterAsync(new SaveQcMeterRequest(_wardA.Id, "Máy đo A (giả lập)", "GL-0001", null), CancellationToken.None));
        var strip = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử A (giả lập)", "QT-01", Expiry, null, null, null, null, null), CancellationToken.None));
        var level1 = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng A", "DC-01", Expiry, 1, 40, 70, QcUnit.MgPerDl, null), CancellationToken.None));
        var level2 = Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng A", "DC-02", Expiry, 2, 100, 140, QcUnit.MgPerDl, null), CancellationToken.None));
        return (meter, strip, level1, level2);
    }

    private static QcRunRequest Run(DateOnly date, QcLotDto strip, QcLotDto level1, decimal value1, QcLotDto level2, decimal value2) =>
        new(date, strip.Id, new DateOnly(2026, 10, 1), level1.Id, new DateOnly(2026, 10, 1), value1, level2.Id, new DateOnly(2026, 10, 1), value2);

    [Fact]
    public async Task Catalog_IsDeclaredByTheLab_WithFieldErrors_AndNoDuplicates()
    {
        ActAs(_nurseA);
        Fails(await Catalog.GetAsync(CancellationToken.None), ErrorType.Forbidden);
        Fails(await Catalog.CreateMeterAsync(new SaveQcMeterRequest(_wardA.Id, "Máy", "S1", null), CancellationToken.None), ErrorType.Forbidden);

        var (meter, strip, level1, _) = await CatalogAsync();
        Assert.Equal((_wardA.Id, "Khoa Nội A (giả lập)", true), (meter.DepartmentId, meter.DepartmentName, meter.IsActive));

        var duplicate = await Catalog.CreateMeterAsync(new SaveQcMeterRequest(_wardB.Id, "Máy đo B", " GL-0001 ", null), CancellationToken.None);
        Fails(duplicate, ErrorType.Validation);
        Assert.Contains("serialNumber", duplicate.Error!.FieldErrors!.Keys);
        var inactive = _ctx.AddDepartment("CU", "Khoa cũ (giả lập)", active: false);
        var missing = await Catalog.CreateMeterAsync(new SaveQcMeterRequest(inactive.Id, " ", null, null), CancellationToken.None);
        Assert.Equal(["departmentId", "model", "serialNumber"], missing.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));

        var badControl = await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "DC", "DC-09", null, 4, 70, 40, null, null), CancellationToken.None);
        Assert.Equal(["expiryDate", "level", "rangeHigh", "unit"], badControl.Error!.FieldErrors!.Keys.Order(StringComparer.Ordinal));
        Fails(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.TestStrip, "Que thử", "qt-01", Expiry, null, null, null, null, null), CancellationToken.None), ErrorType.Validation);
        // Cùng số lô nhưng mức khác là lô khác.
        Ok(await Catalog.CreateLotAsync(
            new SaveQcLotRequest(QcLotKind.ControlSolution, "Dung dịch chứng A", "DC-01", Expiry, 2, 100, 140, QcUnit.MgPerDl, null), CancellationToken.None));

        Fails(await Catalog.UpdateLotAsync(
            strip.Id, new SaveQcLotRequest(QcLotKind.ControlSolution, "Que thử", "QT-01", Expiry, 1, 40, 70, QcUnit.MgPerDl, true), CancellationToken.None),
            ErrorType.Validation);
        var stopped = Ok(await Catalog.UpdateLotAsync(
            level1.Id, new SaveQcLotRequest(null, "Dung dịch chứng A", "DC-01", Expiry, 1, 45, 75, QcUnit.MgPerDl, false), CancellationToken.None));
        Assert.Equal((45m, false), (stopped.RangeLow!.Value, stopped.IsActive));

        var catalog = Ok(await Catalog.GetAsync(CancellationToken.None));
        Assert.Single(catalog.Meters);
        Assert.Equal(4, catalog.Lots.Count);
        Assert.Equal(QcLotKind.TestStrip, catalog.Lots[0].Kind);
    }

    [Fact]
    public async Task Books_AreScopedToTheDepartment_TheLabSeesAll()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        Ok(await Catalog.CreateMeterAsync(new SaveQcMeterRequest(_wardB.Id, "Máy đo B (giả lập)", "GL-0002", null), CancellationToken.None));

        ActAs(_nurseA);
        var mine = Ok(await Books.ListAsync(null, null, CancellationToken.None));
        Assert.Equal("2026-11", mine.Month);
        var row = Assert.Single(mine.Items);
        Assert.Equal((meter.Id, (Guid?)null, true), (row.MeterId, row.BookId, row.CanOpen));

        var book = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        Assert.Equal((30, true, false, false), (book.Days.Count, book.Actions.Record, book.Actions.Submit, book.Actions.Review));
        Assert.Equal(book.Id, Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None)).Id);
        Assert.Equal(3, book.AvailableLots.Count);
        Assert.Null(book.Defaults);
        Ok(await Books.RecordRunAsync(book.Id, Run(Today, strip, level1, 55, level2, 120), CancellationToken.None));

        // Khoa khác không thấy, không ghi được sổ này.
        ActAs(_nurseB);
        var other = Ok(await Books.ListAsync("2026-11", null, CancellationToken.None));
        Assert.Equal(["Máy đo B (giả lập) GL-0002"], other.Items.Select(i => i.MeterLabel));
        Fails(await Books.GetAsync(book.Id, CancellationToken.None), ErrorType.NotFound);
        Fails(await Books.RecordRunAsync(book.Id, Run(Today.AddDays(-1), strip, level1, 55, level2, 120), CancellationToken.None), ErrorType.NotFound);
        Fails(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None), ErrorType.NotFound);

        // Khoa HSH thấy mọi khoa, không ghi kết quả.
        ActAs(_labStaff);
        var all = Ok(await Books.ListAsync("2026-11", null, CancellationToken.None));
        Assert.Equal(["Khoa Ngoại B (giả lập)", "Khoa Nội A (giả lập)"], all.Items.Select(i => i.DepartmentName));
        var opened = all.Items.Single(i => i.BookId == book.Id);
        Assert.Equal((QcBookStatus.Open, 1, 0, 0, 11), (opened.Status!.Value, opened.RecordedDays, opened.FailedDays, opened.ReviewedDays, opened.DaysToReview));
        Assert.Equal([_wardB.Id], Ok(await Books.ListAsync("2026-11", _wardB.Id, CancellationToken.None)).Items.Select(i => i.DepartmentId));
        var asLab = Ok(await Books.GetAsync(book.Id, CancellationToken.None));
        Assert.Equal((false, true), (asLab.Actions.Record, asLab.Actions.Review));
        Assert.Empty(asLab.AvailableLots);
        Fails(await Books.RecordRunAsync(book.Id, Run(Today.AddDays(-1), strip, level1, 55, level2, 120), CancellationToken.None), ErrorType.Forbidden);

        ActAs(_nurseA);
        Fails(await Books.ReviewDaysAsync(book.Id, new QcReviewDaysRequest([new QcReviewDayRequest(Today, QcReviewOutcome.Passed, null)]), CancellationToken.None), ErrorType.Forbidden);
    }

    [Fact]
    public async Task OpeningAMonth_NotInTheFuture_AtMostTwelveMonthsBack()
    {
        var (meter, _, _, _) = await CatalogAsync();
        ActAs(_nurseA);

        Fails(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-12"), CancellationToken.None), ErrorType.Validation);
        Fails(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2025-10"), CancellationToken.None), ErrorType.Validation);
        Fails(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "11/2026"), CancellationToken.None), ErrorType.Validation);
        Fails(await Books.OpenAsync(new OpenQcBookRequest(null, "2026-11"), CancellationToken.None), ErrorType.Validation);
        var oldest = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2025-11"), CancellationToken.None));
        Assert.Equal((2025, 11), (oldest.Year, oldest.Month));
        Assert.False(Ok(await Books.ListAsync("2026-12", null, CancellationToken.None)).Items.Single().CanOpen);
    }

    [Fact]
    public async Task Recording_ReportsMissingFields_AndRuleViolations_AndSuggestsTheLotsInUse()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        ActAs(_nurseA);
        var book = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));

        var missing = await Books.RecordRunAsync(book.Id, new QcRunRequest(null, Guid.NewGuid(), null, null, null, null, null, null, null), CancellationToken.None);
        Fails(missing, ErrorType.Validation);
        Assert.Contains("stripLotId", missing.Error!.FieldErrors!.Keys);
        Assert.Contains("control2Value", missing.Error.FieldErrors.Keys);
        Fails(await Books.RecordRunAsync(book.Id, Run(Today.AddDays(1), strip, level1, 55, level2, 120), CancellationToken.None), ErrorType.BusinessRule, "Chưa tới ngày");
        Fails(await Books.RecordRunAsync(book.Id, Run(Today, strip, level2, 55, level1, 120), CancellationToken.None), ErrorType.BusinessRule, "mức thấp hơn ở cột đầu");

        var recorded = Ok(await Books.RecordRunAsync(book.Id, Run(Today, strip, level1, 80, level2, 120), CancellationToken.None));
        var day = recorded.Days.Single(d => d.Date == Today);
        var run = day.Run!;
        Assert.Equal((false, false, true, true, true), (run.Passed, run.Control1.Passed, run.Control2.Passed, run.CanFollowUp, run.CanCorrect));
        Assert.Equal(("dieuduong.noia Demo", false), (run.PerformedBy, run.Late));
        Assert.Equal((QcReviewOutcome.FailedNotRepeated, false), (day.SuggestedOutcome, day.CanRecord));
        Assert.Equal([QcReviewOutcome.FailedNotRepeated], day.AllowedOutcomes);
        Assert.Equal(new QcRunDefaultsDto(strip.Id, new DateOnly(2026, 10, 1), level1.Id, new DateOnly(2026, 10, 1), level2.Id, new DateOnly(2026, 10, 1)), recorded.Defaults);
        Assert.Equal(["QT-01"], recorded.Header.Strips.Select(s => s.LotNumber));
        Assert.Equal([(1, 40m, 70m)], recorded.Header.Controls1.Select(c => (c.Level!.Value, c.Low!.Value, c.High!.Value)));

        var followed = Ok(await Books.RecordFollowUpAsync(book.Id, run.Id, new QcFollowUpRequest("Lắc chưa đều lọ dung dịch chứng", 60, 118), CancellationToken.None));
        var repeated = followed.Days.Single(d => d.Date == Today).Run!;
        Assert.Equal((true, (bool?)true, false, QcReviewOutcome.Passed), (repeated.HasRepeat, repeated.RepeatPassed, repeated.CanFollowUp, followed.Days.Single(d => d.Date == Today).SuggestedOutcome));

        // Lô đã ngưng sử dụng: không còn trong danh sách chọn và không gợi ý nữa.
        ActAs(_labStaff);
        Ok(await Catalog.UpdateLotAsync(level2.Id, new SaveQcLotRequest(null, "Dung dịch chứng A", "DC-02", Expiry, 2, 100, 140, QcUnit.MgPerDl, false), CancellationToken.None));
        ActAs(_nurseA);
        var after = Ok(await Books.GetAsync(book.Id, CancellationToken.None));
        Assert.Equal(2, after.AvailableLots.Count);
        Assert.Null(after.Defaults!.Control2LotId);
        Fails(await Books.RecordRunAsync(book.Id, Run(Today.AddDays(-1), strip, level1, 55, level2, 120), CancellationToken.None), ErrorType.BusinessRule, "ngưng sử dụng");
    }

    [Fact]
    public async Task Corrections_AndReviews_AreRecordedByDifferentPeople_AndReviewedDaysAreLocked()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        ActAs(_nurseA);
        var book = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        var run = Ok(await Books.RecordRunAsync(book.Id, Run(Today, strip, level1, 55, level2, 210), CancellationToken.None)).Days.Single(d => d.Date == Today).Run!;

        Fails(await Books.CorrectRunAsync(book.Id, run.Id, new QcCorrectionRequest(" ", null), CancellationToken.None), ErrorType.Validation);
        var corrected = Ok(await Books.CorrectRunAsync(
            book.Id, run.Id, new QcCorrectionRequest("Ghi nhầm kết quả mức 2", Run(Today, strip, level1, 55, level2, 120)), CancellationToken.None));
        var day = corrected.Days.Single(d => d.Date == Today);
        var cancelled = Assert.Single(day.CancelledRuns);
        Assert.Equal(("Ghi nhầm kết quả mức 2", "dieuduong.noia Demo", false), (cancelled.CancelReason, cancelled.CancelledBy, cancelled.CanCorrect));
        Assert.Equal((run.Id, true), (day.Run!.ReplacesRunId!.Value, day.Run.Passed));

        // Quản trị có đủ quyền ghi và xem xét: ghi kết quả ngày hôm qua thì không tự xem xét ngày đó được.
        ActAs(_admin);
        var yesterday = Today.AddDays(-1);
        var byAdmin = Ok(await Books.RecordRunAsync(book.Id, Run(yesterday, strip, level1, 55, level2, 120), CancellationToken.None));
        Assert.False(byAdmin.Days.Single(d => d.Date == yesterday).CanReview);
        Fails(await Books.ReviewDaysAsync(book.Id, new QcReviewDaysRequest([new QcReviewDayRequest(yesterday, QcReviewOutcome.Passed, null)]), CancellationToken.None),
            ErrorType.BusinessRule, "không tự xem xét");

        ActAs(_labStaff);
        Fails(await Books.ReviewDaysAsync(book.Id, new QcReviewDaysRequest([]), CancellationToken.None), ErrorType.Validation);
        Fails(await Books.ReviewDaysAsync(book.Id, new QcReviewDaysRequest([new QcReviewDayRequest(Today, null, null)]), CancellationToken.None), ErrorType.Validation);
        Fails(await Books.ReviewDaysAsync(book.Id, new QcReviewDaysRequest([new QcReviewDayRequest(Today, QcReviewOutcome.NoQc, null)]), CancellationToken.None),
            ErrorType.BusinessRule, "không khớp");
        var reviewed = Ok(await Books.ReviewDaysAsync(
            book.Id,
            new QcReviewDaysRequest([new QcReviewDayRequest(Today, QcReviewOutcome.Passed, null), new QcReviewDayRequest(yesterday, QcReviewOutcome.Passed, "Đã kiểm")]),
            CancellationToken.None));
        var reviewedDay = reviewed.Days.Single(d => d.Date == Today);
        Assert.Equal(("ktv.hsh Demo", false, true), (reviewedDay.Review!.ReviewedBy, reviewedDay.CanReview, reviewedDay.CanReturn));

        ActAs(_nurseA);
        var locked = Ok(await Books.GetAsync(book.Id, CancellationToken.None)).Days.Single(d => d.Date == Today).Run!;
        Assert.False(locked.CanCorrect);
        Fails(await Books.CorrectRunAsync(book.Id, locked.Id, new QcCorrectionRequest("Sau khi xem xét", null), CancellationToken.None), ErrorType.BusinessRule, "đã xem xét");

        ActAs(_labStaff);
        Fails(await Books.ReturnDayAsync(book.Id, new QcReturnDayRequest(Today, null), CancellationToken.None), ErrorType.Validation);
        var returned = Ok(await Books.ReturnDayAsync(book.Id, new QcReturnDayRequest(Today, "Khoa báo ghi nhầm"), CancellationToken.None));
        var returnedDay = returned.Days.Single(d => d.Date == Today);
        Assert.Null(returnedDay.Review);
        Assert.Equal("Khoa báo ghi nhầm", Assert.Single(returnedDay.ReturnedReviews).ReturnReason);

        ActAs(_nurseA);
        Assert.True(Ok(await Books.GetAsync(book.Id, CancellationToken.None)).Days.Single(d => d.Date == Today).Run!.CanCorrect);
    }

    [Fact]
    public async Task MonthEnd_DepartmentSubmits_LabConfirms_WhenEveryDayIsReviewed()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        ActAs(_nurseA);
        var current = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        Fails(await Books.SubmitAsync(current.Id, CancellationToken.None), ErrorType.BusinessRule, "ngày cuối tháng");

        var october = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-10"), CancellationToken.None));
        Assert.True(october.Actions.Submit);
        Ok(await Books.RecordRunAsync(october.Id, Run(new DateOnly(2026, 10, 5), strip, level1, 55, level2, 120), CancellationToken.None));
        var submitted = Ok(await Books.SubmitAsync(october.Id, CancellationToken.None));
        Assert.Equal((QcBookStatus.Submitted, false, "dieuduong.noia Demo"), (submitted.Status, submitted.Actions.Record, submitted.SubmittedBy));

        ActAs(_labStaff);
        var review = Ok(await Books.GetAsync(october.Id, CancellationToken.None));
        Assert.Equal((true, false), (review.Actions.Return, review.Actions.Confirm));
        Fails(await Books.ConfirmAsync(october.Id, CancellationToken.None), ErrorType.BusinessRule, "chưa xem xét");
        var everyDay = review.Days.Select(d => new QcReviewDayRequest(d.Date, d.SuggestedOutcome, null)).ToList();
        Fails(await Books.ReviewDaysAsync(october.Id, new QcReviewDaysRequest([.. everyDay, everyDay[0]]), CancellationToken.None), ErrorType.Validation);
        // Xem xét cả tháng (31 ngày) một lần.
        var all = Ok(await Books.ReviewDaysAsync(october.Id, new QcReviewDaysRequest(everyDay), CancellationToken.None));
        Assert.True(all.Actions.Confirm);
        Assert.Equal(QcReviewOutcome.Passed, all.Days.Single(d => d.Date == new DateOnly(2026, 10, 5)).Review!.Outcome);
        Assert.Equal(30, all.Days.Count(d => d.Review?.Outcome == QcReviewOutcome.NoQc));

        var confirmed = Ok(await Books.ConfirmAsync(october.Id, CancellationToken.None));
        Assert.Equal((QcBookStatus.Confirmed, "ktv.hsh Demo", false, false), (confirmed.Status, confirmed.ConfirmedBy, confirmed.Actions.Review, confirmed.Actions.Return));
        Assert.All(confirmed.Days, d => Assert.False(d.CanReturn));
        Fails(await Books.ReturnAsync(october.Id, new QcReasonRequest("Muộn"), CancellationToken.None), ErrorType.BusinessRule);

        var summary = Ok(await Books.ListAsync("2026-10", null, CancellationToken.None)).Items.Single();
        Assert.Equal((QcBookStatus.Confirmed, 1, 31, 0), (summary.Status!.Value, summary.RecordedDays, summary.ReviewedDays, summary.DaysToReview));
    }

    [Fact]
    public async Task Pdf_IsForAnyoneWhoSeesTheBook_WithTheHospitalAndStatus()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        ActAs(_nurseA);
        var book = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        Ok(await Books.RecordRunAsync(book.Id, Run(Today, strip, level1, 55, level2, 120), CancellationToken.None));

        var file = Ok(await Books.RenderPdfAsync(book.Id, CancellationToken.None));
        Assert.Equal("so-noi-kiem-NOIA-2026-11-GL-0001.pdf", file.FileName);
        var document = _ctx.QcPdf.Document!;
        Assert.Equal(("Khoa Nội A (giả lập)", "Máy đo A (giả lập) GL-0001"), (document.Book.DepartmentName, document.Book.MeterLabel));
        Assert.StartsWith("Khoa đang ghi, chưa gửi sổ tháng (in lúc 15:00 11/11/2026)", document.PrintedLine, StringComparison.Ordinal);

        ActAs(_nurseB);
        Fails(await Books.RenderPdfAsync(book.Id, CancellationToken.None), ErrorType.NotFound);
        ActAs(_labStaff);
        Ok(await Books.RenderPdfAsync(book.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ScopeAllDepartments_LetsARecorderWorkForAnyDepartment_AndTheMeterMoveStartsANewBook()
    {
        var (meter, strip, level1, level2) = await CatalogAsync();
        ActAs(_nurseB);
        _ctx.CurrentUser.Permissions.Add(Permissions.ScopeAllDepartments);
        var book = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        Ok(await Books.RecordRunAsync(book.Id, Run(Today, strip, level1, 55, level2, 120), CancellationToken.None));

        // Khoa HSH chuyển máy sang khoa Ngoại B: sổ cũ của khoa Nội A không ghi thêm được, khoa Ngoại B mở sổ mới.
        ActAs(_labStaff);
        Ok(await Catalog.UpdateMeterAsync(meter.Id, new SaveQcMeterRequest(_wardB.Id, meter.Model, meter.SerialNumber, true), CancellationToken.None));
        ActAs(_nurseA);
        var old = Ok(await Books.GetAsync(book.Id, CancellationToken.None));
        Assert.False(old.Actions.Record);
        Fails(await Books.RecordRunAsync(book.Id, Run(Today.AddDays(-1), strip, level1, 55, level2, 120), CancellationToken.None), ErrorType.BusinessRule, "chuyển sang khoa khác");
        Assert.Equal([book.Id], Ok(await Books.ListAsync("2026-11", null, CancellationToken.None)).Items.Select(i => i.BookId));

        ActAs(_nurseB);
        var list = Ok(await Books.ListAsync("2026-11", null, CancellationToken.None));
        Assert.Equal((Guid?)null, Assert.Single(list.Items).BookId);
        var moved = Ok(await Books.OpenAsync(new OpenQcBookRequest(meter.Id, "2026-11"), CancellationToken.None));
        Assert.NotEqual(book.Id, moved.Id);
        Assert.Equal((_wardB.Id, true), (moved.DepartmentId, moved.Actions.Record));
    }
}
