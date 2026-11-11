using Consent.Application.Auth;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Departments;
using Consent.Application.Patients;
using Consent.Application.Roles;
using Consent.Application.Users;
using Consent.Domain.Departments;
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
        FormNumbers,
        new InMemoryAuditLogRepository(Store),
        Store,
        Clock,
        CurrentUser,
        new FakeRequestContext(),
        new CreateConsentFormRequestValidator(),
        new UpdateConsentFormRequestValidator(),
        new CancelConsentFormRequestValidator());

    public SequentialFormNumbers FormNumbers { get; } = new();
}
