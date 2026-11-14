using Consent.Application.Auth;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Departments;
using Consent.Application.Exports;
using Consent.Application.Patients;
using Consent.Application.Reports;
using Consent.Application.Roles;
using Consent.Application.Settings;
using Consent.Application.Users;
using Consent.Domain.Departments;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
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
        user.UpdateProfile(user.FullName, null, null, null, departmentId, null);
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
        new ChangePasswordRequestValidator());

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
        new DepartmentRequestValidator());

    public RoleService CreateRoleService() => new(new InMemoryRoleRepository(Store), Store);

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
        CreateSettingsService());

    public SignatureSpecimenService CreateSignatureSpecimenService() => new(CurrentUser, new InMemoryUserRepository(Store), Store, Clock);

    public SettingsService CreateSettingsService() => new(
        new InMemorySystemSettingRepository(Store),
        Store,
        Clock,
        His,
        Microsoft.Extensions.Options.Options.Create(new HospitalOptions()),
        new UpdateHospitalSettingsRequestValidator(),
        Documents);

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
        new RevokeConsentFormRequestValidator());

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

    public RecordingTabularWriter XlsxWriter { get; } = new(ExportFormat.Xlsx);

    public RecordingTabularWriter CsvWriter { get; } = new(ExportFormat.Csv);

    public SequentialFormNumbers FormNumbers { get; } = new();

    public InMemoryDocumentStore Documents { get; } = new();

    public RecordingPdfRenderer Pdf { get; } = new();
}
