using Consent.Domain.QualityControl;

namespace Consent.Application.QualityControl;

/// <summary>
/// Mẫu "Sổ theo dõi kết quả nội kiểm đường huyết tại giường" (quyết định 57): mã số, lần ban hành, chữ của các ô theo sổ giấy
/// bệnh viện gửi (song ngữ Việt – Anh). Bệnh viện ban hành lại mẫu thì sửa các hằng số này.
/// </summary>
public static class QcLogForm
{
    public const string Code = "CT-ĐHTG/BM01";

    public const string Issue = "03";

    public const string EffectiveDate = "16-06-2026";

    public const string Title = "SỔ THEO DÕI KẾT QUẢ NỘI KIỂM ĐƯỜNG HUYẾT TẠI GIƯỜNG";

    public const string TitleEnglish = "POCT INTERNAL QUALITY CONTROL LOG";

    public const string Pass = "Đạt/ Pass";

    public const string Fail = "Không đạt/ Fail";

    /// <summary>Chữ của lựa chọn xem xét, đúng như cột "Nhân viên Khoa HSH xem xét" của sổ giấy.</summary>
    public static string OutcomeLabel(QcReviewOutcome outcome) => outcome switch
    {
        QcReviewOutcome.Passed => "Đã kiểm - ĐẠT",
        QcReviewOutcome.NoQc => "Đã kiểm - Không có QC ngày này",
        QcReviewOutcome.FailedNotRepeated => "Đã kiểm - KHÔNG ĐẠT, KHÔNG CHẠY LẠI QC",
        QcReviewOutcome.FailedRepeatedAfterReminder => "Đã kiểm - KHÔNG ĐẠT, đã chạy lại QC sau nhắc nhở",
        _ => outcome.ToString(),
    };

    public static string Evaluation(bool passed) => passed ? Pass : Fail;

    public static string StatusLabel(QcBookStatus status) => status switch
    {
        QcBookStatus.Open => "Khoa đang ghi",
        QcBookStatus.Submitted => "Đã gửi Khoa HSH",
        QcBookStatus.Confirmed => "Khoa HSH đã xác nhận",
        _ => status.ToString(),
    };
}
