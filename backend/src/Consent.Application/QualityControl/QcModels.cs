using Consent.Application.ConsentForms;
using Consent.Application.Settings;
using Consent.Domain.QualityControl;

namespace Consent.Application.QualityControl;

/// <summary>Máy đo đường huyết trong danh mục của Khoa HSH.</summary>
public sealed record QcMeterDto(
    Guid Id, Guid DepartmentId, string DepartmentName, string Model, string SerialNumber, bool IsActive, DateTimeOffset CreatedAtUtc);

/// <summary>Thêm hoặc sửa máy đo; thêm mới thì bỏ qua <see cref="IsActive"/> (máy mới luôn đang dùng).</summary>
public sealed record SaveQcMeterRequest(Guid? DepartmentId, string? Model, string? SerialNumber, bool? IsActive);

/// <summary>Lô que thử hoặc dung dịch chứng; que thử không có mức, ngưỡng, đơn vị.</summary>
public sealed record QcLotDto(
    Guid Id,
    QcLotKind Kind,
    string ProductName,
    string LotNumber,
    DateOnly ExpiryDate,
    int? Level,
    decimal? RangeLow,
    decimal? RangeHigh,
    QcUnit? Unit,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

/// <summary>Thêm hoặc sửa lô; sửa thì không đổi được loại vật tư.</summary>
public sealed record SaveQcLotRequest(
    QcLotKind? Kind,
    string? ProductName,
    string? LotNumber,
    DateOnly? ExpiryDate,
    int? Level,
    decimal? RangeLow,
    decimal? RangeHigh,
    QcUnit? Unit,
    bool? IsActive);

/// <summary>Danh mục nội kiểm: máy đo và lô (kể cả ngưng sử dụng).</summary>
public sealed record QcCatalogDto(IReadOnlyList<QcMeterDto> Meters, IReadOnlyList<QcLotDto> Lots);

/// <summary>
/// Một dòng của danh sách sổ tháng: sổ đã mở, hoặc máy đang dùng của khoa chưa mở sổ tháng này (<see cref="BookId"/> trống).
/// <see cref="DaysToReview"/>: số ngày đến hôm nay (hoặc cả tháng nếu tháng đã qua) Khoa HSH chưa xem xét.
/// </summary>
public sealed record QcBookSummaryDto(
    Guid? BookId,
    Guid MeterId,
    string MeterLabel,
    Guid DepartmentId,
    string DepartmentName,
    QcBookStatus? Status,
    int RecordedDays,
    int FailedDays,
    int ReviewedDays,
    int DaysToReview,
    bool CanOpen);

/// <summary>Danh sách sổ của một tháng (tháng dạng <c>yyyy-MM</c>).</summary>
public sealed record QcBookListDto(string Month, IReadOnlyList<QcBookSummaryDto> Items);

/// <summary>Que thử dùng cho một kết quả.</summary>
public sealed record QcStripDto(Guid LotId, string LotNumber, DateOnly ExpiryDate, DateOnly OpenedOn);

/// <summary>Kết quả một mức dung dịch chứng: lô, ngày mở, ngưỡng chấp nhận lúc ghi, kết quả và đánh giá, kết quả chạy lại.</summary>
public sealed record QcControlDto(
    Guid LotId,
    int Level,
    string LotNumber,
    DateOnly ExpiryDate,
    DateOnly OpenedOn,
    decimal Low,
    decimal High,
    QcUnit Unit,
    decimal Value,
    bool Passed,
    decimal? RepeatValue,
    bool? RepeatPassed);

/// <summary>
/// Một kết quả của sổ. <see cref="Late"/>: ghi sau ngày thực hiện. Kết quả đã hủy có lý do, người hủy và (nếu có) kết quả thay
/// thế. <see cref="CanFollowUp"/>, <see cref="CanCorrect"/>: người xem ghi được nguyên nhân và chạy lại, ghi điều chỉnh.
/// </summary>
public sealed record QcRunDto(
    Guid Id,
    DateOnly RunDate,
    QcStripDto Strip,
    QcControlDto Control1,
    QcControlDto Control2,
    bool Passed,
    string? FailureReason,
    bool HasRepeat,
    bool? RepeatPassed,
    string? FollowUpBy,
    DateTimeOffset? FollowUpAtUtc,
    string PerformedBy,
    DateTimeOffset RecordedAtUtc,
    bool Late,
    Guid? ReplacesRunId,
    string? CancelReason,
    string? CancelledBy,
    DateTimeOffset? CancelledAtUtc,
    bool CanFollowUp,
    bool CanCorrect);

/// <summary>Một lần Khoa HSH xem xét một ngày (lần bị trả lại có lý do, người trả lại).</summary>
public sealed record QcReviewDto(
    Guid Id,
    QcReviewOutcome Outcome,
    string? Note,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    string? ReturnReason,
    string? ReturnedBy,
    DateTimeOffset? ReturnedAtUtc);

/// <summary>
/// Một ngày của sổ: kết quả đang có hiệu lực, kết quả đã hủy (in gạch ngang), lần xem xét đang có hiệu lực và các lần đã trả lại;
/// gợi ý và các lựa chọn xem xét khớp dữ liệu; người xem ghi được kết quả, xem xét, trả lại ngày này không.
/// </summary>
public sealed record QcDayDto(
    DateOnly Date,
    QcRunDto? Run,
    IReadOnlyList<QcRunDto> CancelledRuns,
    QcReviewDto? Review,
    IReadOnlyList<QcReviewDto> ReturnedReviews,
    QcReviewOutcome SuggestedOutcome,
    IReadOnlyList<QcReviewOutcome> AllowedOutcomes,
    bool CanRecord,
    bool CanReview,
    bool CanReturn);

/// <summary>Một lô dùng trong tháng, ghi ở phần đầu sổ như sổ giấy (que thử không có mức, ngưỡng).</summary>
public sealed record QcLotUsageDto(
    string LotNumber, DateOnly ExpiryDate, DateOnly OpenedOn, int? Level, decimal? Low, decimal? High, QcUnit? Unit);

/// <summary>Phần đầu sổ: que thử, dung dịch chứng hai mức đã dùng trong tháng (theo kết quả còn hiệu lực).</summary>
public sealed record QcBookHeaderDto(
    IReadOnlyList<QcLotUsageDto> Strips, IReadOnlyList<QcLotUsageDto> Controls1, IReadOnlyList<QcLotUsageDto> Controls2);

/// <summary>Lô đang dùng gợi ý khi ghi kết quả mới: theo kết quả ghi gần nhất của máy (lô còn đang sử dụng).</summary>
public sealed record QcRunDefaultsDto(
    Guid? StripLotId,
    DateOnly? StripOpenedOn,
    Guid? Control1LotId,
    DateOnly? Control1OpenedOn,
    Guid? Control2LotId,
    DateOnly? Control2OpenedOn);

/// <summary>Người xem làm được gì với sổ.</summary>
public sealed record QcBookActionsDto(bool Record, bool Submit, bool Review, bool Confirm, bool Return);

/// <summary>Sổ tháng đầy đủ: thông tin sổ, phần đầu sổ, từng ngày, lô chọn được khi ghi kết quả.</summary>
public sealed record QcBookDto(
    Guid Id,
    Guid MeterId,
    string MeterLabel,
    Guid DepartmentId,
    string DepartmentName,
    int Year,
    int Month,
    QcBookStatus Status,
    string? SubmittedBy,
    DateTimeOffset? SubmittedAtUtc,
    string? ConfirmedBy,
    DateTimeOffset? ConfirmedAtUtc,
    string? ReturnReason,
    string? ReturnedBy,
    DateTimeOffset? ReturnedAtUtc,
    QcBookHeaderDto Header,
    IReadOnlyList<QcDayDto> Days,
    IReadOnlyList<QcLotDto> AvailableLots,
    QcRunDefaultsDto? Defaults,
    QcBookActionsDto Actions);

/// <summary>Mở sổ tháng của một máy (tháng dạng <c>yyyy-MM</c>); sổ đã có thì trả lại sổ đó.</summary>
public sealed record OpenQcBookRequest(Guid? MeterId, string? Month);

/// <summary>Kết quả nội kiểm của một ngày: lô que thử và ngày mở hộp, hai mức dung dịch chứng (mức thấp ở cột đầu).</summary>
public sealed record QcRunRequest(
    DateOnly? RunDate,
    Guid? StripLotId,
    DateOnly? StripOpenedOn,
    Guid? Control1LotId,
    DateOnly? Control1OpenedOn,
    decimal? Control1Value,
    Guid? Control2LotId,
    DateOnly? Control2OpenedOn,
    decimal? Control2Value);

/// <summary>Nguyên nhân không đạt (bắt buộc) và kết quả chạy lại hai mức (bỏ trống cả hai nếu không chạy lại).</summary>
public sealed record QcFollowUpRequest(string? Reason, decimal? Control1RepeatValue, decimal? Control2RepeatValue);

/// <summary>Ghi điều chỉnh: lý do (bắt buộc) và kết quả thay thế; không có kết quả thay thế là chỉ hủy kết quả ghi sai.</summary>
public sealed record QcCorrectionRequest(string? Reason, QcRunRequest? Replacement);

public sealed record QcReviewDayRequest(DateOnly Date, QcReviewOutcome? Outcome, string? Note);

/// <summary>Khoa HSH xem xét một hoặc nhiều ngày.</summary>
public sealed record QcReviewDaysRequest(IReadOnlyList<QcReviewDayRequest>? Days);

public sealed record QcReturnDayRequest(DateOnly? Date, string? Reason);

public sealed record QcReasonRequest(string? Reason);

/// <summary>Dữ liệu dựng PDF sổ theo mẫu giấy. <paramref name="PrintedLine"/>: dòng nhỏ cuối trang (in lúc, trạng thái sổ).</summary>
public sealed record QcLogPdfDocument(HospitalOptions Hospital, LogoImage? Logo, QcBookDto Book, string PrintedLine);

/// <summary>In PDF sổ theo dõi kết quả nội kiểm theo bố cục sổ giấy (mẫu <see cref="QcLogForm.Code"/>).</summary>
public interface IQcLogPdfRenderer
{
    byte[] Render(QcLogPdfDocument document);
}

/// <summary>File PDF sổ.</summary>
public sealed record QcLogFile(byte[] Content, string FileName);
