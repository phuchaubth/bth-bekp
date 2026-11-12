using Consent.Application.Auditing;
using Consent.Application.ConsentForms;
using Consent.Application.Patients;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<bool> AnyInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Số người dùng đang hoạt động có vai trò <paramref name="roleId"/>, không tính <paramref name="excludeUserId"/>.</summary>
    Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludeUserId, CancellationToken cancellationToken);

    Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery query, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    void Add(User user);
}

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);

    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public interface IDepartmentRepository
{
    Task<IReadOnlyList<Department>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludeId, CancellationToken cancellationToken);

    void Add(Department department);

    void Remove(Department department);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken);

    void Add(RefreshToken token);
}

public interface IAuditLogRepository
{
    void Add(AuditLog auditLog);

    Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken);
}

public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tìm theo số CCCD (qua chỉ mục mù, không so sánh giá trị gốc trong CSDL).</summary>
    Task<Patient?> FindByCitizenIdAsync(string citizenId, CancellationToken cancellationToken);

    Task<bool> PatientCodeExistsAsync(string patientCode, Guid? excludeId, CancellationToken cancellationToken);

    Task<PagedResult<PatientSummary>> SearchAsync(PatientQuery query, CancellationToken cancellationToken);

    void Add(Patient patient);
}

public interface IConsentTemplateRepository
{
    /// <summary>Danh sách mẫu kèm các phiên bản.</summary>
    Task<IReadOnlyList<ConsentTemplate>> ListAsync(bool includeInactive, CancellationToken cancellationToken);

    Task<ConsentTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>Phiên bản theo Id (để dựng phiếu đã lập theo đúng phiên bản của nó).</summary>
    Task<ConsentTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken);

    void Add(ConsentTemplate template);
}

public interface IConsentFormRepository
{
    Task<ConsentForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tìm kiếm có phân trang; phạm vi khoa đã được áp dụng sẵn trong <paramref name="query"/>.</summary>
    Task<PagedResult<ConsentFormListItemDto>> SearchAsync(ConsentFormSearch query, CancellationToken cancellationToken);

    /// <summary>Phiếu thay thế cho phiếu <paramref name="formId"/> (nếu đã lập).</summary>
    Task<ConsentFormRef?> FindReplacementAsync(Guid formId, CancellationToken cancellationToken);

    void Add(ConsentForm form);
}

/// <summary>Cấp số phiếu tăng dần, không trùng (dùng sequence của CSDL).</summary>
public interface IConsentFormNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
