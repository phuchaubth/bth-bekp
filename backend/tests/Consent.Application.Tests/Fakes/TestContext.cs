using Consent.Application.Auth;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Departments;
using Consent.Application.Exports;
using Consent.Application.Kiosk;
using Consent.Application.Maintenance;
using Consent.Application.NursingReports;
using Consent.Application.Testing;
using Consent.Application.Patients;
using Consent.Application.Reports;
using Consent.Application.Roles;
using Consent.Application.Settings;
using Consent.Application.Users;
using Consent.Domain.Departments;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Consent.Domain.Settings;
using Microsoft.Extensions.Options;

namespace Consent.Application.Tests.Fakes;

/// <summary>Dựng các service của tầng Application với repository trong bộ nhớ.</summary>
internal sealed class AppTestContext
{
    public const string DefaultPassword = "Mat-Khau-Dung-2026";

    public AppTestContext(AuthOptions? authOptions = null)
    {
        Tokens = new FakeTokenService(Clock);
        Options = authOptions ?? new AuthOptions();
        foreach (var definition in SystemRoles.Defaults)
        {
            var role = Role.Create(definition.Code, definition.Name, definition.Description, isSystem: true);
            if (role.IsAdministrator)
            {
                role.GrantAllPermissions();
            }
            else
            {
                role.SetPermissions(definition.Permissions);
            }

            Store.Roles.Add(role);
        }
    }

    public InMemoryStore Store { get; } = new();

    public FakeClock Clock { get; } = new();

    public FakePasswordHasher Hasher { get; } = new();

    public FakeTokenService Tokens { get; }

    public FakeCurrentUser CurrentUser { get; } = new();

    public AuthOptions Options { get; }

    public StubHisGateway His { get; } = new();

    public HisOptions HisOptions { get; } = new() { TimeoutSeconds = 1 };

    /// <summary>Danh mục khoa/phòng của HIS (quyết định 42): mặc định chưa kết nối, khoa/phòng thêm, sửa trong phần mềm.</summary>
    public StubDepartmentSource DepartmentSource { get; } = new();

    public Role RoleByCode(string code) => Store.Roles.Single(r => r.Code == code);

    public User AddUser(
        string userName,
        string role = SystemRoles.Doctor,
        string password = DefaultPassword,
        bool mustChangePassword = false,
        bool active = true,
        Guid? departmentId = null)
    {
        var user = User.Create(userName, userName + " Demo", Clock.UtcNow);
        user.SetDepartments(departmentId, []);
        user.SetRoles([RoleByCode(role).Id]);
        user.SetPassword(Hasher.Hash(password), mustChangePassword);
        if (!active)
        {
            user.Deactivate();
        }

        Store.Users.Add(user);
        return user;
    }

    public Department AddDepartment(string code, string name, bool active = true)
    {
        var department = Department.Create(code, name);
        if (!active)
        {
            department.Update(code, name, isActive: false);
        }

        Store.Departments.Add(department);
        return department;
    }

    public AuthService CreateAuthService() => new(
        new InMemoryUserRepository(Store),
        new InMemoryRoleRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryRefreshTokenRepository(Store),
        new InMemoryAuditLogRepository(Store),
        Store,
        Hasher,
        Tokens,
        Clock,
        new FakeRequestContext(),
        Microsoft.Extensions.Options.Options.Create(Options),
        new LoginRequestValidator(),
        new ChangePasswordRequestValidator(),
        CreateMaintenanceService());

    /// <summary>
    /// Trạng thái bảo trì dùng chung như singleton của ứng dụng (quyết định 53); mặc định không giữ trong bộ nhớ để test thấy ngay
    /// thay đổi trong kho.
    /// </summary>
    public MaintenanceStateCache MaintenanceCache { get; set; } = new(TimeSpan.Zero);

    public MaintenanceService CreateMaintenanceService() => new(
        new InMemorySystemSettingRepository(Store),
        new InMemoryUserRepository(Store),
        new InMemoryAuditLogRepository(Store),
        Store,
        MaintenanceCache,
        CurrentUser,
        new FakeRequestContext(),
        Clock,
        new UpdateMaintenanceRequestValidator(Clock));

    public UserAdminService CreateUserAdminService() => new(
        new InMemoryUserRepository(Store),
        new InMemoryRoleRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryRefreshTokenRepository(Store),
        Store,
        Hasher,
        Clock,
        CurrentUser,
        new CreateUserRequestValidator(),
        new UpdateUserRequestValidator(),
        new ResetPasswordRequestValidator());

    public DepartmentService CreateDepartmentService() => new(
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        Store,
        new DepartmentRequestValidator(),
        DepartmentSource);

    public RoleService CreateRoleService() => new(
        new InMemoryRoleRepository(Store), new InMemoryUserRepository(Store), new InMemoryConsentTemplateRepository(Store), Store);

    public PatientService CreatePatientService() => new(
        new InMemoryPatientRepository(Store),
        His,
        new InMemoryAuditLogRepository(Store),
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        Microsoft.Extensions.Options.Options.Create(HisOptions),
        new PatientRequestValidator(Clock),
        new RepresentativeRequestValidator(Clock),
        new PatientLookupRequestValidator());

    public ConsentTemplateService CreateConsentTemplateService() => new(
        new InMemoryConsentTemplateRepository(Store),
        new InMemoryRoleRepository(Store),
        new InMemoryDepartmentRepository(Store),
        Store,
        Clock,
        new CreateConsentTemplateRequestValidator(),
        new UpdateConsentTemplateRequestValidator());

    public ConsentFormService CreateConsentFormService() => new(
        new InMemoryConsentFormRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        new InMemoryPatientRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        new InMemoryRoleRepository(Store),
        FormNumbers,
        new InMemoryAuditLogRepository(Store),
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        new CreateConsentFormRequestValidator(),
        new UpdateConsentFormRequestValidator(),
        new CancelConsentFormRequestValidator(),
        Pdf,
        Documents,
        CreateSettingsService(),
        CreateFormInheritanceService());

    public FormInheritanceService CreateFormInheritanceService() => new(
        new InMemorySystemSettingRepository(Store), new InMemoryConsentFormRepository(Store), Store, Clock);

    /// <summary>Quản trị đặt số ngày được kế thừa phiếu lần trước (quyết định 27).</summary>
    public void SetInheritanceDays(int days) =>
        Store.SystemSettings.Add(SystemSetting.Create(SettingKeys.FormInheritanceDays, days.ToString(System.Globalization.CultureInfo.InvariantCulture), Clock.UtcNow));

    public SignatureSpecimenService CreateSignatureSpecimenService() => new(CurrentUser, new InMemoryUserRepository(Store), Store, Clock);

    public SettingsService CreateSettingsService() => new(
        new InMemorySystemSettingRepository(Store),
        Store,
        Clock,
        His,
        Microsoft.Extensions.Options.Options.Create(new HospitalOptions()),
        new UpdateHospitalSettingsRequestValidator(),
        Documents,
        PatientSource);

    public ConsentSigningService CreateConsentSigningService() => new(
        CreateConsentFormService(),
        new InMemoryConsentFormRepository(Store),
        new InMemoryRoleRepository(Store),
        new InMemoryUserRepository(Store),
        Tokens,
        Documents,
        Pdf,
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        new PatientSignatureRequestValidator(),
        new RevokeConsentFormRequestValidator(),
        CreateConsentEncounterService());

    public RemoteSigningService CreateRemoteSigningService() => new(
        CreateConsentFormService(),
        new InMemoryConsentFormRepository(Store),
        new InMemoryRemoteSigningLinkRepository(Store),
        new InMemoryUserRepository(Store),
        Tokens,
        Store,
        Clock,
        CurrentUser);

    public StubEncounterSource EncounterSource { get; } = new();

    public ConsentEncounterService CreateConsentEncounterService() => new(
        CreateConsentFormService(),
        new InMemoryConsentFormRepository(Store),
        new InMemoryPatientRepository(Store),
        EncounterSource,
        CreateEncounterSettingsService(),
        Clock,
        Microsoft.Extensions.Options.Options.Create(HisOptions));

    public EncounterSettingsService CreateEncounterSettingsService() => new(
        new InMemorySystemSettingRepository(Store), EncounterSource, Store, Clock);

    public StubDailyStatisticsSource DailyStatistics { get; } = new();

    public CapturingNursingDocuments NursingDocuments { get; } = new();

    public NursingReportService CreateNursingReportService() => new(
        new InMemoryNursingReportRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        Store,
        CurrentUser,
        Clock,
        CreateSettingsService(),
        NursingDocuments,
        new InMemorySystemSettingRepository(Store));

    public NursingSummaryService CreateNursingSummaryService() => new(
        new InMemoryNursingReportRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        DailyStatistics,
        Store,
        CurrentUser,
        Clock,
        CreateSettingsService(),
        NursingDocuments,
        NursingDocuments,
        new InMemorySystemSettingRepository(Store));

    public NursingDelegationService CreateNursingDelegationService() => new(
        new InMemoryNursingReportRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        new InMemoryRoleRepository(Store),
        Store,
        CurrentUser,
        Clock,
        new InMemorySystemSettingRepository(Store));

    public TestAssignmentService CreateTestAssignmentService() => new(
        new InMemoryTestAssignmentRepository(Store),
        new InMemoryUserRepository(Store),
        new InMemoryRoleRepository(Store),
        new InMemoryDepartmentRepository(Store),
        Store,
        CurrentUser,
        Clock,
        [XlsxWriter, CsvWriter]);

    public NursingDashboardService CreateNursingDashboardService() => new(
        new InMemoryNursingReportRepository(Store),
        new InMemoryDepartmentRepository(Store),
        Clock);

    /// <summary>Đã cấu hình API thông tin người bệnh và quản trị đã bật gắn lượt tiếp nhận (quyết định 25).</summary>
    public void TurnOnEncounters()
    {
        EncounterSource.IsEnabled = true;
        Store.SystemSettings.Add(SystemSetting.Create(SettingKeys.EncountersEnabled, "true", Clock.UtcNow));
    }

    public ConsentFormExportService CreateConsentFormExportService() => new(
        new InMemoryConsentFormRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryUserRepository(Store),
        new InMemoryExportJobRepository(Store),
        new InMemoryAuditLogRepository(Store),
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        [XlsxWriter, CsvWriter],
        new ConsentFormExportRequestValidator());

    public ReportService CreateReportService() => new(
        new InMemoryConsentStatisticsRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        Clock,
        CurrentUser);

    public DashboardService CreateDashboardService() => new(
        new InMemoryConsentFormRepository(Store),
        new InMemoryConsentStatisticsRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        Clock,
        CurrentUser);

    public StubPatientSource PatientSource { get; } = new();

    public KioskService CreateKioskService() => new(
        new InMemoryUserRepository(Store),
        new InMemoryDepartmentRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        new InMemorySystemSettingRepository(Store),
        new InMemoryPatientRepository(Store),
        PatientSource,
        new InMemoryConsentFormRepository(Store),
        FormNumbers,
        new InMemoryAuditLogRepository(Store),
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        Microsoft.Extensions.Options.Options.Create(HisOptions),
        new PatientRequestValidator(Clock),
        CreateFormInheritanceService());

    public KioskSettingsService CreateKioskSettingsService() => new(
        new InMemorySystemSettingRepository(Store),
        new InMemoryConsentTemplateRepository(Store),
        Store,
        Clock);

    public RecordingTabularWriter XlsxWriter { get; } = new(ExportFormat.Xlsx);

    public RecordingTabularWriter CsvWriter { get; } = new(ExportFormat.Csv);

    public SequentialFormNumbers FormNumbers { get; } = new();

    public InMemoryDocumentStore Documents { get; } = new();

    public RecordingPdfRenderer Pdf { get; } = new();
}
