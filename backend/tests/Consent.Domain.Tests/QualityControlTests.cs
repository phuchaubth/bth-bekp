using Consent.Domain.Common;
using Consent.Domain.QualityControl;

namespace Consent.Domain.Tests;

/// <summary>Sổ theo dõi kết quả nội kiểm đường huyết (quyết định 57): quy tắc của danh mục và sổ tháng. Dữ liệu giả lập.</summary>
public sealed class QualityControlTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid Ward = Guid.NewGuid();
    private static readonly Guid Nurse = Guid.NewGuid();
    private static readonly Guid OtherNurse = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    private readonly QcMeter _meter = QcMeter.Create(Ward, " Máy đo A (giả lập) ", " GL-0001 ", Now);
    private readonly QcLot _strip = QcLot.CreateTestStrip("Que thử A (giả lập)", "QT-01", new DateOnly(2027, 6, 30), Now);
    private readonly QcLot _level1 = QcLot.CreateControl("Dung dịch chứng A mức 1", "DC-01", new DateOnly(2027, 6, 30), 1, 40, 70, QcUnit.MgPerDl, Now);
    private readonly QcLot _level2 = QcLot.CreateControl("Dung dịch chứng A mức 2", "DC-02", new DateOnly(2027, 6, 30), 2, 100, 140, QcUnit.MgPerDl, Now);

    private static DateOnly Day(int day) => new(2026, 11, day);

    private QcBook Book() => QcBook.Open(_meter, 2026, 11, Nurse, Now);

    private QcRunReading Reading(int day, decimal value1, decimal value2, QcLot? strip = null, QcLot? level1 = null, QcLot? level2 = null) =>
        new(Day(day), strip ?? _strip, Day(1), new QcControlReading(level1 ?? _level1, Day(1), value1), new QcControlReading(level2 ?? _level2, Day(1), value2));

    [Fact]
    public void Catalog_TrimsText_AndValidatesLotsByKind()
    {
        Assert.Equal(("Máy đo A (giả lập)", "GL-0001", "Máy đo A (giả lập) GL-0001", true), (_meter.Model, _meter.SerialNumber, _meter.Label, _meter.IsActive));
        Assert.Throws<DomainException>(() => QcMeter.Create(Guid.Empty, "Máy", "S1", Now));
        Assert.Throws<DomainException>(() => QcMeter.Create(Ward, " ", "S1", Now));
        Assert.Throws<DomainException>(() => QcMeter.Create(Ward, "Máy", new string('9', QcMeter.SerialMaxLength + 1), Now));

        Assert.Equal((QcLotKind.TestStrip, (int?)null, (decimal?)null), (_strip.Kind, _strip.Level, _strip.RangeLow));
        Assert.Equal((2, 100m, 140m, QcUnit.MgPerDl), (_level2.Level!.Value, _level2.RangeLow!.Value, _level2.RangeHigh!.Value, _level2.Unit!.Value));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 0, 40, 70, QcUnit.MgPerDl, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 4, 40, 70, QcUnit.MgPerDl, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 1, 70, 70, QcUnit.MgPerDl, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 1, 4.25m, 7, QcUnit.MmolPerL, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 1, 0, 7, QcUnit.MmolPerL, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateControl("DC", "L", Today, 1, 4, 7, (QcUnit)9, Now));
        Assert.Throws<DomainException>(() => QcLot.CreateTestStrip("QT", "L", default, Now));
        Assert.Throws<DomainException>(() => _strip.Update("QT", "L", Today, 1, 40, 70, QcUnit.MgPerDl, true));
        Assert.Equal(4.3m, QcLot.CreateControl("DC", "L", Today, 1, 4.3m, 7.4m, QcUnit.MmolPerL, Now).RangeLow);
        Assert.Equal(("140", "6.5", "mg/dL", "mmol/L"), (QcLot.Format(140.0m), QcLot.Format(6.5m), QcLot.UnitLabel(QcUnit.MgPerDl), QcLot.UnitLabel(QcUnit.MmolPerL)));
    }

    [Fact]
    public void RecordRun_EvaluatesBothLevels_IncludingTheRangeLimits_AndCopiesTheLots()
    {
        var book = Book();

        var passed = book.RecordRun(_meter, Reading(1, 40, 140), Nurse, Today, Now);
        var failed = book.RecordRun(_meter, Reading(2, 39.9m, 120), Nurse, Today, Now);
        var high = book.RecordRun(_meter, Reading(3, 55, 140.1m), Nurse, Today, Now);

        Assert.True(passed.Passed);
        Assert.Equal((false, true, false), (failed.Control1Passed, failed.Control2Passed, failed.Passed));
        Assert.Equal((true, false, false), (high.Control1Passed, high.Control2Passed, high.Passed));
        Assert.Equal(("QT-01", "DC-01", 1, "DC-02", 2, 40m, 70m), (passed.StripLotNumber, passed.Control1LotNumber, passed.Control1Level, passed.Control2LotNumber, passed.Control2Level, passed.Control1Low, passed.Control1High));
        Assert.Equal((Nurse, Today, true), (passed.PerformedByUserId, passed.RecordedOn, passed.IsLate));
        Assert.Equal(3, book.Revision);

        // Sửa ngưỡng của lô sau đó không làm đổi kết quả đã ghi (kết quả giữ ngưỡng lúc ghi).
        _level1.Update("Dung dịch chứng A mức 1", "DC-01", new DateOnly(2027, 6, 30), 1, 30, 50, QcUnit.MgPerDl, true);
        Assert.Equal((40m, 70m, true), (passed.Control1Low, passed.Control1High, passed.Passed));
    }

    [Fact]
    public void RecordRun_RejectsWrongLots_ExpiredLots_FutureDates_AndASecondRunOfTheDay()
    {
        var book = Book();
        var expired = QcLot.CreateTestStrip("Que thử B", "QT-HET", Day(9), Now);
        var inactive = QcLot.CreateControl("Dung dịch chứng B", "DC-NGUNG", new DateOnly(2027, 1, 1), 1, 40, 70, QcUnit.MgPerDl, Now);
        inactive.Update("Dung dịch chứng B", "DC-NGUNG", new DateOnly(2027, 1, 1), 1, 40, 70, QcUnit.MgPerDl, isActive: false);
        var level3 = QcLot.CreateControl("Dung dịch chứng A mức 3", "DC-03", new DateOnly(2027, 6, 30), 3, 280, 380, QcUnit.MgPerDl, Now);

        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(10, 50, 120, strip: expired), Nurse, Today, Now));
        Assert.Equal(Day(9), book.RecordRun(_meter, Reading(9, 50, 120, strip: expired), Nurse, Today, Now).RunDate);
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 50, 120, strip: _level1), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 50, 120, level1: _strip), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 50, 120, level1: inactive), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 120, 50, level1: _level2, level2: _level1), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 50, 120, level2: _level1), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 0, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(11, 50.25m, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(21, 50, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(9, 50, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(
            _meter, new QcRunReading(new DateOnly(2026, 10, 31), _strip, Day(1), new QcControlReading(_level1, Day(1), 50), new QcControlReading(_level2, Day(1), 120)), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(
            _meter, new QcRunReading(Day(11), _strip, Day(12), new QcControlReading(_level1, Day(1), 50), new QcControlReading(_level2, Day(1), 120)), Nurse, new DateOnly(2026, 11, 20), Now));

        // Hai mức 2 và 3 (máy dùng Level 2/3) vẫn ghi được.
        Assert.True(book.RecordRun(_meter, Reading(12, 120, 300, level1: _level2, level2: level3), Nurse, Today, Now).Passed);
        Assert.Equal(2, book.Runs.Count);
    }

    [Fact]
    public void RecordRun_NeedsTheMeterOfTheBook_StillActive_AndInTheSameDepartment()
    {
        var book = Book();
        var other = QcMeter.Create(Ward, "Máy đo B", "GL-0002", Now);
        Assert.Throws<DomainException>(() => book.RecordRun(other, Reading(1, 50, 120), Nurse, Today, Now));

        _meter.Update(Guid.NewGuid(), _meter.Model, _meter.SerialNumber, isActive: true);
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now));

        _meter.Update(Ward, _meter.Model, _meter.SerialNumber, isActive: false);
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => QcBook.Open(_meter, 2026, 12, Nurse, Now));
    }

    [Fact]
    public void FollowUp_OnlyForFailedRuns_Once_WithReason_AndBothRepeatValuesOrNone()
    {
        var book = Book();
        var passed = book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now);
        var failed = book.RecordRun(_meter, Reading(2, 80, 120), Nurse, Today, Now);
        var noRepeat = book.RecordRun(_meter, Reading(3, 80, 120), Nurse, Today, Now);

        Assert.Throws<DomainException>(() => book.RecordFollowUp(passed.Id, "Lý do", 50, 120, Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordFollowUp(failed.Id, " ", 50, 120, Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordFollowUp(failed.Id, "Lý do", 50, null, Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordFollowUp(failed.Id, "Lý do", 50, 0, Nurse, Today, Now));

        book.RecordFollowUp(failed.Id, " Lọ dung dịch chứng để ngoài tủ mát ", 60, 125, OtherNurse, Today, Now);
        Assert.Equal(("Lọ dung dịch chứng để ngoài tủ mát", true, (bool?)true, true, OtherNurse), (failed.FailureReason, failed.HasRepeat, failed.RepeatPassed, failed.FinalPassed, failed.FollowUpByUserId!.Value));
        Assert.Throws<DomainException>(() => book.RecordFollowUp(failed.Id, "Lần hai", 60, 125, Nurse, Today, Now));

        book.RecordFollowUp(noRepeat.Id, "Hết que thử, không chạy lại", null, null, Nurse, Today, Now);
        Assert.Equal((false, (bool?)null, false), (noRepeat.HasRepeat, noRepeat.RepeatPassed, noRepeat.FinalPassed));
    }

    [Fact]
    public void Correction_KeepsTheOldRun_WithAReason_AndOnlyBeforeTheDayIsReviewed()
    {
        var book = Book();
        var wrong = book.RecordRun(_meter, Reading(1, 50, 210), Nurse, Today, Now);

        Assert.Throws<DomainException>(() => book.CorrectRun(wrong.Id, " ", _meter, Reading(1, 50, 120), Nurse, Today, Now));
        var corrected = book.CorrectRun(wrong.Id, "Ghi nhầm kết quả mức 2", _meter, Reading(1, 50, 120), Nurse, Today, Now)!;

        Assert.Equal((false, "Ghi nhầm kết quả mức 2", corrected.Id), (wrong.IsActive, wrong.CancelReason, wrong.ReplacedByRunId!.Value));
        Assert.Equal((true, wrong.Id), (corrected.IsActive, corrected.ReplacesRunId!.Value));
        Assert.Same(corrected, book.ActiveRun(Day(1)));
        Assert.Equal(2, book.Runs.Count);
        Assert.Throws<DomainException>(() => book.CorrectRun(wrong.Id, "Lại", _meter, Reading(1, 50, 120), Nurse, Today, Now));

        // Ghi nhầm ngày: chuyển sang ngày khác chưa có kết quả; ghi nhầm máy: chỉ hủy.
        var moved = book.CorrectRun(corrected.Id, "Chạy ngày 2, ghi nhầm ngày 1", _meter, Reading(2, 50, 120), Nurse, Today, Now)!;
        Assert.Equal((Day(2), (QcRun?)null), (moved.RunDate, book.ActiveRun(Day(1))));
        var other = book.RecordRun(_meter, Reading(3, 50, 120), Nurse, Today, Now);
        Assert.Throws<DomainException>(() => book.CorrectRun(other.Id, "Trùng ngày", _meter, Reading(2, 50, 120), Nurse, Today, Now));
        Assert.Null(book.CorrectRun(other.Id, "Ghi nhầm máy", _meter, null, Nurse, Today, Now));
        Assert.Null(book.ActiveRun(Day(3)));

        book.ReviewDays([new QcDayReviewInput(Day(2), QcReviewOutcome.Passed, null)], Reviewer, Today, Now);
        Assert.Throws<DomainException>(() => book.CorrectRun(moved.Id, "Sau khi xem xét", _meter, Reading(2, 51, 120), Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordFollowUp(moved.Id, "Sau khi xem xét", null, null, Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(2, 50, 120), Nurse, Today, Now));
    }

    [Fact]
    public void Review_MustMatchTheData_NotByThePerformer_AndReturningTheDayReopensIt()
    {
        var book = Book();
        var passed = book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now);
        var failed = book.RecordRun(_meter, Reading(2, 80, 120), Nurse, Today, Now);
        var repeated = book.RecordRun(_meter, Reading(3, 80, 120), Nurse, Today, Now);
        book.RecordFollowUp(repeated.Id, "Que thử ẩm", 55, 125, OtherNurse, Today, Now);

        Assert.Equal(
            [QcReviewOutcome.Passed, QcReviewOutcome.FailedNotRepeated, QcReviewOutcome.Passed, QcReviewOutcome.NoQc],
            new[] { 1, 2, 3, 4 }.Select(d => book.SuggestOutcome(Day(d))));
        Assert.False(book.Allows(Day(1), QcReviewOutcome.NoQc));
        Assert.False(book.Allows(Day(1), QcReviewOutcome.FailedNotRepeated));
        Assert.False(book.Allows(Day(2), QcReviewOutcome.Passed));
        Assert.False(book.Allows(Day(2), QcReviewOutcome.FailedRepeatedAfterReminder));
        Assert.True(book.Allows(Day(3), QcReviewOutcome.FailedRepeatedAfterReminder));
        Assert.False(book.Allows(Day(4), QcReviewOutcome.Passed));

        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.NoQc, null)], Reviewer, Today, Now));
        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null)], Nurse, Today, Now));
        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(3), QcReviewOutcome.Passed, null)], OtherNurse, Today, Now));
        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(21), QcReviewOutcome.NoQc, null)], Reviewer, Today, Now));
        Assert.Throws<DomainException>(() => book.ReviewDays(
            [new QcDayReviewInput(Day(4), QcReviewOutcome.NoQc, null), new QcDayReviewInput(Day(4), QcReviewOutcome.NoQc, null)], Reviewer, Today, Now));
        Assert.Throws<DomainException>(() => book.ReviewDays([], Reviewer, Today, Now));
        Assert.Empty(book.Reviews);

        book.ReviewDays(
            [
                new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null),
                new QcDayReviewInput(Day(2), QcReviewOutcome.FailedNotRepeated, " Đã nhắc khoa chạy lại "),
                new QcDayReviewInput(Day(3), QcReviewOutcome.FailedRepeatedAfterReminder, null),
                new QcDayReviewInput(Day(4), QcReviewOutcome.NoQc, null),
            ],
            Reviewer,
            Today,
            Now);
        Assert.Equal("Đã nhắc khoa chạy lại", book.ReviewOf(Day(2))!.Note);
        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null)], Reviewer, Today, Now));

        Assert.Throws<DomainException>(() => book.ReturnDay(Day(5), "Chưa xem xét", Reviewer, Now));
        Assert.Throws<DomainException>(() => book.ReturnDay(Day(1), " ", Reviewer, Now));
        book.ReturnDay(Day(1), "Khoa báo ghi nhầm kết quả mức 2", Reviewer, Now);
        var returned = Assert.Single(book.Reviews, r => r.Date == Day(1));
        Assert.Equal((false, "Khoa báo ghi nhầm kết quả mức 2"), (returned.IsEffective, returned.ReturnReason));
        Assert.Null(book.ReviewOf(Day(1)));

        book.CorrectRun(passed.Id, "Ghi nhầm kết quả mức 2", _meter, Reading(1, 50, 121), Nurse, Today, Now);
        book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null)], Reviewer, Today, Now);
        Assert.Equal(2, book.Reviews.Count(r => r.Date == Day(1)));
        Assert.False(failed.FinalPassed);
    }

    [Fact]
    public void SubmitAndConfirm_FromTheLastDay_EveryDayReviewed_ByAnotherPerson_ThenLocked()
    {
        var book = Book();
        var lastDay = new DateOnly(2026, 11, 30);
        var lastNow = new DateTimeOffset(2026, 11, 30, 3, 0, 0, TimeSpan.Zero);
        book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now);

        Assert.Throws<DomainException>(() => book.Submit(Nurse, new DateOnly(2026, 11, 29), Now));
        Assert.Throws<DomainException>(() => book.Confirm(Reviewer, Now));
        book.Submit(Nurse, lastDay, lastNow);
        Assert.Equal((QcBookStatus.Submitted, Nurse), (book.Status, book.SubmittedByUserId!.Value));

        // Đã gửi: khoa không ghi thêm, không điều chỉnh; Khoa HSH vẫn xem xét được.
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(2, 50, 120), Nurse, lastDay, lastNow));
        Assert.Throws<DomainException>(() => book.Submit(Nurse, lastDay, lastNow));

        var days = book.Days().ToList();
        Assert.Equal(30, days.Count);
        book.ReviewDays([.. days.Take(29).Select(d => new QcDayReviewInput(d, book.SuggestOutcome(d), null))], Reviewer, lastDay, lastNow);
        var missing = Assert.Throws<DomainException>(() => book.Confirm(Reviewer, lastNow));
        Assert.Contains("1 ngày chưa xem xét: 30", missing.Message, StringComparison.Ordinal);
        book.ReviewDays([new QcDayReviewInput(lastDay, QcReviewOutcome.NoQc, null)], Reviewer, lastDay, lastNow);
        Assert.Throws<DomainException>(() => book.Confirm(Nurse, lastNow));

        // Trả lại sổ có lý do, khoa gửi lại, Khoa HSH xác nhận.
        Assert.Throws<DomainException>(() => book.Return(" ", Reviewer, lastNow));
        book.Return("Bổ sung nguyên nhân ngày 2", Reviewer, lastNow);
        Assert.Equal((QcBookStatus.Open, "Bổ sung nguyên nhân ngày 2", (Guid?)null), (book.Status, book.ReturnReason, book.SubmittedByUserId));
        book.Submit(Nurse, lastDay, lastNow);
        book.Confirm(Reviewer, lastNow);
        Assert.Equal((QcBookStatus.Confirmed, Reviewer), (book.Status, book.ConfirmedByUserId!.Value));

        Assert.Throws<DomainException>(() => book.Return("Muộn", Reviewer, lastNow));
        Assert.Throws<DomainException>(() => book.ReturnDay(Day(1), "Muộn", Reviewer, lastNow));
        Assert.Throws<DomainException>(() => book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null)], Reviewer, lastDay, lastNow));
        Assert.Throws<DomainException>(() => book.RecordRun(_meter, Reading(2, 50, 120), Nurse, lastDay, lastNow));
    }

    [Fact]
    public void ReturningADay_OfASubmittedBook_ReturnsTheBookToo()
    {
        var book = Book();
        book.RecordRun(_meter, Reading(1, 50, 120), Nurse, Today, Now);
        book.ReviewDays([new QcDayReviewInput(Day(1), QcReviewOutcome.Passed, null)], Reviewer, Today, Now);
        book.Submit(Nurse, new DateOnly(2026, 11, 30), Now);

        book.ReturnDay(Day(1), "Ghi nhầm kết quả mức 1", Reviewer, Now);

        Assert.Equal((QcBookStatus.Open, "Ghi nhầm kết quả mức 1", Reviewer), (book.Status, book.ReturnReason, book.ReturnedByUserId!.Value));
        Assert.Null(book.SubmittedAtUtc);
    }

    [Fact]
    public void Days_CoverTheWholeMonth()
    {
        Assert.Equal(28, QcBook.Open(_meter, 2026, 2, Nurse, Now).Days().Count());
        Assert.Equal(29, QcBook.Open(_meter, 2028, 2, Nurse, Now).Days().Count());
        Assert.Equal((new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 31)), (QcBook.Open(_meter, 2026, 12, Nurse, Now).FirstDay, QcBook.Open(_meter, 2026, 12, Nurse, Now).LastDay));
        Assert.Throws<DomainException>(() => QcBook.Open(_meter, 2026, 13, Nurse, Now));
    }
}
