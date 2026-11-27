namespace Consent.Domain.QualityControl;

/// <summary>Loại vật tư nội kiểm đường huyết tại giường (quyết định 57).</summary>
public enum QcLotKind
{
    /// <summary>Que thử.</summary>
    TestStrip,

    /// <summary>Dung dịch chứng (có mức và ngưỡng chấp nhận).</summary>
    ControlSolution,
}

/// <summary>Đơn vị kết quả và ngưỡng chấp nhận của dung dịch chứng.</summary>
public enum QcUnit
{
    /// <summary>mg/dL.</summary>
    MgPerDl,

    /// <summary>mmol/L.</summary>
    MmolPerL,
}

/// <summary>Trạng thái sổ tháng: khoa ghi kết quả, gửi sổ; Khoa HSH xác nhận thì sổ khóa hẳn.</summary>
public enum QcBookStatus
{
    /// <summary>Khoa đang ghi kết quả (hoặc Khoa HSH đã trả lại sổ).</summary>
    Open,

    /// <summary>Khoa đã gửi sổ tháng, chờ Khoa HSH xác nhận.</summary>
    Submitted,

    /// <summary>Khoa HSH đã xác nhận sổ tháng: không thay đổi được nữa.</summary>
    Confirmed,
}

/// <summary>
/// Kết quả xem xét của nhân viên Khoa HSH cho một ngày: bốn lựa chọn của cột "Nhân viên Khoa HSH xem xét" trên sổ giấy (mẫu
/// CT-ĐHTG/BM01, sheet GHI CHÚ).
/// </summary>
public enum QcReviewOutcome
{
    /// <summary>Đã kiểm - ĐẠT.</summary>
    Passed,

    /// <summary>Đã kiểm - Không có QC ngày này.</summary>
    NoQc,

    /// <summary>Đã kiểm - KHÔNG ĐẠT, KHÔNG CHẠY LẠI QC.</summary>
    FailedNotRepeated,

    /// <summary>Đã kiểm - KHÔNG ĐẠT, đã chạy lại QC sau nhắc nhở.</summary>
    FailedRepeatedAfterReminder,
}
