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

    private static readonly string[] ClinicalStaff =
    [
        Permissions.PatientsRead,
        Permissions.PatientsManage,
        Permissions.TemplatesRead,
        Permissions.ConsentsRead,
        Permissions.ConsentsCreate,
        Permissions.ConsentsSign,
    ];

    public static IReadOnlyList<SystemRoleDefinition> Defaults { get; } =
    [
        new(Administrator, "Quản trị hệ thống", "Phòng CNTT: toàn quyền quản trị.", [.. PermissionCatalog.AllCodes]),
        new(TemplateManager, "Quản lý mẫu phiếu", "Phòng KHTH/QLCL: quản lý mẫu phiếu.",
            [Permissions.TemplatesRead, Permissions.TemplatesManage, Permissions.ConsentsRead, Permissions.ScopeAllDepartments]),
        new(Doctor, "Bác sĩ", "Lập phiếu, ký phiếu phía bệnh viện theo cấu hình mẫu.", ClinicalStaff),
        new(Nurse, "Điều dưỡng", "Quét CCCD, lập phiếu, ký phía bệnh viện theo cấu hình mẫu.", ClinicalStaff),
        new(Receptionist, "Tiếp đón", "Tiếp nhận, quét CCCD, lập phiếu cam kết DV/BHYT.", ClinicalStaff),
        new(DepartmentHead, "Trưởng khoa", "Xem, thống kê, xuất dữ liệu trong khoa; thu hồi phiếu đã ký.",
            [.. ClinicalStaff, Permissions.ConsentsRevoke, Permissions.ReportsRead, Permissions.ExportsRun]),
        new(Reporter, "Báo cáo / Xuất dữ liệu", "Phòng KHTH: tra cứu, xuất dữ liệu toàn viện.",
            [Permissions.PatientsRead, Permissions.TemplatesRead, Permissions.ConsentsRead, Permissions.ReportsRead,
             Permissions.ExportsRun, Permissions.ScopeAllDepartments]),
        new(Auditor, "Kiểm tra", "Thanh tra, pháp chế, QLCL: chỉ đọc phiếu và nhật ký.",
            [Permissions.AuditRead, Permissions.UsersRead, Permissions.PatientsRead, Permissions.TemplatesRead,
             Permissions.ConsentsRead, Permissions.ReportsRead, Permissions.ScopeAllDepartments]),
    ];
}
