using System.Globalization;
using Consent.Domain.Common;

namespace Consent.Domain.QualityControl;

/// <summary>Một ngày Khoa HSH xem xét: ngày, kết quả xem xét, ghi chú.</summary>
public sealed record QcDayReviewInput(DateOnly Date, QcReviewOutcome Outcome, string? Note);

/// <summary>
/// Sổ theo dõi kết quả nội kiểm đường huyết tại giường của một máy trong một khoa, một tháng (quyết định 57, mẫu CT-ĐHTG/BM01).
/// Khoa ghi kết quả từng ngày; nhân viên Khoa HSH xem xét từng ngày; cuối tháng khoa gửi sổ, Khoa HSH xác nhận thì sổ khóa hẳn.
/// Không sửa dữ liệu đã ghi: điều chỉnh là bản ghi mới có lý do, bản cũ vẫn giữ. Người ghi kết quả không tự xem xét, người gửi sổ
/// không tự xác nhận (xác nhận giữa hai bộ phận). Mỗi thay đổi tăng <see cref="Revision"/> để hai người cùng lúc thay đổi một
/// sổ thì người sau phải tải lại.
/// </summary>
public sealed class QcBook : Entity, IAuditableEntity
{
    public const int ReasonMaxLength = 500;

    private readonly List<QcRun> _runs = [];
    private readonly List<QcDayReview> _reviews = [];

    private QcBook(Guid id)
        : base(id)
    {
    }

    public Guid MeterId { get; private set; }

    /// <summary>Khoa giữ máy lúc mở sổ.</summary>
    public Guid DepartmentId { get; private set; }

    public int Year { get; private set; }

    public int Month { get; private set; }

    public QcBookStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? SubmittedByUserId { get; private set; }

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public Guid? ConfirmedByUserId { get; private set; }

    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    /// <summary>Lý do Khoa HSH trả lại sổ (lần gần nhất).</summary>
    public string? ReturnReason { get; private set; }

    public Guid? ReturnedByUserId { get; private set; }

    public DateTimeOffset? ReturnedAtUtc { get; private set; }

    /// <summary>Số lần sổ thay đổi.</summary>
    public int Revision { get; private set; }

    /// <summary>Mọi kết quả đã ghi, kể cả kết quả đã hủy.</summary>
    public IReadOnlyList<QcRun> Runs => _runs;

    /// <summary>Mọi lần xem xét, kể cả lần đã bị trả lại.</summary>
    public IReadOnlyList<QcDayReview> Reviews => _reviews;

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public static QcBook Open(QcMeter meter, int year, int month, Guid userId, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(meter);
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người mở sổ.");
        }

        if (!meter.IsActive)
        {
            throw new DomainException("Máy đã ngưng sử dụng, không mở sổ mới được.");
        }

        if (year is < 2000 or > 9999 || month is < 1 or > 12)
        {
            throw new DomainException("Tháng không hợp lệ.");
        }

        return new QcBook(Guid.CreateVersion7())
        {
            MeterId = meter.Id,
            DepartmentId = meter.DepartmentId,
            Year = year,
            Month = month,
            Status = QcBookStatus.Open,
            CreatedByUserId = userId,
            CreatedAtUtc = nowUtc,
        };
    }

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    /// <summary>Kết quả đang có hiệu lực của ngày (chưa hủy).</summary>
    public QcRun? ActiveRun(DateOnly date) => _runs.FirstOrDefault(r => r.RunDate == date && r.IsActive);

    /// <summary>Lần xem xét đang có hiệu lực của ngày.</summary>
    public QcDayReview? ReviewOf(DateOnly date) => _reviews.FirstOrDefault(r => r.Date == date && r.IsEffective);

    /// <summary>
    /// Kết quả xem xét gợi ý cho Khoa HSH: chưa có kết quả là không có QC; đạt (ngay hoặc khi chạy lại) là đạt; còn lại là không
    /// đạt, không chạy lại. Khoa HSH vẫn chọn được lựa chọn khác phù hợp với dữ liệu.
    /// </summary>
    public QcReviewOutcome SuggestOutcome(DateOnly date) =>
        ActiveRun(date) switch
        {
            null => QcReviewOutcome.NoQc,
            { FinalPassed: true } => QcReviewOutcome.Passed,
            _ => QcReviewOutcome.FailedNotRepeated,
        };

    /// <summary>Kết quả xem xét có khớp dữ liệu của ngày không (ví dụ không chọn "Không có QC" cho ngày đã có kết quả).</summary>
    public bool Allows(DateOnly date, QcReviewOutcome outcome) =>
        (ActiveRun(date), outcome) switch
        {
            (null, QcReviewOutcome.NoQc) => true,
            (null, _) => false,
            (_, QcReviewOutcome.NoQc) => false,
            ({ } run, QcReviewOutcome.Passed) => run.FinalPassed,
            ({ } run, QcReviewOutcome.FailedNotRepeated) => !run.FinalPassed,
            ({ } run, QcReviewOutcome.FailedRepeatedAfterReminder) => !run.Passed && run.HasRepeat,
            _ => false,
        };

    /// <summary>Khoa ghi kết quả của một ngày chưa có kết quả.</summary>
    public QcRun RecordRun(QcMeter meter, QcRunReading reading, Guid userId, DateOnly today, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(reading);
        EnsureMeter(meter);
        EnsureDepartmentCanChange(reading.RunDate, today);
        if (ActiveRun(reading.RunDate) is not null)
        {
            throw new DomainException($"Ngày {Day(reading.RunDate)} đã có kết quả; ghi sai thì ghi điều chỉnh.");
        }

        var run = QcRun.Create(Id, reading, userId, today, nowUtc, replacesRunId: null);
        _runs.Add(run);
        Touch();
        return run;
    }

    /// <summary>Kết quả không đạt: khoa ghi nguyên nhân và kết quả chạy lại.</summary>
    public void RecordFollowUp(
        Guid runId, string? reason, decimal? repeat1, decimal? repeat2, Guid userId, DateOnly today, DateTimeOffset nowUtc)
    {
        var run = Active(runId);
        EnsureDepartmentCanChange(run.RunDate, today);
        run.RecordFollowUp(reason, repeat1, repeat2, userId, nowUtc);
        Touch();
    }

    /// <summary>
    /// Khoa điều chỉnh kết quả ghi sai: hủy kết quả (bắt buộc lý do) và ghi kết quả thay thế nếu có (<paramref name="replacement"/>
    /// <c>null</c> là chỉ hủy, ví dụ ghi nhầm máy). Chỉ khi Khoa HSH chưa xem xét ngày đó.
    /// </summary>
    public QcRun? CorrectRun(
        Guid runId, string? reason, QcMeter meter, QcRunReading? replacement, Guid userId, DateOnly today, DateTimeOffset nowUtc)
    {
        var run = Active(runId);
        EnsureDepartmentCanChange(run.RunDate, today);
        QcRun? created = null;
        if (replacement is not null)
        {
            EnsureMeter(meter);
            if (replacement.RunDate != run.RunDate)
            {
                EnsureDepartmentCanChange(replacement.RunDate, today);
                if (ActiveRun(replacement.RunDate) is not null)
                {
                    throw new DomainException($"Ngày {Day(replacement.RunDate)} đã có kết quả.");
                }
            }

            created = QcRun.Create(Id, replacement, userId, today, nowUtc, replacesRunId: run.Id);
        }

        run.Cancel(reason, userId, nowUtc, created?.Id);
        if (created is not null)
        {
            _runs.Add(created);
        }

        Touch();
        return created;
    }

    /// <summary>
    /// Nhân viên Khoa HSH xem xét một hoặc nhiều ngày (đến hôm nay) chưa xem xét. Kết quả xem xét phải khớp dữ liệu của ngày; người
    /// ghi kết quả của ngày không tự xem xét.
    /// </summary>
    public void ReviewDays(IReadOnlyList<QcDayReviewInput> days, Guid userId, DateOnly today, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(days);
        if (Status == QcBookStatus.Confirmed)
        {
            throw new DomainException("Sổ đã xác nhận, không thay đổi được nữa.");
        }

        if (days.Count == 0)
        {
            throw new DomainException("Chọn ngày cần xem xét.");
        }

        if (days.Select(d => d.Date).Distinct().Count() != days.Count)
        {
            throw new DomainException("Mỗi ngày chỉ xem xét một lần.");
        }

        foreach (var day in days)
        {
            if (!Contains(day.Date) || day.Date > today)
            {
                throw new DomainException($"Chỉ xem xét được ngày trong tháng {MonthLabel} và đến hôm nay.");
            }

            if (ReviewOf(day.Date) is not null)
            {
                throw new DomainException($"Ngày {Day(day.Date)} đã xem xét; trả lại ngày nếu cần xem xét lại.");
            }

            if (!Enum.IsDefined(day.Outcome) || !Allows(day.Date, day.Outcome))
            {
                throw new DomainException($"Kết quả xem xét ngày {Day(day.Date)} không khớp kết quả khoa đã ghi.");
            }

            if (ActiveRun(day.Date) is { } run && (run.PerformedByUserId == userId || run.FollowUpByUserId == userId))
            {
                throw new DomainException($"Người ghi kết quả ngày {Day(day.Date)} không tự xem xét được.");
            }
        }

        foreach (var day in days)
        {
            _reviews.Add(QcDayReview.Create(Id, day.Date, day.Outcome, day.Note, userId, nowUtc));
        }

        Touch();
    }

    /// <summary>
    /// Khoa HSH trả lại một ngày đã xem xét (bắt buộc lý do) để khoa ghi điều chỉnh. Sổ đã gửi thì cũng trả lại sổ cho khoa.
    /// </summary>
    public void ReturnDay(DateOnly date, string? reason, Guid userId, DateTimeOffset nowUtc)
    {
        if (Status == QcBookStatus.Confirmed)
        {
            throw new DomainException("Sổ đã xác nhận, không thay đổi được nữa.");
        }

        var review = ReviewOf(date) ?? throw new DomainException($"Ngày {Day(date)} chưa được xem xét.");
        review.Return(reason, userId, nowUtc);
        if (Status == QcBookStatus.Submitted)
        {
            Reopen(review.ReturnReason!, userId, nowUtc);
        }

        Touch();
    }

    /// <summary>Khoa gửi sổ tháng cho Khoa HSH, từ ngày cuối tháng; gửi rồi khoa không ghi thêm được.</summary>
    public void Submit(Guid userId, DateOnly today, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người gửi sổ.");
        }

        EnsureOpen();
        if (today < LastDay)
        {
            throw new DomainException($"Gửi sổ tháng từ ngày cuối tháng ({Day(LastDay)}).");
        }

        Status = QcBookStatus.Submitted;
        SubmittedByUserId = userId;
        SubmittedAtUtc = nowUtc;
        Touch();
    }

    /// <summary>Khoa HSH xác nhận sổ đã gửi khi mọi ngày trong tháng đã xem xét; người gửi sổ không tự xác nhận. Xác nhận rồi sổ khóa hẳn.</summary>
    public void Confirm(Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người xác nhận sổ.");
        }

        if (Status != QcBookStatus.Submitted)
        {
            throw new DomainException(Status == QcBookStatus.Confirmed ? "Sổ đã xác nhận." : "Khoa chưa gửi sổ tháng.");
        }

        if (SubmittedByUserId == userId)
        {
            throw new DomainException("Người gửi sổ không tự xác nhận được.");
        }

        var missing = Days().Where(d => ReviewOf(d) is null).ToList();
        if (missing.Count > 0)
        {
            throw new DomainException(
                $"Còn {missing.Count} ngày chưa xem xét: {string.Join(", ", missing.Take(10).Select(d => d.Day.ToString(CultureInfo.InvariantCulture)))}"
                + (missing.Count > 10 ? "…" : "."));
        }

        Status = QcBookStatus.Confirmed;
        ConfirmedByUserId = userId;
        ConfirmedAtUtc = nowUtc;
        Touch();
    }

    /// <summary>Khoa HSH trả lại sổ đã gửi (bắt buộc lý do): khoa ghi, điều chỉnh tiếp rồi gửi lại.</summary>
    public void Return(string? reason, Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người trả lại sổ.");
        }

        if (Status != QcBookStatus.Submitted)
        {
            throw new DomainException(Status == QcBookStatus.Confirmed ? "Sổ đã xác nhận, không trả lại được." : "Khoa chưa gửi sổ tháng.");
        }

        Reopen(QcText.Required(reason, ReasonMaxLength, "Lý do trả lại"), userId, nowUtc);
        Touch();
    }

    /// <summary>Các ngày của tháng.</summary>
    public IEnumerable<DateOnly> Days()
    {
        var first = FirstDay;
        return Enumerable.Range(0, LastDay.Day).Select(first.AddDays);
    }

    private string MonthLabel => FirstDay.ToString("MM/yyyy", CultureInfo.InvariantCulture);

    private static string Day(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private QcRun Active(Guid runId) =>
        _runs.FirstOrDefault(r => r.Id == runId && r.IsActive) ?? throw new DomainException("Không tìm thấy kết quả (hoặc kết quả đã hủy).");

    private void EnsureOpen()
    {
        if (Status == QcBookStatus.Submitted)
        {
            throw new DomainException("Sổ đã gửi Khoa HSH; cần Khoa HSH trả lại sổ để ghi thêm hoặc điều chỉnh.");
        }

        if (Status == QcBookStatus.Confirmed)
        {
            throw new DomainException("Sổ đã được Khoa HSH xác nhận, không thay đổi được nữa.");
        }
    }

    private void EnsureDepartmentCanChange(DateOnly date, DateOnly today)
    {
        EnsureOpen();
        if (!Contains(date))
        {
            throw new DomainException($"Ngày thực hiện phải trong tháng {MonthLabel}.");
        }

        if (date > today)
        {
            throw new DomainException($"Chưa tới ngày {Day(date)}.");
        }

        if (ReviewOf(date) is not null)
        {
            throw new DomainException($"Khoa HSH đã xem xét ngày {Day(date)}; cần Khoa HSH trả lại ngày này để ghi điều chỉnh.");
        }
    }

    private void EnsureMeter(QcMeter meter)
    {
        ArgumentNullException.ThrowIfNull(meter);
        if (meter.Id != MeterId)
        {
            throw new DomainException("Máy không thuộc sổ này.");
        }

        if (!meter.IsActive)
        {
            throw new DomainException("Máy đã ngưng sử dụng, không ghi thêm kết quả được.");
        }

        if (meter.DepartmentId != DepartmentId)
        {
            throw new DomainException("Máy đã chuyển sang khoa khác; khoa mới mở sổ mới để ghi kết quả.");
        }
    }

    private void Reopen(string reason, Guid userId, DateTimeOffset nowUtc)
    {
        Status = QcBookStatus.Open;
        ReturnReason = reason;
        ReturnedByUserId = userId;
        ReturnedAtUtc = nowUtc;
        SubmittedByUserId = null;
        SubmittedAtUtc = null;
    }

    private void Touch() => Revision++;
}
