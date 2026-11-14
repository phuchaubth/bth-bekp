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

    public const string ReportsRead = "reports.read";
    public const string ExportsRun = "exports.run";
    public const string ExportsSensitive = "exports.sensitive";

    /// <summary>Xem dữ liệu của mọi khoa (không giới hạn theo khoa của người dùng).</summary>
    public const string ScopeAllDepartments = "scope.all_departments";
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
    private const string ScopeGroup = "Phạm vi dữ liệu";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Permissions.UsersRead, "Xem người dùng", SystemGroup),
        new(Permissions.UsersManage, "Quản lý người dùng", SystemGroup),
        new(Permissions.RolesManage, "Quản lý vai trò và phân quyền", SystemGroup),
        new(Permissions.DepartmentsManage, "Quản lý khoa/phòng", SystemGroup),
        new(Permissions.AuditRead, "Xem nhật ký hệ thống", SystemGroup),
        new(Permissions.SettingsManage, "Cấu hình hệ thống", SystemGroup),
        new(Permissions.PatientsRead, "Xem người bệnh", PatientGroup),
        new(Permissions.PatientsManage, "Thêm/sửa người bệnh", PatientGroup),
        new(Permissions.TemplatesRead, "Xem mẫu phiếu", TemplateGroup),
        new(Permissions.TemplatesManage, "Quản lý mẫu phiếu", TemplateGroup),
        new(Permissions.ConsentsRead, "Xem phiếu", ConsentGroup),
        new(Permissions.ConsentsCreate, "Lập phiếu", ConsentGroup),
        new(Permissions.ConsentsSign, "Ký phiếu phía bệnh viện", ConsentGroup),
        new(Permissions.ConsentsRevoke, "Thu hồi phiếu", ConsentGroup),
        new(Permissions.ReportsRead, "Xem báo cáo thống kê", ReportGroup),
        new(Permissions.ExportsRun, "Xuất dữ liệu", ReportGroup),
        new(Permissions.ExportsSensitive, "Xuất dữ liệu nhạy cảm (CCCD đầy đủ)", ReportGroup),
        new(Permissions.ScopeAllDepartments, "Xem dữ liệu toàn viện", ScopeGroup),
    ];

    private static readonly HashSet<string> Codes = new(All.Select(p => p.Code), StringComparer.Ordinal);

    public static IReadOnlyCollection<string> AllCodes => Codes;

    public static bool IsDefined(string code) => Codes.Contains(code);
}
