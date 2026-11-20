using Consent.Domain.NursingReports;

namespace Consent.Application.NursingReports;

/// <summary>
/// Một dòng số liệu của báo cáo khoa. <paramref name="CarryOver"/>: báo cáo mới điền sẵn theo báo cáo đã gửi gần nhất của
/// khoa (số ít thay đổi như nhân sự, chỉ tiêu), vẫn sửa được. <paramref name="Since"/>: phiên bản mẫu thêm dòng này
/// (<see cref="NursingReportForm.FormVersion"/>); báo cáo lưu bằng mẫu cũ hơn để trống dòng này trên bảng tổng hợp, không ghi 0.
/// </summary>
public sealed record NursingReportItem(string Key, string Label, bool CarryOver = false, int Since = 1);

/// <summary>
/// Một mục (I–VI) của phần số liệu. <paramref name="ValueKey"/>: dòng tiêu đề mục cũng là ô nhập số (mục IV. ADR, bản giấy
/// không có dòng con).
/// </summary>
public sealed record NursingReportSection(string Title, IReadOnlyList<NursingReportItem> Items, string? ValueKey = null);

/// <summary>Một dòng của phần tick. <paramref name="InSummary"/>: in ở mục E của bảng tổng hợp.</summary>
public sealed record NursingReportCheck(string Key, string Label, bool InSummary = true);

/// <summary>Một cột số của bảng A (số liệu người bệnh nội trú) trên bảng tổng hợp.</summary>
public sealed record NursingSummaryColumn(string Key, string Header);

/// <summary>Một dòng "nội dung – số liệu" của mục B, C, D trên bảng tổng hợp.</summary>
public sealed record NursingSummaryLine(string Key, string Label);

/// <summary>Toàn bộ mẫu báo cáo khoa, trả cho giao diện để dựng màn hình nhập.</summary>
public sealed record NursingReportFormDto(
    string Title,
    string Category,
    IReadOnlyList<NursingReportSection> Sections,
    IReadOnlyList<NursingReportCheck> Checks,
    string IssueLabel,
    string OkLabel,
    string NotesLabel,
    int MaxValue,
    int NotesMaxLength);

/// <summary>
/// Mẫu "Báo cáo công tác điều dưỡng" của khoa lâm sàng và bảng "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày", theo bản giấy
/// bệnh viện gửi ngày 16/11/2026 (quyết định 31). Khóa (key) là tên trong JSON lưu, không đổi khi sửa nhãn; thêm dòng mới thì
/// thêm khóa mới, báo cáo cũ để trống dòng đó.
/// </summary>
public static class NursingReportForm
{
    public const string Title = "BÁO CÁO CÔNG TÁC ĐIỀU DƯỠNG";

    /// <summary>Ghi ở góc phải đầu báo cáo khoa như bản giấy.</summary>
    public const string Category = "Khoa Lâm sàng";

    public const string SummaryTitle = "TỔNG KẾT BÁO CÁO PHÒNG ĐIỀU DƯỠNG HẰNG NGÀY";

    public const string SummaryUnit = "Phòng Điều dưỡng";

    public const string CheckIssue = "issue";

    public const string CheckOk = "ok";

    public const string IssueLabel = "Có sự cố/ Không ổn";

    public const string OkLabel = "Không có sự cố/ Ổn";

    public const string NotesLabel = "Báo cáo các vấn đề bất thường (nếu có):";

    /// <summary>Số lớn nhất nhập được ở một ô.</summary>
    public const int MaxValue = 99_999;

    /// <summary>Khóa cột "Số giường thực kê" của bảng A: Phòng Điều dưỡng cài cho từng khoa, không do khoa báo cáo.</summary>
    public const string BedsColumn = "beds";

    /// <summary>Khóa cột "Số ca đi ngoại viện" của bảng A: tổng các dòng của mục V.</summary>
    public const string OutsideVisitsColumn = "outsideVisits";

    public const string AdrKey = "adr";

    /// <summary>Số ca nhập khoa từ phòng khám (PK) ngày hôm trước.</summary>
    public const string AdmissionsFromClinicKey = "admissionsFromClinic";

    /// <summary>Số ca nhập khoa do chuyển khoa (CK) ngày hôm trước.</summary>
    public const string AdmissionsByTransferKey = "admissionsByTransfer";

    /// <summary>
    /// Khóa cũ "Số ca nhập viện ngày hôm trước", trước khi tách thành nhập khoa từ PK và do CK (quyết định 38). Báo cáo đã lưu
    /// với khóa này vẫn đọc được để nhận ra là báo cáo cũ; không nhập mới được.
    /// </summary>
    public const string LegacyAdmissionsKey = "admissionsYesterday";

    /// <summary>Hai dòng tách từ "Số ca nhập viện ngày hôm trước" (quyết định 38).</summary>
    public static IReadOnlyList<string> AdmissionKeys { get; } = [AdmissionsFromClinicKey, AdmissionsByTransferKey];

    /// <summary>Số người bệnh người nước ngoài (quyết định 44).</summary>
    public const string ForeignPatientsKey = "foreignPatients";

    /// <summary>Số ca chuyển khoa (chuyển sang khoa khác) ngày hôm trước (quyết định 44).</summary>
    public const string TransfersOutKey = "transfersOutYesterday";

    /// <summary>
    /// Phiên bản mẫu báo cáo khoa, ghi kèm số liệu khi lưu (<see cref="NursingReportFigures.Version"/>). 1: mẫu ban đầu (quyết
    /// định 31, 38); 2: thêm số người bệnh người nước ngoài và số ca chuyển khoa (quyết định 44).
    /// </summary>
    public const int FormVersion = 2;

    /// <summary>Cột cuối bảng A: người gửi báo cáo của khoa (quyết định 44).</summary>
    public const string ReporterHeader = "Người báo cáo";

    /// <summary>
    /// Người báo cáo của mục C, E, F khi không khoa nào có số liệu PTTT, báo "Không ổn" hay báo cáo sự cố: mục đó tổng hợp từ báo
    /// cáo của mọi khoa đã gửi.
    /// </summary>
    public const string AllSubmittedReporters = "Các khoa đã gửi báo cáo (cột Người báo cáo, mục A)";

    /// <summary>Chỗ ký cuối bảng tổng hợp (quyết định 44).</summary>
    public const string SignatureTitle = "PHÒNG ĐIỀU DƯỠNG";

    public const string SignatureHint = "(Ký, ghi rõ họ tên)";

    public static IReadOnlyList<NursingReportSection> Sections { get; } =
    [
        new("I. NHÂN SỰ",
        [
            new("nurses", "Điều dưỡng", CarryOver: true),
            new("technicians", "KTV", CarryOver: true),
            new("medicalSecretaries", "TK YK", CarryOver: true),
            new("orderlies", "HL - YC", CarryOver: true),
        ]),
        new("II. NHÂN VIÊN TRONG NGÀY",
        [
            new("administrative", "Hành chánh"),
            new("shift1", "Trực ca 1"),
            new("shift2", "Trực ca 2"),
            new("leave", "Nghỉ phép, bù"),
            new("sick", "Ốm"),
            new("maternity", "Hậu sản"),
            new("training", "Đi học"),
            new("businessTrip", "Công tác"),
        ]),
        new("III. TÌNH HÌNH NGƯỜI BỆNH",
        [
            new("careLevel1", "NB chăm sóc cấp 1"),
            new("ventilated", "NB thở máy (Xâm nhập, không xâm nhập)"),
            new("oxygen", "NB thở oxy (Nhánh, Mask)"),
            new("monitored", "NB theo dõi bằng monitor"),
            new("chemotherapy", "NB điều trị bằng hóa trị liệu"),
            new("transfusedToday", "NB truyền CP máu (Trong ngày)"),
            new("bloodProductsToday", "Số CP máu (Trong ngày)"),
            new("syringePump", "NB sử dụng bơm tiêm tự động"),
            new("pressureUlcer", "NB bị loét do tỳ đè"),
            new("cvcCare", "Chăm sóc ống sonde TMTW"),
            new("apheresis", "NB chiết tách (BC, HT,...)"),
            new("phlebotomy", "Rút máu đa hồng cầu"),
            new("injections", "Số NB tiêm thuốc"),
            new("after16h", "NB còn sau 16h00"),
            new("after16hTt", "NB còn sau 16h00 (TT)"),
            new("after16hTm", "NB còn sau 16h00 (TM)"),
            new("transfusedOvernight", "NB truyền CP máu (Qua ngày)"),
            new("bloodBagsOvernight", "Số Túi máu (Qua ngày)"),
            new("chemicalInfusion", "NB truyền hóa chất"),
            new("cvcDressing", "NB thay băng ống sonde TMTW"),
            new("boneMarrow", "Tủy xương"),
            new("marrowBiopsy", "Sinh thiết tủy"),
            new("lumbarPuncture", "DNT"),
            new("hickman", "Đặt sonde Hickman"),
            new("btdd", "Đặt sonde BTDD"),
            new("catheterRemoval", "Rút sonde"),
            new("anesthesia", "Gây mê"),
            new("cvp", "Đặt catheter tĩnh mạch cảnh ngoài (CVP)"),
            new("paracentesis", "Chọc dịch màng bụng"),
            new("thoracentesis", "Chọc dò dịch màng phổi"),
        ]),
        new("IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)", [], ValueKey: AdrKey),
        new("V. SỐ CA ĐI NGOẠI VIỆN",
        [
            new("outsideExams", "Khám ngoại viện (CT, MRI, KCK, ...)"),
            new("consultations", "Hội chẩn"),
            new("specialistExams", "Khám chuyên khoa"),
            new("transfers", "Chuyển viện"),
        ]),
        new("VI. SỐ LƯỢNG NB",
        [
            new("quotaPatients", "Số NB theo chỉ tiêu", CarryOver: true),
            new("currentPatients", "Số NB hiện tại"),
            new(ForeignPatientsKey, "Số NB người nước ngoài", Since: 2),
            new("expectedDischarges", "Dự kiến xuất viện"),
            new("scheduledAdmissions", "Hẹn nhập viện"),
            new("newAdmissions", "Nhận mới"),
            new(AdmissionsFromClinicKey, "Số ca nhập khoa từ PK ngày hôm trước"),
            new(AdmissionsByTransferKey, "Số ca nhập khoa do CK ngày hôm trước"),
            new("dischargesYesterday", "Số ca xuất viện ngày hôm trước"),
            new(TransfersOutKey, "Số ca chuyển khoa ngày hôm trước", Since: 2),
            new("noBed", "* Chưa có giường"),
        ]),
    ];

    /// <summary>Phần tick: mỗi dòng chọn "Có sự cố/ Không ổn" hoặc "Không có sự cố/ Ổn"; bắt buộc chọn đủ khi gửi.</summary>
    public static IReadOnlyList<NursingReportCheck> Checks { get; } =
    [
        new("professional", "Vấn đề chuyên môn"),
        new("drugSupply", "Tình hình cung cấp thuốc"),
        new("hospitalFees", "Tình hình viện phí"),
        // Bảng tổng hợp giấy không có dòng này (bệnh viện chọn giữ đúng bản giấy): chỉ lưu và in ở báo cáo khoa.
        new("drugAdministration", "ĐD thực hiện thuốc", InSummary: false),
        new("patientFeedback", "Phản ánh, phê bình của NB"),
        new("security", "An ninh trật tự"),
        new("facilities", "Cơ sở vật chất"),
        new("speakers", "Tình hình loa"),
    ];

    /// <summary>Các dòng mục V: "Số ca đi ngoại viện" trên bảng tổng hợp là tổng các dòng này (giả định, ghi ở quyết định 31).</summary>
    public static IReadOnlyList<string> OutsideVisitKeys { get; } = ["outsideExams", "consultations", "specialistExams", "transfers"];

    /// <summary>Tên cột "NB PK chờ nhập viện" (người bệnh phòng khám chờ nhập viện) trên bảng tổng hợp.</summary>
    public const string WaitingAdmissionLabel = "NB PK chờ nhập viện";

    /// <summary>
    /// Cột số của bảng A, theo thứ tự bản giấy; cột "Số ca nhập viện ngày hôm trước" của bản giấy tách thành nhập khoa từ PK và
    /// do CK (quyết định 38); thêm "Người nước ngoài" sau "Số NB hiện tại" và "Chuyển khoa" sau cột xuất viện (quyết định 44).
    /// Sau các cột số là cột <see cref="ReporterHeader"/>.
    /// </summary>
    public static IReadOnlyList<NursingSummaryColumn> SummaryColumns { get; } =
    [
        new(BedsColumn, "Số giường thực kê (Không tính băng ca)"),
        new("currentPatients", "Số NB hiện tại"),
        new(ForeignPatientsKey, "Người nước ngoài"),
        new(AdmissionsFromClinicKey, "Nhập khoa từ PK"),
        new(AdmissionsByTransferKey, "Nhập khoa do CK"),
        new("dischargesYesterday", "Số ca Xuất viện ngày hôm trước"),
        new(TransfersOutKey, "Chuyển khoa"),
        new("expectedDischarges", "Dự kiến xuất viện"),
        new("scheduledAdmissions", "Hẹn nhập viện"),
        new("newAdmissions", "Dự kiến Nhận mới"),
        new(NursingHisIndicators.WaitingAdmission, WaitingAdmissionLabel),
        new("noBed", "*Chưa có giường"),
        new(AdrKey, "Phản ứng có hại thuốc ADR"),
        new(OutsideVisitsColumn, "Số ca đi ngoại viện"),
    ];

    /// <summary>Mục B (số liệu khoa khám bệnh): lấy từ HIS.</summary>
    public static IReadOnlyList<NursingSummaryLine> OutpatientLines { get; } =
    [
        new(NursingHisIndicators.OutpatientVisits, "TSNB đến khám"),
    ];

    /// <summary>
    /// Mục C (số liệu PTTT): tổng các dòng tương ứng của báo cáo khoa. Không có "Chọc dịch màng bụng" (bệnh viện chọn giữ
    /// đúng bản giấy).
    /// </summary>
    public static IReadOnlyList<NursingSummaryLine> ProcedureLines { get; } =
    [
        new("boneMarrow", "Tủy xương"),
        new("marrowBiopsy", "Sinh thiết tủy"),
        new("lumbarPuncture", "DNT"),
        new("hickman", "Đặt sonde Hickman"),
        new("btdd", "Đặt sonde BTDD"),
        new("catheterRemoval", "Rút sonde"),
        new("anesthesia", "Gây mê"),
        new("cvp", "Đặt catheter tĩnh mạch cảnh ngoài (CVP)"),
        new("thoracentesis", "Chọc dò dịch màng phổi"),
    ];

    /// <summary>Mục D (số liệu chẩn đoán hình ảnh): lấy từ HIS.</summary>
    public static IReadOnlyList<NursingSummaryLine> ImagingLines { get; } =
    [
        new(NursingHisIndicators.Ultrasound, "TS siêu âm tổng quát"),
        new(NursingHisIndicators.Echocardiography, "TS siêu âm tim"),
        new(NursingHisIndicators.Ecg, "TS đo điện tâm đồ"),
        new(NursingHisIndicators.XRay, "TS X-quang"),
        new(NursingHisIndicators.CtScan, "TS CT Scan"),
        new(NursingHisIndicators.Mri, "TS MRI"),
    ];

    /// <summary>Mọi khóa số liệu khoa nhập được (dòng con và dòng tiêu đề là ô nhập).</summary>
    public static IReadOnlySet<string> ValueKeys { get; } = new HashSet<string>(
        Sections.SelectMany(s => s.Items.Select(i => i.Key).Concat(s.ValueKey is null ? [] : [s.ValueKey])),
        StringComparer.Ordinal);

    public static IReadOnlySet<string> CheckKeys { get; } = new HashSet<string>(Checks.Select(c => c.Key), StringComparer.Ordinal);

    public static IReadOnlyList<string> CarryOverKeys { get; } =
        [.. Sections.SelectMany(s => s.Items).Where(i => i.CarryOver).Select(i => i.Key)];

    /// <summary>Phiên bản mẫu thêm dòng <paramref name="key"/> (1 với dòng có từ đầu và cột không phải dòng của báo cáo khoa).</summary>
    public static int Since(string key) => SinceVersions.GetValueOrDefault(key, 1);

    private static readonly Dictionary<string, int> SinceVersions =
        Sections.SelectMany(s => s.Items).Where(i => i.Since > 1).ToDictionary(i => i.Key, i => i.Since, StringComparer.Ordinal);

    public static NursingReportFormDto Definition { get; } = new(
        Title, Category, Sections, Checks, IssueLabel, OkLabel, NotesLabel, MaxValue, NursingDailyReport.NotesMaxLength);
}

/// <summary>
/// Chỉ số lấy từ HIS qua API thông tin người bệnh (câu truy vấn <c>PatientInfo__DailyStatisticsQuery</c> trả cột Indicator
/// bằng các mã này). <see cref="WaitingAdmission"/> tính theo khoa (cột DepartmentCode trùng mã khoa trong phần mềm phiếu),
/// các chỉ số khác tính toàn viện.
/// </summary>
public static class NursingHisIndicators
{
    /// <summary>B. Tổng số người bệnh đến khám trong ngày.</summary>
    public const string OutpatientVisits = "outpatientVisits";

    /// <summary>A. Người bệnh phòng khám chờ nhập viện của từng khoa.</summary>
    public const string WaitingAdmission = "waitingAdmission";

    public const string Ultrasound = "ultrasound";

    public const string Echocardiography = "echocardiography";

    public const string Ecg = "ecg";

    public const string XRay = "xray";

    public const string CtScan = "ct";

    public const string Mri = "mri";

    /// <summary>Chỉ số tính toàn viện (mục B và D).</summary>
    public static IReadOnlySet<string> HospitalWide { get; } = new HashSet<string>(
        [OutpatientVisits, Ultrasound, Echocardiography, Ecg, XRay, CtScan, Mri], StringComparer.Ordinal);
}
