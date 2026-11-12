using Consent.Application.Auditing;
using Consent.Application.ConsentForms;
using Consent.Application.Exports;
using Consent.Application.Patients;
using Consent.Application.Reports;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Exports;
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

    /// <summary>Số phiếu khớp bộ lọc (bỏ qua phân trang).</summary>
    Task<int> CountAsync(ConsentFormSearch query, CancellationToken cancellationToken);

    /// <summary>
    /// Đọc lần lượt các phiếu khớp bộ lọc để xuất dữ liệu, theo thời điểm lập tăng dần (lấy tối đa
    /// <see cref="ConsentFormSearch.PageSize"/> phiếu của trang <see cref="ConsentFormSearch.Page"/>).
    /// Nội dung phiếu chỉ được đọc khi <paramref name="includeData"/>.
    /// </summary>
    IAsyncEnumerable<ConsentFormExportRecord> StreamForExportAsync(ConsentFormSearch query, bool includeData, CancellationToken cancellationToken);

    void Add(ConsentForm form);
}

/// <summary>
/// Truy vấn thống kê cho báo cáo và trang tổng quan (đếm, nhóm ngay trong CSDL; không đọc dữ liệu cá nhân).
/// Phạm vi khoa và bộ lọc lấy từ <see cref="ConsentFormSearch"/> (bỏ qua phân trang và từ khóa).
/// </summary>
public interface IConsentStatisticsRepository
{
    /// <summary>Số phiếu theo khoa, mẫu, trạng thái, quyết định và người ký (lọc theo ngày lập).</summary>
    Task<IReadOnlyList<ConsentFormGroupCount>> CountByGroupAsync(ConsentFormSearch query, CancellationToken cancellationToken);

    /// <summary>Số phiếu theo ngày lập (chỉ số ngày tính từ <paramref name="originUtc"/>, mỗi ngày 24 giờ) và trạng thái.</summary>
    Task<IReadOnlyList<DailyStatusCount>> CountByCreatedDayAsync(
        ConsentFormSearch query, DateTimeOffset originUtc, CancellationToken cancellationToken);

    /// <summary>Số phiếu hoàn tất theo ngày hoàn tất, với thời điểm hoàn tất trong [<paramref name="originUtc"/>, <paramref name="toUtc"/>).</summary>
    Task<IReadOnlyList<DailyCount>> CountCompletedByDayAsync(
        ConsentFormSearch scope, DateTimeOffset originUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>Số giây từ lúc lập đến lúc hoàn tất của từng phiếu đang ở trạng thái hoàn tất.</summary>
    Task<IReadOnlyList<int>> CompletionSecondsAsync(ConsentFormSearch query, CancellationToken cancellationToken);

    /// <summary>Số phiếu hoàn tất (hoặc thu hồi khi <paramref name="revoked"/>) trong [<paramref name="fromUtc"/>, <paramref name="toUtc"/>).</summary>
    Task<int> CountClosedAsync(
        ConsentFormSearch scope, bool revoked, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken);

    /// <summary>Phiếu chờ ký mà người bệnh đã ký, thuộc các mẫu <paramref name="templateIds"/>: chờ phía bệnh viện xác nhận.</summary>
    Task<DashboardFormList> AwaitingConfirmationAsync(
        ConsentFormSearch scope, IReadOnlyCollection<Guid> templateIds, int take, CancellationToken cancellationToken);

    /// <summary>Phiếu nháp/chờ ký do người dùng <paramref name="userId"/> lập.</summary>
    Task<DashboardFormList> OpenFormsCreatedByAsync(Guid userId, int take, CancellationToken cancellationToken);
}

public interface IExportJobRepository
{
    void Add(ExportJob job);

    Task<PagedResult<ExportJobRecord>> SearchAsync(ExportJobSearch query, CancellationToken cancellationToken);
}

/// <summary>Cấp số phiếu tăng dần, không trùng (dùng sequence của CSDL).</summary>
public interface IConsentFormNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
