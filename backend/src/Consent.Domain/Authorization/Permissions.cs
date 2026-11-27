namespace Consent.Domain.Authorization;

/// <summary>Mã quyền (permission) dùng trong toàn hệ thống. Quyền được gán cho vai trò.</summary>
public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesManage = "roles.manage";
    public const string DepartmentsManage = "departments.manage";
    public const string AuditRead = "audit.read";
    public const string SettingsManage = "settings.manage";

    public const string PatientsRead = "patients.read";
    public const string PatientsManage = "patients.manage";

    public const string TemplatesRead = "templates.read";
    public const string TemplatesManage = "templates.manage";

    public const string ConsentsRead = "consents.read";
    public const string ConsentsCreate = "consents.create";
    public const string ConsentsSign = "consents.sign";
    public const string ConsentsRevoke = "consents.revoke";

    /// <summary>
    /// Dùng màn hình kiosk cho người bệnh tự làm phiếu (quyết định 24): chỉ nhận diện người bệnh, lập và ký bộ phiếu của
    /// chính người đó; không xem được danh sách hay phiếu khác.
    /// </summary>
    public const string KioskUse = "kiosk.use";

    public const string ReportsRead = "reports.read";
    public const string ExportsRun = "exports.run";
    public const string ExportsSensitive = "exports.sensitive";

    /// <summary>
    /// Lập, sửa và gửi báo cáo điều dưỡng hằng ngày của khoa mình (quyết định 31); có thêm
    /// <see cref="ScopeAllDepartments"/> thì lập được cho mọi khoa.
    /// </summary>
    public const string NursingReportsSubmit = "nursingreports.submit";

    /// <summary>Xem báo cáo điều dưỡng của mọi khoa và bảng tổng hợp hằng ngày, in PDF, xuất Excel.</summary>
    public const string NursingReportsRead = "nursingreports.read";

    /// <summary>Phòng Điều dưỡng: mở lại báo cáo đã gửi, cài khoa báo cáo và số giường, lấy hoặc nhập số liệu HIS.</summary>
    public const string NursingReportsManage = "nursingreports.manage";

    /// <summary>Xem dữ liệu của mọi khoa (không giới hạn theo khoa của người dùng).</summary>
    public const string ScopeAllDepartments = "scope.all_departments";

    /// <summary>
    /// Phân công kiểm thử tính năng mới cho từng người, có thời gian; xem, ghi hướng xử lý và xuất báo cáo kiểm thử (quyết định 49).
    /// Người được phân công tự xem và báo cáo việc của mình, không cần quyền này.
    /// </summary>
    public const string TestingManage = "testing.manage";

    /// <summary>
    /// Bật/tắt chế độ bảo trì (quyết định 53) và vẫn đăng nhập, thao tác được khi đang bảo trì để điều chỉnh cấu hình, mẫu phiếu,
    /// dữ liệu. Người không có quyền này (kể cả kiosk, người bệnh ký qua mã QR) không đăng nhập, không thao tác được khi đang bảo trì.
    /// </summary>
    public const string SystemMaintenance = "system.maintenance";

    /// <summary>
    /// Khoa lâm sàng ghi kết quả nội kiểm đường huyết tại giường vào sổ của khoa mình, ghi điều chỉnh, gửi sổ tháng (quyết định 57);
    /// có thêm <see cref="ScopeAllDepartments"/> thì ghi được cho mọi khoa.
    /// </summary>
    public const string QcRecord = "qc.record";

    /// <summary>Khoa HSH: xem sổ nội kiểm của mọi khoa, xem xét từng ngày, trả lại ngày hoặc sổ, xác nhận sổ tháng, in PDF.</summary>
    public const string QcReview = "qc.review";

    /// <summary>Khoa HSH: khai báo máy đo đường huyết của các khoa, lô que thử, lô dung dịch chứng và ngưỡng chấp nhận.</summary>
    public const string QcManage = "qc.manage";
}

/// <summary>Mô tả một quyền để hiển thị trên ma trận phân quyền.</summary>
public sealed record PermissionDefinition(string Code, string Name, string Group);

/// <summary>Danh mục quyền đầy đủ, kèm tên tiếng Việt và nhóm.</summary>
public static class PermissionCatalog
{
    private const string SystemGroup = "Hệ thống";
    private const string PatientGroup = "Người bệnh";
    private const string TemplateGroup = "Mẫu phiếu";
    private const string ConsentGroup = "Phiếu đồng thuận";
    private const string ReportGroup = "Báo cáo và xuất dữ liệu";
    private const string NursingGroup = "Báo cáo điều dưỡng";
    private const string ScopeGroup = "Phạm vi dữ liệu";
    private const string TestingGroup = "Kiểm thử";
    private const string QcGroup = "Nội kiểm đường huyết";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Permissions.UsersRead, "Xem người dùng", SystemGroup),
        new(Permissions.UsersManage, "Quản lý người dùng", SystemGroup),
        new(Permissions.RolesManage, "Quản lý vai trò và phân quyền", SystemGroup),
        new(Permissions.DepartmentsManage, "Quản lý khoa/phòng", SystemGroup),
        new(Permissions.AuditRead, "Xem nhật ký hệ thống", SystemGroup),
        new(Permissions.SettingsManage, "Cấu hình hệ thống", SystemGroup),
        new(Permissions.SystemMaintenance, "Bảo trì hệ thống: bật/tắt, vào hệ thống khi đang bảo trì", SystemGroup),
        new(Permissions.PatientsRead, "Xem người bệnh", PatientGroup),
        new(Permissions.PatientsManage, "Thêm/sửa người bệnh", PatientGroup),
        new(Permissions.TemplatesRead, "Xem mẫu phiếu", TemplateGroup),
        new(Permissions.TemplatesManage, "Quản lý mẫu phiếu", TemplateGroup),
        new(Permissions.ConsentsRead, "Xem phiếu", ConsentGroup),
        new(Permissions.ConsentsCreate, "Lập phiếu", ConsentGroup),
        new(Permissions.ConsentsSign, "Ký phiếu phía bệnh viện", ConsentGroup),
        new(Permissions.ConsentsRevoke, "Thu hồi phiếu", ConsentGroup),
        new(Permissions.KioskUse, "Dùng kiosk người bệnh tự làm phiếu", ConsentGroup),
        new(Permissions.ReportsRead, "Xem báo cáo thống kê", ReportGroup),
        new(Permissions.ExportsRun, "Xuất dữ liệu", ReportGroup),
        new(Permissions.ExportsSensitive, "Xuất dữ liệu nhạy cảm (CCCD đầy đủ)", ReportGroup),
        new(Permissions.NursingReportsSubmit, "Lập và gửi báo cáo điều dưỡng của khoa", NursingGroup),
        new(Permissions.NursingReportsRead, "Xem báo cáo điều dưỡng mọi khoa và bảng tổng hợp", NursingGroup),
        new(Permissions.NursingReportsManage, "Phòng Điều dưỡng: mở lại báo cáo, cài khoa báo cáo, số liệu HIS", NursingGroup),
        new(Permissions.ScopeAllDepartments, "Xem dữ liệu toàn viện", ScopeGroup),
        new(Permissions.TestingManage, "Phân công kiểm thử, xem và xử lý báo cáo kiểm thử", TestingGroup),
        new(Permissions.QcRecord, "Ghi kết quả nội kiểm đường huyết của khoa, gửi sổ tháng", QcGroup),
        new(Permissions.QcReview, "Khoa HSH: xem xét kết quả nội kiểm các khoa, xác nhận sổ tháng", QcGroup),
        new(Permissions.QcManage, "Khoa HSH: khai báo máy đo, lô que thử, lô dung dịch chứng", QcGroup),
    ];

    private static readonly HashSet<string> Codes = new(All.Select(p => p.Code), StringComparer.Ordinal);

    public static IReadOnlyCollection<string> AllCodes => Codes;

    public static bool IsDefined(string code) => Codes.Contains(code);
}
