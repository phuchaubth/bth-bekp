using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Consent.Persistence.Seeding;

/// <summary>
/// Khởi tạo dữ liệu bắt buộc (vai trò hệ thống, tài khoản quản trị đầu tiên) và dữ liệu minh họa (tùy chọn).
/// Chạy lại nhiều lần không tạo bản trùng.
/// </summary>
internal sealed partial class DatabaseSeeder(
    ConsentDbContext db,
    IPasswordHasher passwordHasher,
    IClock clock,
    IOptions<SeedOptions> options,
    ILogger<DatabaseSeeder> logger)
{
    private static readonly (string Code, string Name)[] DemoDepartments =
    [
        ("KKB", "Khoa Khám bệnh"),
        ("NOI", "Khoa Nội tổng hợp"),
        ("KHTH", "Phòng Kế hoạch tổng hợp"),
    ];

    /// <summary>Tài khoản demo: tên giả lập, không phải người thật.</summary>
    private static readonly (string UserName, string FullName, string Title, string Role, string DepartmentCode)[] DemoUsers =
    [
        ("bacsi.demo", "Bác sĩ Demo", "Bác sĩ", SystemRoles.Doctor, "NOI"),
        ("dieuduong.demo", "Điều dưỡng Demo", "Điều dưỡng", SystemRoles.Nurse, "NOI"),
        ("tiepdon.demo", "Tiếp đón Demo", "Nhân viên tiếp đón", SystemRoles.Receptionist, "KKB"),
        ("truongkhoa.demo", "Trưởng khoa Demo", "Trưởng khoa", SystemRoles.DepartmentHead, "NOI"),
        ("mauphieu.demo", "Quản lý mẫu phiếu Demo", "Chuyên viên", SystemRoles.TemplateManager, "KHTH"),
        ("baocao.demo", "Báo cáo Demo", "Chuyên viên", SystemRoles.Reporter, "KHTH"),
        ("kiemtra.demo", "Kiểm tra Demo", "Chuyên viên", SystemRoles.Auditor, "KHTH"),
    ];

    /// <summary>Người bệnh demo: họ tên có "(giả lập)", số giấy tờ là số thử nghiệm, không phải người thật.</summary>
    private static readonly PatientDetails[] DemoPatients =
    [
        new("Nguyễn Văn An (giả lập)", new DateOnly(1985, 3, 12), Gender.Male, "001085000001", null, new DateOnly(2021, 8, 15),
            "Số 1 Đường Thử Nghiệm, Phường Mẫu, Hà Nội", null, "0900000001", "Kinh", "Kỹ sư", "Việt Nam", null, null),
        new("Trần Thị Bình (giả lập)", new DateOnly(1992, 7, 25), Gender.Female, "079192000002", null, new DateOnly(2022, 1, 10),
            "Số 2 Đường Thử Nghiệm, Phường Mẫu, TP. Hồ Chí Minh", null, "0900000002", "Kinh", "Giáo viên", "Việt Nam", "DN4790000000002", null),
        new("Lê Minh Cường (giả lập)", new DateOnly(2010, 11, 2), Gender.Male, "048210000010", null, null,
            "Số 3 Đường Thử Nghiệm, Phường Mẫu, Đà Nẵng", null, null, "Kinh", "Học sinh", "Việt Nam", null, null),
    ];

    private readonly SeedOptions _options = options.Value;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var roles = await SeedRolesAsync(cancellationToken).ConfigureAwait(false);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await SeedAdministratorAsync(roles, cancellationToken).ConfigureAwait(false);
        await SeedConsentTemplatesAsync(cancellationToken).ConfigureAwait(false);
        if (_options.DemoData)
        {
            await SeedDemoDataAsync(roles, cancellationToken).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tạo vai trò hệ thống còn thiếu với quyền mặc định. Không ghi đè quyền của vai trò đã có (quản trị viên có thể đã sửa),
    /// riêng vai trò Quản trị hệ thống luôn được bổ sung đủ mọi quyền.
    /// </summary>
    private async Task<Dictionary<string, Role>> SeedRolesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Roles.Include(r => r.Permissions).ToDictionaryAsync(r => r.Code, cancellationToken)
            .ConfigureAwait(false);

        foreach (var definition in SystemRoles.Defaults)
        {
            if (!existing.TryGetValue(definition.Code, out var role))
            {
                role = Role.Create(definition.Code, definition.Name, definition.Description, isSystem: true);
                if (role.IsAdministrator)
                {
                    role.GrantAllPermissions();
                }
                else
                {
                    role.SetPermissions(definition.Permissions);
                }

                db.Roles.Add(role);
                existing[role.Code] = role;
            }
            else if (role.IsAdministrator)
            {
                role.GrantAllPermissions();
            }
        }

        return existing;
    }

    private async Task SeedAdministratorAsync(Dictionary<string, Role> roles, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            LogNoAdministratorPassword(logger);
            return;
        }

        var errors = PasswordPolicy.Validate(_options.AdminPassword, _options.AdminUserName);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Mật khẩu quản trị khởi tạo (Seed:AdminPassword) chưa đủ mạnh: " + string.Join(" ", errors));
        }

        var admin = User.Create(_options.AdminUserName, _options.AdminFullName, clock.UtcNow);
        admin.SetRoles([roles[SystemRoles.Administrator].Id]);
        admin.SetPassword(passwordHasher.Hash(_options.AdminPassword), mustChangePassword: true);
        db.Users.Add(admin);
        LogAdministratorCreated(logger, admin.UserName);
    }

    /// <summary>
    /// Ba mẫu phiếu đề xuất (chờ bệnh viện duyệt). Môi trường thật: tạo ở dạng nháp để Phòng KHTH duyệt rồi mới phát hành;
    /// môi trường demo: phát hành luôn để thử lập phiếu.
    /// </summary>
    private async Task SeedConsentTemplatesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.ConsentTemplates.Select(t => t.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var definition in DefaultConsentTemplates.All.Where(t => !existing.Contains(t.Code)))
        {
            var template = ConsentTemplate.Create(
                definition.Code, definition.Name, definition.HospitalSignerRoleCode, null, definition.ContentJson, clock.UtcNow);
            if (_options.DemoData)
            {
                template.Publish(1, clock.UtcNow);
            }

            db.ConsentTemplates.Add(template);
            LogConsentTemplateCreated(logger, definition.Code, _options.DemoData);
        }
    }

    private async Task SeedDemoDataAsync(Dictionary<string, Role> roles, CancellationToken cancellationToken)
    {
        var departments = await db.Departments.ToDictionaryAsync(d => d.Code, cancellationToken).ConfigureAwait(false);
        foreach (var (code, name) in DemoDepartments.Where(d => !departments.ContainsKey(d.Code)))
        {
            var department = Department.Create(code, name);
            db.Departments.Add(department);
            departments[code] = department;
        }

        if (string.IsNullOrWhiteSpace(_options.DemoPassword))
        {
            LogNoDemoPassword(logger);
            return;
        }

        var errors = PasswordPolicy.Validate(_options.DemoPassword);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "Mật khẩu tài khoản demo (Seed:DemoPassword) chưa đủ mạnh: " + string.Join(" ", errors));
        }

        if (!await db.Patients.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var details in DemoPatients)
            {
                db.Patients.Add(Patient.Create(details, clock.UtcNow));
            }
        }

        var existingUserNames = await db.Users.Select(u => u.UserName).ToListAsync(cancellationToken).ConfigureAwait(false);
        var passwordHash = passwordHasher.Hash(_options.DemoPassword);
        foreach (var demo in DemoUsers.Where(u => !existingUserNames.Contains(u.UserName)))
        {
            var user = User.Create(demo.UserName, demo.FullName, clock.UtcNow);
            user.UpdateProfile(demo.FullName, null, null, demo.Title, departments[demo.DepartmentCode].Id, null);
            user.SetRoles([roles[demo.Role].Id]);
            user.SetPassword(passwordHash, mustChangePassword: false);
            db.Users.Add(user);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Chưa có tài khoản nào và chưa cấu hình Seed:AdminPassword: bỏ qua tạo tài khoản quản trị đầu tiên.")]
    private static partial void LogNoAdministratorPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Đã tạo tài khoản quản trị đầu tiên '{UserName}' (phải đổi mật khẩu ở lần đăng nhập đầu).")]
    private static partial void LogAdministratorCreated(ILogger logger, string userName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Chưa cấu hình Seed:DemoPassword: bỏ qua tạo tài khoản demo.")]
    private static partial void LogNoDemoPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Đã tạo mẫu phiếu đề xuất {Code} (phát hành ngay: {Published}).")]
    private static partial void LogConsentTemplateCreated(ILogger logger, string code, bool published);
}
