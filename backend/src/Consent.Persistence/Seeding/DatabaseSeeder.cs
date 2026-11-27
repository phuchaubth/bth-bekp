using Consent.Application.Abstractions;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.CriticalResults;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Domain.QualityControl;
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
        ("PDD", "Phòng Điều dưỡng"),
        ("HSH", "Khoa Huyết học – Sinh hóa"),
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
        ("phongdieuduong.demo", "Phòng Điều dưỡng Demo", "Điều dưỡng trưởng", SystemRoles.NursingOffice, "PDD"),
        ("hsh.demo", "Khoa HSH Demo", "Kỹ thuật viên", SystemRoles.LabQualityControl, "HSH"),
        ("ktv.demo", "Kỹ thuật viên Demo", "Kỹ thuật viên", SystemRoles.CriticalLab, "HSH"),
        ("qlcl.demo", "QLCL Demo", "Chuyên viên", SystemRoles.CriticalQuality, "KHTH"),
        ("manhinh.demo", "Màn hình Khoa Nội Demo", "Màn hình theo dõi", SystemRoles.CriticalDisplay, "NOI"),
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
            user.UpdateProfile(demo.FullName, null, null, demo.Title, null);
            user.SetDepartments(departments[demo.DepartmentCode].Id, []);
            user.SetRoles([roles[demo.Role].Id]);
            user.SetPassword(passwordHash, mustChangePassword: false);
            db.Users.Add(user);
        }

        await SeedDemoQualityControlAsync(departments["NOI"].Id, cancellationToken).ConfigureAwait(false);
        await SeedDemoCriticalTestsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Danh mục kết quả nguy cấp demo (quyết định 60): vài mục ví dụ, tên có "(giả lập)", khoảng nguy cấp ghi "ví dụ". Khi dùng
    /// thật, QLCL nhập danh mục theo Phụ lục 01 QT-KQNC đang hiệu lực. Chỉ tạo khi danh mục còn trống.
    /// </summary>
    private async Task SeedDemoCriticalTestsAsync(CancellationToken cancellationToken)
    {
        if (await db.CriticalTests.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var now = clock.UtcNow;
        db.CriticalTests.Add(CriticalTest.Create(
            new("Kali (K+) (giả lập)", "Ví dụ: ≤ 2,5 hoặc ≥ 6,0 mmol/L", null, "mmol/L", "Huyết học – Sinh hóa", CriticalLogbook.Laboratory, false, 1), now));
        db.CriticalTests.Add(CriticalTest.Create(
            new("Tiểu cầu (PLT) (giả lập)", "Ví dụ: < 10 K/µL", null, "K/µL", "Huyết học – Sinh hóa", CriticalLogbook.Laboratory, false, 2), now));
        db.CriticalTests.Add(CriticalTest.Create(
            new("Cấy máu (giả lập)", "Ví dụ: dương tính", null, null, "Vi sinh", CriticalLogbook.Microbiology, true, 3), now));
        db.CriticalTests.Add(CriticalTest.Create(
            new("Siêu âm tim (giả lập)", "Ví dụ: suy tim cấp, nghi nhồi máu cơ tim", null, null, "Chẩn đoán hình ảnh", CriticalLogbook.Laboratory, true, 4), now));
    }

    /// <summary>
    /// Danh mục nội kiểm demo (quyết định 57): một máy đo của khoa Nội tổng hợp, một lô que thử, hai lô dung dịch chứng. Tên, số
    /// seri, số lô, ngưỡng đều giả lập. Chỉ tạo khi chưa có máy đo nào.
    /// </summary>
    private async Task SeedDemoQualityControlAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        if (await db.QcMeters.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var now = clock.UtcNow;
        var expiry = DateOnly.FromDateTime(now.UtcDateTime).AddYears(1);
        db.QcMeters.Add(QcMeter.Create(departmentId, "Máy đo đường huyết Demo (giả lập)", "DEMO-0001", now));
        db.QcLots.Add(QcLot.CreateTestStrip("Que thử Demo (giả lập)", "QT-DEMO-01", expiry, now));
        db.QcLots.Add(QcLot.CreateControl("Dung dịch chứng Demo mức 1 (giả lập)", "DC1-DEMO-01", expiry, 1, 40, 70, QcUnit.MgPerDl, now));
        db.QcLots.Add(QcLot.CreateControl("Dung dịch chứng Demo mức 2 (giả lập)", "DC2-DEMO-01", expiry, 2, 100, 140, QcUnit.MgPerDl, now));
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
