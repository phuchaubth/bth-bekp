using Consent.Domain.Authorization;

namespace Consent.Domain.Identity;

/// <summary>Định nghĩa vai trò mặc định được tạo khi khởi tạo hệ thống.</summary>
public sealed record SystemRoleDefinition(string Code, string Name, string Description, IReadOnlyList<string> Permissions);

public static class SystemRoles
{
    public const string Administrator = "ADMIN";
    public const string TemplateManager = "TEMPLATE_MANAGER";
    public const string Doctor = "DOCTOR";
    public const string Nurse = "NURSE";
    public const string Receptionist = "RECEPTIONIST";
    public const string DepartmentHead = "DEPARTMENT_HEAD";
    public const string Reporter = "REPORTER";
    public const string Auditor = "AUDITOR";

    /// <summary>Phòng Điều dưỡng (quyết định 31): tổng hợp báo cáo điều dưỡng hằng ngày của các khoa.</summary>
    public const string NursingOffice = "NURSING_OFFICE";

    /// <summary>Khoa Huyết học – Sinh hóa (quyết định 57): xem xét kết quả nội kiểm đường huyết của các khoa, khai báo máy đo và lô.</summary>
    public const string LabQualityControl = "LAB_QC";

    /// <summary>Khoa cận lâm sàng (quyết định 60): ghi và báo kết quả nguy cấp của khoa.</summary>
    public const string CriticalLab = "CRITICAL_LAB";

    /// <summary>QLCL (quyết định 60): danh mục, thông số, sổ và dashboard kết quả nguy cấp mọi khoa.</summary>
    public const string CriticalQuality = "CRITICAL_QA";

    /// <summary>Tài khoản của màn hình theo dõi kết quả nguy cấp đặt tại khoa (TV): chỉ xem, không thao tác.</summary>
    public const string CriticalDisplay = "CRITICAL_DISPLAY";

    /// <summary>
    /// Máy kiosk cho người bệnh tự làm phiếu (quyết định 24). Chỉ có đúng quyền dùng kiosk (không sửa được), và tài khoản
    /// mang vai trò này không được có vai trò khác: máy đặt ở nơi công cộng không được xem dữ liệu nào khác.
    /// </summary>
    public const string Kiosk = "KIOSK";

    private static readonly string[] ClinicalStaff =
    [
        Permissions.PatientsRead,
        Permissions.PatientsManage,
        Permissions.TemplatesRead,
        Permissions.ConsentsRead,
        Permissions.ConsentsCreate,
        Permissions.ConsentsSign,
    ];

    /// <summary>Quyền cố định của vai trò Kiosk (khai báo trước <see cref="Defaults"/> vì khởi tạo tĩnh chạy theo thứ tự).</summary>
    public static IReadOnlyList<string> KioskPermissions { get; } = [Permissions.KioskUse];

    public static IReadOnlyList<SystemRoleDefinition> Defaults { get; } =
    [
        new(Administrator, "Quản trị hệ thống", "Phòng CNTT: toàn quyền quản trị.", [.. PermissionCatalog.AllCodes]),
        new(TemplateManager, "Quản lý mẫu phiếu", "Phòng KHTH/QLCL: quản lý mẫu phiếu.",
            [Permissions.TemplatesRead, Permissions.TemplatesManage, Permissions.ConsentsRead, Permissions.ScopeAllDepartments]),
        new(Doctor, "Bác sĩ", "Lập phiếu, ký phiếu phía bệnh viện theo cấu hình mẫu; nhận và ghi hướng xử trí kết quả nguy cấp gửi đến khoa.",
            [.. ClinicalStaff, Permissions.CriticalReceive, Permissions.CriticalTreat]),
        new(Nurse, "Điều dưỡng", "Quét CCCD, lập phiếu, ký phía bệnh viện theo cấu hình mẫu; lập và gửi báo cáo điều dưỡng của khoa; ghi kết quả nội kiểm đường huyết của khoa; xác nhận đã nhận kết quả nguy cấp gửi đến khoa.",
            [.. ClinicalStaff, Permissions.NursingReportsSubmit, Permissions.QcRecord, Permissions.CriticalReceive]),
        new(Receptionist, "Tiếp đón", "Tiếp nhận, quét CCCD, lập phiếu cam kết DV/BHYT.", ClinicalStaff),
        new(DepartmentHead, "Trưởng khoa", "Xem, thống kê, xuất dữ liệu trong khoa; thu hồi phiếu đã ký.",
            [.. ClinicalStaff, Permissions.ConsentsRevoke, Permissions.ReportsRead, Permissions.ExportsRun]),
        new(Reporter, "Báo cáo / Xuất dữ liệu", "Phòng KHTH: tra cứu, xuất dữ liệu toàn viện.",
            [Permissions.PatientsRead, Permissions.TemplatesRead, Permissions.ConsentsRead, Permissions.ReportsRead,
             Permissions.ExportsRun, Permissions.ScopeAllDepartments]),
        new(Auditor, "Kiểm tra", "Thanh tra, pháp chế, QLCL: chỉ đọc phiếu và nhật ký.",
            [Permissions.AuditRead, Permissions.UsersRead, Permissions.PatientsRead, Permissions.TemplatesRead,
             Permissions.ConsentsRead, Permissions.ReportsRead, Permissions.ScopeAllDepartments]),
        new(NursingOffice, "Phòng Điều dưỡng", "Tổng hợp báo cáo điều dưỡng hằng ngày của các khoa; mở lại báo cáo, cài khoa báo cáo và số giường.",
            [Permissions.NursingReportsRead, Permissions.NursingReportsManage]),
        new(LabQualityControl, "Khoa HSH (nội kiểm)", "Khoa Huyết học – Sinh hóa: xem xét kết quả nội kiểm đường huyết của các khoa, xác nhận sổ tháng; khai báo máy đo, lô que thử, lô dung dịch chứng.",
            [Permissions.QcReview, Permissions.QcManage]),
        new(CriticalLab, "Khoa cận lâm sàng (KQNC)", "Khoa xét nghiệm, vi sinh, giải phẫu bệnh, chẩn đoán hình ảnh: ghi kết quả nguy cấp và lần báo cho khoa điều trị, in sổ của khoa.",
            [Permissions.CriticalRecord]),
        new(CriticalQuality, "QLCL (kết quả nguy cấp)", "Phòng Quản lý chất lượng: danh mục, thông số vận hành, sổ và dashboard kết quả nguy cấp mọi khoa.",
            [Permissions.CriticalMonitor, Permissions.CriticalManage]),
        new(CriticalDisplay, "Màn hình theo dõi KQNC", "Tài khoản của màn hình đặt tại khoa: chỉ xem kết quả nguy cấp gửi đến khoa, không thao tác.",
            [Permissions.CriticalDisplay]),
        new(Kiosk, "Kiosk người bệnh", "Máy kiosk cho người bệnh tự làm phiếu: chỉ dùng được màn hình kiosk.", KioskPermissions),
    ];
}
