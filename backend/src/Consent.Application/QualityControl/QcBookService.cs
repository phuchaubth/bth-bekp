using System.Globalization;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.QualityControl;
using Consent.Shared;

namespace Consent.Application.QualityControl;

/// <summary>
/// Sổ theo dõi kết quả nội kiểm đường huyết tại giường (quyết định 57). Khoa lâm sàng (<see cref="Permissions.QcRecord"/>, khoa
/// của mình) ghi kết quả từng ngày, ghi nguyên nhân và chạy lại khi không đạt, ghi điều chỉnh có lý do, cuối tháng gửi sổ. Khoa
/// HSH (<see cref="Permissions.QcReview"/>, mọi khoa) xem xét từng ngày, trả lại ngày hoặc sổ, xác nhận sổ tháng, in PDF. Quy tắc
/// khóa dữ liệu nằm ở <see cref="QcBook"/>; mọi thay đổi được ghi nhật ký hệ thống.
/// </summary>
public sealed class QcBookService(
    IQualityControlRepository qc,
    IDepartmentRepository departments,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    SettingsService settings,
    IQcLogPdfRenderer pdf)
{
    /// <summary>Mở được sổ của tối đa bấy nhiêu tháng trước (ghi bù từ sổ giấy).</summary>
    public const int MaxMonthsBack = 12;

    /// <summary>Một lần xem xét tối đa bấy nhiêu ngày (cả tháng).</summary>
    public const int MaxReviewDays = 31;

    private static readonly Error Forbidden = Error.Forbidden(
        "qc.forbidden", "Khoa ghi kết quả nội kiểm của khoa mình; Khoa HSH xem xét và xác nhận sổ của mọi khoa.");

    private static readonly Error NotFound = Error.NotFound("qc.not_found", "Không tìm thấy sổ nội kiểm.");

    private bool CanRecordAny => currentUser.HasPermission(Permissions.QcRecord);

    private bool CanReview => currentUser.HasPermission(Permissions.QcReview);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>
    /// Sổ của một tháng (mặc định tháng này): Khoa HSH thấy mọi khoa, khoa thấy khoa của mình; kèm máy đang dùng chưa mở sổ tháng
    /// này. Theo khoa, rồi theo máy.
    /// </summary>
    public async Task<Result<QcBookListDto>> ListAsync(string? month, Guid? departmentId, CancellationToken cancellationToken)
    {
        if (!CanReview && !CanRecordAny)
        {
            return Forbidden;
        }

        var today = Today;
        if (ParseMonth(month ?? today.ToString("yyyy-MM", CultureInfo.InvariantCulture)) is not { } first)
        {
            return Error.ValidationField("month", "Tháng dạng yyyy-MM.");
        }

        var names = await DepartmentNamesAsync(cancellationToken).ConfigureAwait(false);
        var meters = (await qc.ListMetersAsync(cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.Id);
        var books = (await qc.ListBooksAsync(first.Year, first.Month, cancellationToken).ConfigureAwait(false))
            .Where(b => CanView(b.DepartmentId) && (departmentId is null || b.DepartmentId == departmentId))
            .ToList();
        var canOpenMonth = OpenWindowError(first, today) is null;
        var items = books.Select(b =>
            {
                var days = b.Days().ToList();
                var lastToReview = today < b.LastDay ? today : b.LastDay;
                return new QcBookSummaryDto(
                    b.Id,
                    b.MeterId,
                    meters.GetValueOrDefault(b.MeterId)?.Label ?? "Không rõ",
                    b.DepartmentId,
                    names.GetValueOrDefault(b.DepartmentId, "Không rõ"),
                    b.Status,
                    days.Count(d => b.ActiveRun(d) is not null),
                    days.Count(d => b.ActiveRun(d) is { FinalPassed: false }),
                    days.Count(d => b.ReviewOf(d) is not null),
                    days.Count(d => d <= lastToReview && b.ReviewOf(d) is null),
                    CanOpen: false);
            })
            .Concat(meters.Values
                .Where(m => m.IsActive
                            && CanView(m.DepartmentId)
                            && (departmentId is null || m.DepartmentId == departmentId)
                            && !books.Any(b => b.MeterId == m.Id && b.DepartmentId == m.DepartmentId))
                .Select(m => new QcBookSummaryDto(
                    null,
                    m.Id,
                    m.Label,
                    m.DepartmentId,
                    names.GetValueOrDefault(m.DepartmentId, "Không rõ"),
                    null,
                    0,
                    0,
                    0,
                    0,
                    canOpenMonth && (CanRecord(m.DepartmentId) || CanReview))))
            .OrderBy(i => i.DepartmentName, VietnameseComparer.Instance)
            .ThenBy(i => i.MeterLabel, VietnameseComparer.Instance)
            .ToList();
        return new QcBookListDto(first.ToString("yyyy-MM", CultureInfo.InvariantCulture), items);
    }

    /// <summary>Mở sổ tháng của một máy cho khoa đang giữ máy (đã có thì trả lại sổ đó). Khoa của máy hoặc Khoa HSH mở được.</summary>
    public async Task<Result<QcBookDto>> OpenAsync(OpenQcBookRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is not { } userId || (!CanReview && !CanRecordAny))
        {
            return Forbidden;
        }

        if (request.MeterId is not { } meterId)
        {
            return Error.ValidationField("meterId", "Chọn máy đo.");
        }

        if (await qc.GetMeterAsync(meterId, cancellationToken).ConfigureAwait(false) is not { } meter || !CanView(meter.DepartmentId))
        {
            return Error.NotFound("qc.meter_not_found", "Không tìm thấy máy đo.");
        }

        if (ParseMonth(request.Month) is not { } first)
        {
            return Error.ValidationField("month", "Tháng dạng yyyy-MM.");
        }

        if (await qc.FindBookAsync(meter.Id, meter.DepartmentId, first.Year, first.Month, cancellationToken).ConfigureAwait(false) is { } existing)
        {
            return await ToDtoAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        if (OpenWindowError(first, Today) is { } windowError)
        {
            return Error.ValidationField("month", windowError);
        }

        QcBook book;
        try
        {
            book = QcBook.Open(meter, first.Year, first.Month, userId, clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("qc.rule", ex.Message);
        }

        qc.Add(book);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(book, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<QcBookDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanReview && !CanRecordAny)
        {
            return Forbidden;
        }

        if (await qc.GetBookAsync(id, cancellationToken).ConfigureAwait(false) is not { } book || !CanView(book.DepartmentId))
        {
            return NotFound;
        }

        return await ToDtoAsync(book, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Khoa ghi kết quả của một ngày.</summary>
    public Task<Result<QcBookDto>> RecordRunAsync(Guid id, QcRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: false, async (book, userId) =>
        {
            var reading = await ReadingAsync(request, string.Empty, cancellationToken).ConfigureAwait(false);
            if (reading.IsFailure)
            {
                return reading.Error;
            }

            var meter = await qc.GetMeterAsync(book.MeterId, cancellationToken).ConfigureAwait(false);
            book.RecordRun(meter!, reading.Value, userId, Today, clock.UtcNow);
            return null;
        }, cancellationToken);
    }

    /// <summary>Khoa ghi nguyên nhân không đạt và kết quả chạy lại.</summary>
    public Task<Result<QcBookDto>> RecordFollowUpAsync(Guid id, Guid runId, QcFollowUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: false, (book, userId) =>
        {
            book.RecordFollowUp(runId, request.Reason, request.Control1RepeatValue, request.Control2RepeatValue, userId, Today, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);
    }

    /// <summary>Khoa ghi điều chỉnh kết quả ghi sai (hủy kết quả, ghi kết quả thay thế nếu có); bắt buộc lý do.</summary>
    public Task<Result<QcBookDto>> CorrectRunAsync(Guid id, Guid runId, QcCorrectionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: false, async (book, userId) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Error.ValidationField("reason", "Ghi lý do điều chỉnh.");
            }

            QcRunReading? replacement = null;
            if (request.Replacement is { } replacementRequest)
            {
                var reading = await ReadingAsync(replacementRequest, "replacement.", cancellationToken).ConfigureAwait(false);
                if (reading.IsFailure)
                {
                    return reading.Error;
                }

                replacement = reading.Value;
            }

            var meter = await qc.GetMeterAsync(book.MeterId, cancellationToken).ConfigureAwait(false);
            book.CorrectRun(runId, request.Reason, meter!, replacement, userId, Today, clock.UtcNow);
            return null;
        }, cancellationToken);
    }

    /// <summary>Khoa HSH xem xét một hoặc nhiều ngày.</summary>
    public Task<Result<QcBookDto>> ReviewDaysAsync(Guid id, QcReviewDaysRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: true, (book, userId) =>
        {
            var days = request.Days ?? [];
            if (days.Count == 0)
            {
                return Task.FromResult<Error?>(Error.ValidationField("days", "Chọn ngày cần xem xét."));
            }

            if (days.Count > MaxReviewDays)
            {
                return Task.FromResult<Error?>(Error.ValidationField("days", $"Mỗi lần xem xét tối đa {MaxReviewDays} ngày."));
            }

            var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
            for (var i = 0; i < days.Count; i++)
            {
                if (days[i].Outcome is not { } outcome || !Enum.IsDefined(outcome))
                {
                    errors[$"days[{i}].outcome"] = ["Chọn kết quả xem xét."];
                }
                else if (days[i].Note?.Trim().Length > QcDayReview.NoteMaxLength)
                {
                    errors[$"days[{i}].note"] = [$"Ghi chú tối đa {QcDayReview.NoteMaxLength} ký tự."];
                }
            }

            if (errors.Count > 0)
            {
                return Task.FromResult<Error?>(Error.Validation("validation", "Dữ liệu không hợp lệ.", errors));
            }

            book.ReviewDays([.. days.Select(d => new QcDayReviewInput(d.Date, d.Outcome!.Value, d.Note))], userId, Today, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);
    }

    /// <summary>Khoa HSH trả lại một ngày đã xem xét (bắt buộc lý do); sổ đã gửi thì sổ cũng trả lại khoa.</summary>
    public Task<Result<QcBookDto>> ReturnDayAsync(Guid id, QcReturnDayRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: true, (book, userId) =>
        {
            if (request.Date is not { } date)
            {
                return Task.FromResult<Error?>(Error.ValidationField("date", "Chọn ngày trả lại."));
            }

            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Task.FromResult<Error?>(Error.ValidationField("reason", "Ghi lý do trả lại."));
            }

            book.ReturnDay(date, request.Reason, userId, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);
    }

    /// <summary>Khoa gửi sổ tháng cho Khoa HSH (từ ngày cuối tháng).</summary>
    public Task<Result<QcBookDto>> SubmitAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, reviewer: false, (book, userId) =>
        {
            book.Submit(userId, Today, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);

    /// <summary>Khoa HSH xác nhận sổ tháng: sổ khóa hẳn.</summary>
    public Task<Result<QcBookDto>> ConfirmAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, reviewer: true, (book, userId) =>
        {
            book.Confirm(userId, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);

    /// <summary>Khoa HSH trả lại sổ đã gửi (bắt buộc lý do).</summary>
    public Task<Result<QcBookDto>> ReturnAsync(Guid id, QcReasonRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(id, reviewer: true, (book, userId) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return Task.FromResult<Error?>(Error.ValidationField("reason", "Ghi lý do trả lại."));
            }

            book.Return(request.Reason, userId, clock.UtcNow);
            return Task.FromResult<Error?>(null);
        }, cancellationToken);
    }

    /// <summary>PDF sổ theo bố cục sổ giấy (mẫu <see cref="QcLogForm.Code"/>): người xem được sổ thì in được.</summary>
    public async Task<Result<QcLogFile>> RenderPdfAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!CanReview && !CanRecordAny)
        {
            return Forbidden;
        }

        if (await qc.GetBookAsync(id, cancellationToken).ConfigureAwait(false) is not { } book || !CanView(book.DepartmentId))
        {
            return NotFound;
        }

        var dto = await ToDtoAsync(book, cancellationToken).ConfigureAwait(false);
        var hospital = await settings.ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var logo = await settings.ResolveLogoAsync(cancellationToken).ConfigureAwait(false);
        var status = book.Status switch
        {
            QcBookStatus.Confirmed => $"Khoa HSH đã xác nhận sổ lúc {Local(book.ConfirmedAtUtc!.Value)} bởi {dto.ConfirmedBy}",
            QcBookStatus.Submitted => $"Khoa đã gửi sổ lúc {Local(book.SubmittedAtUtc!.Value)} bởi {dto.SubmittedBy}, chờ Khoa HSH xác nhận",
            _ => "Khoa đang ghi, chưa gửi sổ tháng",
        };
        var content = pdf.Render(new QcLogPdfDocument(hospital, logo, dto, $"{status} (in lúc {Local(clock.UtcNow)})"));
        var department = await departments.GetByIdAsync(book.DepartmentId, cancellationToken).ConfigureAwait(false);
        var meter = await qc.GetMeterAsync(book.MeterId, cancellationToken).ConfigureAwait(false);
        return new QcLogFile(
            content,
            string.Create(
                CultureInfo.InvariantCulture,
                $"so-noi-kiem-{FileSafe(department?.Code ?? "khoa")}-{book.Year:0000}-{book.Month:00}-{FileSafe(meter?.SerialNumber ?? "may")}.pdf"));
    }

    internal static DateOnly? ParseMonth(string? month) =>
        month is not null
        && DateOnly.TryParseExact(month.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var first)
            ? first
            : null;

    private static string Local(DateTimeOffset utc) =>
        utc.ToOffset(ConsentFormService.VietnamOffset).ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Tên file chỉ giữ chữ cái, chữ số và dấu gạch (bỏ dấu tiếng Việt).</summary>
    private static string FileSafe(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (c is 'đ' or 'Đ')
            {
                builder.Append('d');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var safe = builder.ToString().Trim('-');
        return safe.Length == 0 ? "x" : safe;
    }

    /// <summary>Tháng mở được sổ: không phải tháng sau, không quá <see cref="MaxMonthsBack"/> tháng trước.</summary>
    private static string? OpenWindowError(DateOnly first, DateOnly today)
    {
        var current = new DateOnly(today.Year, today.Month, 1);
        if (first > current)
        {
            return "Chưa tới tháng này.";
        }

        return first < current.AddMonths(-MaxMonthsBack) ? $"Chỉ mở sổ của {MaxMonthsBack} tháng gần đây." : null;
    }

    private bool CanRecord(Guid departmentId) =>
        CanRecordAny
        && (currentUser.HasPermission(Permissions.ScopeAllDepartments) || currentUser.DepartmentIds.Contains(departmentId));

    private bool CanView(Guid departmentId) => CanReview || CanRecord(departmentId);

    /// <summary>
    /// Nạp sổ, kiểm tra quyền (khoa của sổ hoặc Khoa HSH), áp dụng thay đổi rồi lưu. Vi phạm quy tắc của sổ trả lỗi 422 với lời
    /// nhắn của quy tắc; hai người cùng lúc thay đổi một sổ thì người sau nhận lỗi xung đột (409), tải lại rồi làm lại.
    /// </summary>
    private async Task<Result<QcBookDto>> ChangeAsync(
        Guid id, bool reviewer, Func<QcBook, Guid, Task<Error?>> change, CancellationToken cancellationToken)
    {
        if (!CanReview && !CanRecordAny)
        {
            return Forbidden;
        }

        if (await qc.GetBookAsync(id, cancellationToken).ConfigureAwait(false) is not { } book || !CanView(book.DepartmentId))
        {
            return NotFound;
        }

        if (currentUser.UserId is not { } userId || (reviewer ? !CanReview : !CanRecord(book.DepartmentId)))
        {
            return Forbidden;
        }

        try
        {
            if (await change(book, userId).ConfigureAwait(false) is { } error)
            {
                return error;
            }
        }
        catch (DomainException ex)
        {
            return Error.BusinessRule("qc.rule", ex.Message);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await ToDtoAsync(book, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Kiểm tra đủ trường và tìm lô; quy tắc lô (loại, đang dùng, hạn dùng, mức) do <see cref="QcRun"/> kiểm tra.</summary>
    private async Task<Result<QcRunReading>> ReadingAsync(QcRunRequest request, string prefix, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Require(bool present, string field, string message)
        {
            if (!present)
            {
                errors[prefix + field] = [message];
            }
        }

        Require(request.RunDate is not null, "runDate", "Chọn ngày thực hiện.");
        Require(request.StripOpenedOn is not null, "stripOpenedOn", "Nhập ngày mở hộp que thử.");
        Require(request.Control1OpenedOn is not null, "control1OpenedOn", "Nhập ngày mở lọ dung dịch chứng.");
        Require(request.Control2OpenedOn is not null, "control2OpenedOn", "Nhập ngày mở lọ dung dịch chứng.");
        Require(request.Control1Value is not null, "control1Value", "Nhập kết quả.");
        Require(request.Control2Value is not null, "control2Value", "Nhập kết quả.");
        var strip = request.StripLotId is { } stripId ? await qc.GetLotAsync(stripId, cancellationToken).ConfigureAwait(false) : null;
        var control1 = request.Control1LotId is { } control1Id ? await qc.GetLotAsync(control1Id, cancellationToken).ConfigureAwait(false) : null;
        var control2 = request.Control2LotId is { } control2Id ? await qc.GetLotAsync(control2Id, cancellationToken).ConfigureAwait(false) : null;
        Require(strip is not null, "stripLotId", "Chọn lô que thử.");
        Require(control1 is not null, "control1LotId", "Chọn lô dung dịch chứng.");
        Require(control2 is not null, "control2LotId", "Chọn lô dung dịch chứng.");
        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        return new QcRunReading(
            request.RunDate!.Value,
            strip!,
            request.StripOpenedOn!.Value,
            new QcControlReading(control1!, request.Control1OpenedOn!.Value, request.Control1Value!.Value),
            new QcControlReading(control2!, request.Control2OpenedOn!.Value, request.Control2Value!.Value));
    }

    private async Task<IReadOnlyDictionary<Guid, string>> DepartmentNamesAsync(CancellationToken cancellationToken) =>
        (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Name);

    private async Task<QcBookDto> ToDtoAsync(QcBook book, CancellationToken cancellationToken)
    {
        var meter = await qc.GetMeterAsync(book.MeterId, cancellationToken).ConfigureAwait(false);
        var department = await departments.GetByIdAsync(book.DepartmentId, cancellationToken).ConfigureAwait(false);
        var people = new Dictionary<Guid, string>();
        foreach (var id in PeopleOf(book).Distinct())
        {
            people[id] = (await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false))?.FullName ?? "Không rõ";
        }

        string? Name(Guid? id) => id is { } value ? people.GetValueOrDefault(value, "Không rõ") : null;

        var today = Today;
        var userId = currentUser.UserId;
        var open = book.Status == QcBookStatus.Open;
        var canRecord = open && CanRecord(book.DepartmentId) && meter is { IsActive: true } && meter.DepartmentId == book.DepartmentId;
        var canReview = CanReview && book.Status != QcBookStatus.Confirmed;
        var allReviewed = book.Days().All(d => book.ReviewOf(d) is not null);
        var actions = new QcBookActionsDto(
            Record: canRecord,
            Submit: open && CanRecord(book.DepartmentId) && today >= book.LastDay,
            Review: canReview,
            Confirm: CanReview && book.Status == QcBookStatus.Submitted && allReviewed && book.SubmittedByUserId != userId,
            Return: CanReview && book.Status == QcBookStatus.Submitted);

        QcRunDto Run(QcRun run, bool dayReviewed) => new(
            run.Id,
            run.RunDate,
            new QcStripDto(run.StripLotId, run.StripLotNumber, run.StripExpiryDate, run.StripOpenedOn),
            new QcControlDto(
                run.Control1LotId, run.Control1Level, run.Control1LotNumber, run.Control1ExpiryDate, run.Control1OpenedOn,
                run.Control1Low, run.Control1High, run.Control1Unit, run.Control1Value, run.Control1Passed, run.Control1RepeatValue,
                run.Control1RepeatPassed),
            new QcControlDto(
                run.Control2LotId, run.Control2Level, run.Control2LotNumber, run.Control2ExpiryDate, run.Control2OpenedOn,
                run.Control2Low, run.Control2High, run.Control2Unit, run.Control2Value, run.Control2Passed, run.Control2RepeatValue,
                run.Control2RepeatPassed),
            run.Passed,
            run.FailureReason,
            run.HasRepeat,
            run.RepeatPassed,
            Name(run.FollowUpByUserId),
            run.FollowUpAtUtc,
            Name(run.PerformedByUserId)!,
            run.RecordedAtUtc,
            run.IsLate,
            run.ReplacesRunId,
            run.CancelReason,
            Name(run.CancelledByUserId),
            run.CancelledAtUtc,
            CanFollowUp: run.IsActive && canRecord && !dayReviewed && !run.Passed && !run.HasFollowUp,
            CanCorrect: run.IsActive && canRecord && !dayReviewed);

        QcReviewDto Review(QcDayReview review) => new(
            review.Id, review.Outcome, review.Note, Name(review.ReviewedByUserId)!, review.ReviewedAtUtc, review.ReturnReason,
            Name(review.ReturnedByUserId), review.ReturnedAtUtc);

        var days = book.Days().Select(date =>
        {
            var run = book.ActiveRun(date);
            var review = book.ReviewOf(date);
            var reviewed = review is not null;
            return new QcDayDto(
                date,
                run is null ? null : Run(run, reviewed),
                [.. book.Runs.Where(r => r.RunDate == date && !r.IsActive).OrderBy(r => r.RecordedAtUtc).Select(r => Run(r, reviewed))],
                review is null ? null : Review(review),
                [.. book.Reviews.Where(r => r.Date == date && !r.IsEffective).OrderBy(r => r.ReviewedAtUtc).Select(Review)],
                book.SuggestOutcome(date),
                [.. Enum.GetValues<QcReviewOutcome>().Where(o => book.Allows(date, o))],
                CanRecord: canRecord && date <= today && !reviewed && run is null,
                CanReview: canReview && date <= today && !reviewed
                           && (run is null || (run.PerformedByUserId != userId && run.FollowUpByUserId != userId)),
                CanReturn: canReview && reviewed);
        }).ToList();

        var active = book.Runs.Where(r => r.IsActive).OrderBy(r => r.RunDate).ToList();
        var header = new QcBookHeaderDto(
            [.. active.DistinctBy(r => (r.StripLotId, r.StripOpenedOn))
                .Select(r => new QcLotUsageDto(r.StripLotNumber, r.StripExpiryDate, r.StripOpenedOn, null, null, null, null))],
            [.. active.DistinctBy(r => (r.Control1LotId, r.Control1OpenedOn, r.Control1Low, r.Control1High))
                .Select(r => new QcLotUsageDto(
                    r.Control1LotNumber, r.Control1ExpiryDate, r.Control1OpenedOn, r.Control1Level, r.Control1Low, r.Control1High, r.Control1Unit))],
            [.. active.DistinctBy(r => (r.Control2LotId, r.Control2OpenedOn, r.Control2Low, r.Control2High))
                .Select(r => new QcLotUsageDto(
                    r.Control2LotNumber, r.Control2ExpiryDate, r.Control2OpenedOn, r.Control2Level, r.Control2Low, r.Control2High, r.Control2Unit))]);

        IReadOnlyList<QcLotDto> lots = [];
        QcRunDefaultsDto? defaults = null;
        if (canRecord)
        {
            var usable = (await qc.ListLotsAsync(cancellationToken).ConfigureAwait(false))
                .Where(l => l.IsActive && l.ExpiryDate >= book.FirstDay)
                .OrderBy(l => l.Kind)
                .ThenBy(l => l.Level)
                .ThenBy(l => l.ExpiryDate)
                .ThenBy(l => l.LotNumber, StringComparer.Ordinal)
                .ToList();
            lots = [.. usable.Select(QcCatalogService.ToDto)];
            var ids = usable.Select(l => l.Id).ToHashSet();
            if (await qc.FindLatestRunAsync(book.MeterId, cancellationToken).ConfigureAwait(false) is { } latest)
            {
                defaults = new QcRunDefaultsDto(
                    ids.Contains(latest.StripLotId) ? latest.StripLotId : null,
                    ids.Contains(latest.StripLotId) ? latest.StripOpenedOn : null,
                    ids.Contains(latest.Control1LotId) ? latest.Control1LotId : null,
                    ids.Contains(latest.Control1LotId) ? latest.Control1OpenedOn : null,
                    ids.Contains(latest.Control2LotId) ? latest.Control2LotId : null,
                    ids.Contains(latest.Control2LotId) ? latest.Control2OpenedOn : null);
            }
        }

        return new QcBookDto(
            book.Id,
            book.MeterId,
            meter?.Label ?? "Không rõ",
            book.DepartmentId,
            department?.Name ?? "Không rõ",
            book.Year,
            book.Month,
            book.Status,
            Name(book.SubmittedByUserId),
            book.SubmittedAtUtc,
            Name(book.ConfirmedByUserId),
            book.ConfirmedAtUtc,
            book.ReturnReason,
            Name(book.ReturnedByUserId),
            book.ReturnedAtUtc,
            header,
            days,
            lots,
            defaults,
            actions);
    }

    private static IEnumerable<Guid> PeopleOf(QcBook book)
    {
        foreach (var id in new[] { book.SubmittedByUserId, book.ConfirmedByUserId, book.ReturnedByUserId })
        {
            if (id is { } value)
            {
                yield return value;
            }
        }

        foreach (var run in book.Runs)
        {
            yield return run.PerformedByUserId;
            foreach (var id in new[] { run.FollowUpByUserId, run.CancelledByUserId })
            {
                if (id is { } value)
                {
                    yield return value;
                }
            }
        }

        foreach (var review in book.Reviews)
        {
            yield return review.ReviewedByUserId;
            if (review.ReturnedByUserId is { } returnedBy)
            {
                yield return returnedBy;
            }
        }
    }
}
