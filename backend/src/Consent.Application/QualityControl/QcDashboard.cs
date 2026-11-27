using System.Globalization;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Domain.QualityControl;

namespace Consent.Application.QualityControl;

/// <summary>
/// Số liệu nội kiểm của một sổ (một máy của một khoa trong một tháng), của một khoa hoặc cả tháng (quyết định 59).
/// <see cref="RequiredDays"/>: ngày phải thực hiện (mọi ngày đã qua của tháng, cách tính ở <see cref="QcDashboardBuilder"/>);
/// <see cref="RecordedDays"/>: ngày có kết quả; <see cref="MissingDays"/>: ngày phải thực hiện mà chưa có kết quả;
/// <see cref="LateDays"/>: ngày có nhãn Ghi muộn (kết quả ghi sau ngày thực hiện). Không đạt ở lần chạy đầu:
/// <see cref="FailedDays"/>, trong đó chạy lại đạt <see cref="RepeatPassedDays"/>, chạy lại vẫn không đạt
/// <see cref="RepeatFailedDays"/>, chưa chạy lại (cần nhắc khoa) <see cref="NotRepeatedDays"/>. Xem xét:
/// <see cref="ReviewedDays"/> ngày Khoa HSH đã xem xét, <see cref="DaysToReview"/> ngày đến hôm nay chưa xem xét (như danh sách sổ).
/// </summary>
public sealed record QcDashboardFiguresDto(
    int RequiredDays,
    int RecordedDays,
    int MissingDays,
    int LateDays,
    int FailedDays,
    int RepeatPassedDays,
    int RepeatFailedDays,
    int NotRepeatedDays,
    int ReviewedDays,
    int DaysToReview);

/// <summary>
/// Số sổ theo trạng thái: khoa đang ghi, bị trả lại (khoa ghi tiếp rồi gửi lại), đã gửi chờ Khoa HSH xác nhận, đã xác nhận; và
/// số máy đang dùng chưa mở sổ.
/// </summary>
public sealed record QcDashboardBookCountsDto(int Open, int Returned, int Submitted, int Confirmed, int NotOpened);

/// <summary>Lần không đạt gần nhất: ngày, máy, nguyên nhân khoa ghi (trống là khoa chưa ghi nguyên nhân).</summary>
public sealed record QcDashboardFailureDto(DateOnly Date, string MeterLabel, string? Reason);

/// <summary>
/// Một máy của khoa trong tháng: sổ (<see cref="BookId"/> trống là máy đang dùng chưa mở sổ), trạng thái sổ, số liệu, các ngày
/// phải thực hiện chưa có kết quả. <see cref="CanOpen"/>: người xem mở được sổ (khoa của sổ hoặc Khoa HSH; quyền chỉ xem
/// dashboard thì không).
/// </summary>
public sealed record QcDashboardMeterDto(
    Guid MeterId,
    string MeterLabel,
    bool MeterActive,
    Guid? BookId,
    QcBookStatus? Status,
    bool Returned,
    bool CanOpen,
    QcDashboardFiguresDto Figures,
    IReadOnlyList<DateOnly> MissingDates,
    QcDashboardFailureDto? LatestFailure);

/// <summary>Một khoa: cộng số liệu các máy, đếm sổ theo trạng thái, lần không đạt gần nhất, từng máy (theo tên máy).</summary>
public sealed record QcDashboardDepartmentDto(
    Guid DepartmentId,
    string DepartmentName,
    QcDashboardFiguresDto Figures,
    QcDashboardBookCountsDto Books,
    QcDashboardFailureDto? LatestFailure,
    IReadOnlyList<QcDashboardMeterDto> Meters);

/// <summary>Cả tháng (các khoa người xem thấy): số khoa, số máy, số liệu, số sổ theo trạng thái.</summary>
public sealed record QcDashboardTotalsDto(int Departments, int Meters, QcDashboardFiguresDto Figures, QcDashboardBookCountsDto Books);

/// <summary>Một tháng của biểu đồ xu hướng (tháng dạng <c>yyyy-MM</c>): tỷ lệ thực hiện, tỷ lệ không đạt tính từ các số này.</summary>
public sealed record QcDashboardMonthDto(string Month, int RequiredDays, int RecordedDays, int FailedDays, int LateDays);

/// <summary>
/// Lô đang dùng đã hết hạn hoặc còn ≤ <see cref="QcDashboardBuilder.ExpiryWarningDays"/> ngày (<see cref="DaysLeft"/> âm là đã
/// hết hạn; 0 là hết hạn hôm nay, hôm nay vẫn dùng được). <see cref="UsedBy"/>: khoa có kết quả gần nhất của máy dùng lô này.
/// </summary>
public sealed record QcDashboardLotAlertDto(
    Guid LotId,
    QcLotKind Kind,
    string ProductName,
    string LotNumber,
    int? Level,
    DateOnly ExpiryDate,
    int DaysLeft,
    IReadOnlyList<string> UsedBy);

/// <summary>Máy đang dùng của một khoa.</summary>
public sealed record QcDashboardMeterRefDto(Guid MeterId, string MeterLabel, Guid DepartmentId, string DepartmentName);

/// <summary>Sổ của tháng trước chưa xác nhận: khoa chưa gửi (hoặc bị trả lại), hay đã gửi chờ Khoa HSH xác nhận.</summary>
public sealed record QcDashboardPendingBookDto(
    Guid BookId,
    string Month,
    Guid DepartmentId,
    string DepartmentName,
    string MeterLabel,
    QcBookStatus Status,
    bool Returned,
    bool CanOpen);

/// <summary>
/// Cảnh báo tính đến hôm nay (không theo tháng đang xem): lô đang dùng hết hạn, sắp hết hạn; các mức dung dịch chứng và số lô que
/// thử đang dùng còn hạn (chưa đủ hai mức hoặc hết que thử thì khoa không ghi được kết quả); máy đang dùng chưa mở sổ tháng này
/// (<see cref="CurrentMonth"/>); sổ của <see cref="QcDashboardBuilder.PendingMonths"/> tháng trước chưa xác nhận.
/// </summary>
public sealed record QcDashboardAlertsDto(
    string CurrentMonth,
    IReadOnlyList<QcDashboardLotAlertDto> Lots,
    IReadOnlyList<int> ControlLevels,
    int StripLots,
    IReadOnlyList<QcDashboardMeterRefDto> MetersWithoutBook,
    IReadOnlyList<QcDashboardPendingBookDto> PendingBooks);

/// <summary>
/// Dashboard giám sát nội kiểm đường huyết tại giường của một tháng (quyết định 59, tháng dạng <c>yyyy-MM</c>):
/// <see cref="Today"/> là hôm nay theo giờ bệnh viện; tổng cả tháng; theo khoa, theo máy; xu hướng
/// <see cref="QcDashboardBuilder.TrendMonths"/> tháng (tháng đang xem ở cuối); cảnh báo tính đến hôm nay.
/// </summary>
public sealed record QcDashboardDto(
    string Month,
    DateOnly Today,
    QcDashboardTotalsDto Totals,
    IReadOnlyList<QcDashboardDepartmentDto> Departments,
    IReadOnlyList<QcDashboardMonthDto> Trend,
    QcDashboardAlertsDto Alerts);

/// <summary>
/// Dựng dashboard nội kiểm (quyết định 59) từ danh mục và các sổ. Tỷ lệ thực hiện tính trên <b>mọi ngày trong tháng</b> như sổ
/// giấy, mỗi máy mỗi ngày một lần nội kiểm:
/// <list type="bullet">
/// <item>Ngày phải thực hiện là các ngày của tháng đến hôm qua; hôm nay chỉ tính khi đã có kết quả (khoa còn chạy trong ngày).
/// Tháng đã qua tính cả tháng.</item>
/// <item>Máy khai báo giữa tháng tính từ ngày khai báo (ghi bù từ sổ giấy kết quả trước ngày khai báo thì tính từ kết quả đầu
/// tiên).</item>
/// <item>Máy đã ngưng sử dụng hoặc đã chuyển sang khoa khác: phần mềm không lưu ngày ngưng, ngày chuyển, nên sổ của khoa cũ chỉ
/// tính đến ngày có kết quả cuối cùng của sổ. Khoa nhận máy trong tháng tính từ ngày sau kết quả cuối của khoa cũ.</item>
/// <item>Máy đang dùng chưa mở sổ tháng đó: mọi ngày phải thực hiện là chưa ghi.</item>
/// </list>
/// Chỉ đọc; khoa người xem không thấy thì không có trong số liệu. Lô vật tư dùng chung toàn viện nên ai cũng thấy cảnh báo lô.
/// </summary>
public static class QcDashboardBuilder
{
    /// <summary>Số tháng của biểu đồ xu hướng (tháng đang xem và các tháng trước).</summary>
    public const int TrendMonths = 6;

    /// <summary>Lô còn bấy nhiêu ngày (hoặc ít hơn) là sắp hết hạn.</summary>
    public const int ExpiryWarningDays = 30;

    /// <summary>Báo sổ chưa xác nhận của bấy nhiêu tháng trước tháng này.</summary>
    public const int PendingMonths = 5;

    private static readonly QcDashboardFiguresDto Zero = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// <paramref name="month"/>: một ngày của tháng đang xem (không sau tháng này); <paramref name="books"/>: các sổ từ
    /// <see cref="TrendMonths"/> − 1 tháng trước tháng đang xem đến tháng này; <paramref name="canSee"/>: khoa người xem thấy được;
    /// <paramref name="canOpen"/>: khoa người xem mở được sổ.
    /// </summary>
    public static QcDashboardDto Build(
        DateOnly month,
        DateOnly today,
        IReadOnlyList<QcMeter> meters,
        IReadOnlyList<QcLot> lots,
        IReadOnlyList<QcBook> books,
        IReadOnlyDictionary<Guid, string> departmentNames,
        Func<Guid, bool> canSee,
        Func<Guid, bool> canOpen)
    {
        ArgumentNullException.ThrowIfNull(meters);
        ArgumentNullException.ThrowIfNull(lots);
        ArgumentNullException.ThrowIfNull(books);
        ArgumentNullException.ThrowIfNull(departmentNames);
        ArgumentNullException.ThrowIfNull(canSee);
        ArgumentNullException.ThrowIfNull(canOpen);

        var first = new DateOnly(month.Year, month.Month, 1);
        var byId = meters.ToDictionary(m => m.Id);
        string DepartmentName(Guid id) => departmentNames.GetValueOrDefault(id, "Không rõ");
        string MeterLabel(Guid id) => byId.GetValueOrDefault(id)?.Label ?? "Không rõ";

        QcDashboardFailureDto? Failure(Row? row) =>
            row?.LatestFailure is { } run ? new QcDashboardFailureDto(run.RunDate, MeterLabel(row.MeterId), run.FailureReason) : null;

        QcDashboardMeterDto Meter(Row row) => new(
            row.MeterId,
            MeterLabel(row.MeterId),
            byId.GetValueOrDefault(row.MeterId)?.IsActive ?? false,
            row.Book?.Id,
            row.Book?.Status,
            row.Book is { } book && IsReturned(book),
            row.Book is not null && canOpen(row.DepartmentId),
            row.Figures,
            row.MissingDates,
            Failure(row));

        var rows = Rows(first, today, byId, books).Where(r => canSee(r.DepartmentId)).ToList();
        var departments = rows
            .GroupBy(r => r.DepartmentId)
            .Select(g => new QcDashboardDepartmentDto(
                g.Key,
                DepartmentName(g.Key),
                Sum(g.Select(r => r.Figures)),
                CountBooks([.. g]),
                Failure(Latest(g)),
                [.. g.Select(Meter).OrderBy(m => m.MeterLabel, VietnameseComparer.Instance)]))
            .OrderBy(d => d.DepartmentName, VietnameseComparer.Instance)
            .ToList();
        var totals = new QcDashboardTotalsDto(
            departments.Count, rows.Select(r => r.MeterId).Distinct().Count(), Sum(rows.Select(r => r.Figures)), CountBooks(rows));

        var trend = Enumerable.Range(1 - TrendMonths, TrendMonths)
            .Select(offset =>
            {
                var monthFirst = first.AddMonths(offset);
                var figures = Sum(Rows(monthFirst, today, byId, books).Where(r => canSee(r.DepartmentId)).Select(r => r.Figures));
                return new QcDashboardMonthDto(MonthKey(monthFirst), figures.RequiredDays, figures.RecordedDays, figures.FailedDays, figures.LateDays);
            })
            .ToList();

        return new QcDashboardDto(
            MonthKey(first), today, totals, departments, trend, Alerts(today, meters, lots, books, DepartmentName, MeterLabel, canSee, canOpen));
    }

    private static QcDashboardAlertsDto Alerts(
        DateOnly today,
        IReadOnlyList<QcMeter> meters,
        IReadOnlyList<QcLot> lots,
        IReadOnlyList<QcBook> books,
        Func<Guid, string> departmentName,
        Func<Guid, string> meterLabel,
        Func<Guid, bool> canSee,
        Func<Guid, bool> canOpen)
    {
        var current = new DateOnly(today.Year, today.Month, 1);

        // Như danh sách sổ: máy đang dùng mà khoa đang giữ máy chưa mở sổ tháng này (kể cả máy vừa chuyển khoa).
        var currentBooks = books.Where(b => b.FirstDay == current).ToList();
        var withoutBook = meters
            .Where(m => m.IsActive && canSee(m.DepartmentId) && !currentBooks.Exists(b => b.MeterId == m.Id && b.DepartmentId == m.DepartmentId))
            .Select(m => new QcDashboardMeterRefDto(m.Id, m.Label, m.DepartmentId, departmentName(m.DepartmentId)))
            .OrderBy(m => m.DepartmentName, VietnameseComparer.Instance)
            .ThenBy(m => m.MeterLabel, VietnameseComparer.Instance)
            .ToList();

        var pendingFrom = current.AddMonths(-PendingMonths);
        var pending = books
            .Where(b => b.FirstDay >= pendingFrom && b.FirstDay < current && b.Status != QcBookStatus.Confirmed && canSee(b.DepartmentId))
            .Select(b => new QcDashboardPendingBookDto(
                b.Id, MonthKey(b.FirstDay), b.DepartmentId, departmentName(b.DepartmentId), meterLabel(b.MeterId), b.Status, IsReturned(b),
                canOpen(b.DepartmentId)))
            .OrderBy(b => b.Month, StringComparer.Ordinal)
            .ThenBy(b => b.DepartmentName, VietnameseComparer.Instance)
            .ThenBy(b => b.MeterLabel, VietnameseComparer.Instance)
            .ToList();

        // Lô máy đang dùng: lô của kết quả gần nhất (từ đầu tháng trước) của mỗi máy, ở khoa người xem thấy.
        var recentFrom = current.AddMonths(-1);
        var inUse = books
            .Where(b => canSee(b.DepartmentId))
            .SelectMany(b => b.Runs.Where(r => r.IsActive && r.RunDate >= recentFrom).Select(r => (Book: b, Run: r)))
            .GroupBy(x => x.Book.MeterId)
            .Select(g => g.OrderByDescending(x => x.Run.RunDate).ThenByDescending(x => x.Run.RecordedAtUtc).First())
            .ToList();
        var warnUntil = today.AddDays(ExpiryWarningDays);
        var lotAlerts = lots
            .Where(l => l.IsActive && l.ExpiryDate <= warnUntil)
            .OrderBy(l => l.ExpiryDate)
            .ThenBy(l => l.Kind)
            .ThenBy(l => l.Level)
            .ThenBy(l => l.LotNumber, StringComparer.Ordinal)
            .Select(l => new QcDashboardLotAlertDto(
                l.Id,
                l.Kind,
                l.ProductName,
                l.LotNumber,
                l.Level,
                l.ExpiryDate,
                l.ExpiryDate.DayNumber - today.DayNumber,
                [.. inUse
                    .Where(x => x.Run.StripLotId == l.Id || x.Run.Control1LotId == l.Id || x.Run.Control2LotId == l.Id)
                    .Select(x => departmentName(x.Book.DepartmentId))
                    .Distinct(StringComparer.Ordinal)
                    .Order(VietnameseComparer.Instance)]))
            .ToList();

        var usable = lots.Where(l => l.IsActive && l.ExpiryDate >= today).ToList();
        return new QcDashboardAlertsDto(
            MonthKey(current),
            lotAlerts,
            [.. usable.Where(l => l.Kind == QcLotKind.ControlSolution && l.Level is not null).Select(l => l.Level!.Value).Distinct().Order()],
            usable.Count(l => l.Kind == QcLotKind.TestStrip),
            withoutBook,
            pending);
    }

    /// <summary>Các dòng của một tháng: mỗi sổ một dòng, cộng máy đang dùng chưa có sổ nào trong tháng (đã khai báo trước cuối tháng).</summary>
    private static IEnumerable<Row> Rows(DateOnly first, DateOnly today, IReadOnlyDictionary<Guid, QcMeter> meters, IReadOnlyList<QcBook> books)
    {
        var last = first.AddMonths(1).AddDays(-1);
        var monthBooks = books.Where(b => b.FirstDay == first).ToList();
        foreach (var book in monthBooks)
        {
            yield return BookRow(book, meters.GetValueOrDefault(book.MeterId), monthBooks, today);
        }

        foreach (var meter in meters.Values)
        {
            if (meter.IsActive && LocalDate(meter.CreatedAtUtc) <= last && !monthBooks.Exists(b => b.MeterId == meter.Id))
            {
                var missing = Period(Later(first, LocalDate(meter.CreatedAtUtc)), CountedThrough(first, today, hasToday: false)).ToList();
                yield return new Row(
                    meter.DepartmentId, meter.Id, null, Zero with { RequiredDays = missing.Count, MissingDays = missing.Count }, missing, null);
            }
        }
    }

    private static Row BookRow(QcBook book, QcMeter? meter, IReadOnlyList<QcBook> monthBooks, DateOnly today)
    {
        var runs = book.Runs.Where(r => r.IsActive).GroupBy(r => r.RunDate).ToDictionary(g => g.Key, g => g.First());
        DateOnly? firstRun = runs.Count == 0 ? null : runs.Keys.Min();
        DateOnly? lastRun = runs.Count == 0 ? null : runs.Keys.Max();
        var declared = meter is null ? book.FirstDay : LocalDate(meter.CreatedAtUtc);
        var start = Later(book.FirstDay, Earlier(declared, firstRun));
        var end = CountedThrough(book.FirstDay, today, runs.ContainsKey(today));
        if (meter is null || !meter.IsActive || meter.DepartmentId != book.DepartmentId)
        {
            // Máy đã ngưng hoặc đã chuyển khoa: không lưu ngày ngưng, ngày chuyển nên chỉ tính đến kết quả cuối của sổ.
            end = lastRun ?? start.AddDays(-1);
        }
        else if (monthBooks
                     .Where(o => o.MeterId == book.MeterId && o.Id != book.Id)
                     .SelectMany(o => o.Runs)
                     .Where(r => r.IsActive)
                     .Select(r => (DateOnly?)r.RunDate)
                     .Max() is { } handedOver)
        {
            // Máy chuyển đến trong tháng: tính từ ngày sau kết quả cuối của khoa cũ (khoa mới ghi sớm hơn thì từ kết quả đầu tiên).
            start = Later(start, Earlier(handedOver.AddDays(1), firstRun));
        }

        var required = Period(start, end).Union(runs.Keys).ToList();
        var missing = required.Where(d => !runs.ContainsKey(d)).Order().ToList();
        var active = runs.Values.ToList();
        var lastToReview = today < book.LastDay ? today : book.LastDay;
        var days = book.Days().ToList();
        var figures = new QcDashboardFiguresDto(
            required.Count,
            active.Count,
            missing.Count,
            active.Count(r => r.IsLate),
            active.Count(r => !r.Passed),
            active.Count(r => !r.Passed && r.RepeatPassed == true),
            active.Count(r => !r.Passed && r.RepeatPassed == false),
            active.Count(r => !r.Passed && !r.HasRepeat),
            days.Count(d => book.ReviewOf(d) is not null),
            days.Count(d => d <= lastToReview && book.ReviewOf(d) is null));
        var latestFailure = active.Where(r => !r.Passed).OrderByDescending(r => r.RunDate).FirstOrDefault();
        return new Row(book.DepartmentId, book.MeterId, book, figures, missing, latestFailure);
    }

    /// <summary>Ngày cuối được tính của tháng: tháng đã qua là ngày cuối tháng; tháng này là hôm qua, hoặc hôm nay nếu đã có kết quả.</summary>
    private static DateOnly CountedThrough(DateOnly first, DateOnly today, bool hasToday)
    {
        var last = first.AddMonths(1).AddDays(-1);
        return today > last ? last : hasToday ? today : today.AddDays(-1);
    }

    private static IEnumerable<DateOnly> Period(DateOnly start, DateOnly end)
    {
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            yield return date;
        }
    }

    private static Row? Latest(IEnumerable<Row> rows) =>
        rows.Where(r => r.LatestFailure is not null)
            .OrderByDescending(r => r.LatestFailure!.RunDate)
            .ThenByDescending(r => r.LatestFailure!.RecordedAtUtc)
            .FirstOrDefault();

    private static QcDashboardFiguresDto Sum(IEnumerable<QcDashboardFiguresDto> items) =>
        items.Aggregate(Zero, (a, b) => new QcDashboardFiguresDto(
            a.RequiredDays + b.RequiredDays,
            a.RecordedDays + b.RecordedDays,
            a.MissingDays + b.MissingDays,
            a.LateDays + b.LateDays,
            a.FailedDays + b.FailedDays,
            a.RepeatPassedDays + b.RepeatPassedDays,
            a.RepeatFailedDays + b.RepeatFailedDays,
            a.NotRepeatedDays + b.NotRepeatedDays,
            a.ReviewedDays + b.ReviewedDays,
            a.DaysToReview + b.DaysToReview));

    private static QcDashboardBookCountsDto CountBooks(IReadOnlyList<Row> rows) => new(
        rows.Count(r => r.Book is { Status: QcBookStatus.Open } book && !IsReturned(book)),
        rows.Count(r => r.Book is { } book && IsReturned(book)),
        rows.Count(r => r.Book is { Status: QcBookStatus.Submitted }),
        rows.Count(r => r.Book is { Status: QcBookStatus.Confirmed }),
        rows.Count(r => r.Book is null));

    /// <summary>Khoa HSH đã trả lại sổ (hoặc trả lại một ngày của sổ đã gửi), khoa đang ghi tiếp.</summary>
    private static bool IsReturned(QcBook book) => book.Status == QcBookStatus.Open && book.ReturnedAtUtc is not null;

    private static DateOnly LocalDate(DateTimeOffset utc) => DateOnly.FromDateTime(utc.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    private static DateOnly Later(DateOnly a, DateOnly b) => a > b ? a : b;

    private static DateOnly Earlier(DateOnly a, DateOnly? b) => b is { } value && value < a ? value : a;

    private static string MonthKey(DateOnly first) => first.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    /// <summary>Một máy của một khoa trong một tháng: sổ (trống là chưa mở sổ), số liệu, ngày chưa ghi, kết quả không đạt gần nhất.</summary>
    private sealed record Row(
        Guid DepartmentId, Guid MeterId, QcBook? Book, QcDashboardFiguresDto Figures, IReadOnlyList<DateOnly> MissingDates, QcRun? LatestFailure);
}
