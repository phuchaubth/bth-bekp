using Consent.Application.Auditing;
using Consent.Application.ConsentForms;
using Consent.Application.Exports;
using Consent.Application.NursingReports;
using Consent.Application.Patients;
using Consent.Application.Reports;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Exports;
using Consent.Domain.Identity;
using Consent.Domain.NursingReports;
using Consent.Domain.Patients;
using Consent.Domain.QualityControl;
using Consent.Domain.Settings;
using Consent.Domain.Testing;
using Consent.Shared;

namespace Consent.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> GetByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken);

    Task<bool> AnyInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken);

    Task<bool> AnyAsync(CancellationToken cancellationToken);

    /// <summary>Số người dùng đang hoạt động có vai trò <paramref name="roleId"/>, không tính <paramref name="excludeUserId"/>.</summary>
    Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludeUserId, CancellationToken cancellationToken);

    /// <summary>Số người dùng có vai trò <paramref name="roleId"/>, kể cả tài khoản đã khóa.</summary>
    Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken cancellationToken);

    Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery query, DateTimeOffset nowUtc, CancellationToken cancellationToken);

    /// <summary>Người dùng đang hoạt động có ít nhất một vai trò trong <paramref name="roleIds"/>, kèm khoa của từng người.</summary>
    Task<IReadOnlyList<User>> ListActiveInRolesAsync(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken);

    void Add(User user);
}

public interface IRoleRepository
{
    Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken);

    Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(Role role);

    void Remove(Role role);
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

    /// <summary>Tìm theo mã người bệnh (mã y tế trên HIS), so sánh chính xác.</summary>
    Task<Patient?> FindByPatientCodeAsync(string patientCode, CancellationToken cancellationToken);

    Task<PagedResult<PatientSummary>> SearchAsync(PatientQuery query, PatientScope scope, CancellationToken cancellationToken);

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

/// <summary>Mã ký trên điện thoại riêng của người bệnh (quyết định 32).</summary>
public interface IRemoteSigningLinkRepository
{
    Task<RemoteSigningLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tìm theo mã băm SHA-256 (hex) của mã trong QR.</summary>
    Task<RemoteSigningLink?> FindByCodeHashAsync(string codeHash, CancellationToken cancellationToken);

    void Add(RemoteSigningLink link);
}

public interface IConsentFormRepository
{
    Task<ConsentForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tìm kiếm có phân trang; phạm vi khoa đã được áp dụng sẵn trong <paramref name="query"/>.</summary>
    Task<PagedResult<ConsentFormListItemDto>> SearchAsync(ConsentFormSearch query, CancellationToken cancellationToken);

    /// <summary>Phiếu thay thế cho phiếu <paramref name="formId"/> (nếu đã lập).</summary>
    Task<ConsentFormRef?> FindReplacementAsync(Guid formId, CancellationToken cancellationToken);

    /// <summary>Các phiếu của một bộ phiếu (kèm chữ ký), theo thứ tự lập.</summary>
    Task<IReadOnlyList<ConsentForm>> ListByBundleAsync(Guid bundleId, CancellationToken cancellationToken);

    /// <summary>
    /// Các phiếu của người bệnh đã gắn vào một trong các lượt tiếp nhận <paramref name="encounterNos"/> (quyết định 25), theo
    /// số phiếu. Chỉ đọc số phiếu, tên mẫu, trạng thái: không đọc (giải mã) nội dung phiếu.
    /// </summary>
    Task<IReadOnlyList<EncounterFormRecord>> ListByEncountersAsync(
        Guid patientId, IReadOnlyCollection<string> encounterNos, CancellationToken cancellationToken);

    /// <summary>
    /// Phiếu đã hoàn tất gần nhất của người bệnh theo mẫu, ở khoa <paramref name="departmentId"/>, hoàn tất từ
    /// <paramref name="completedSinceUtc"/> trở đi (kế thừa phiếu lần trước, quyết định 27). Phiếu nháp, chờ ký, đã hủy hay đã
    /// thu hồi không tính.
    /// </summary>
    Task<ConsentForm?> FindLatestCompletedAsync(
        Guid patientId, Guid templateId, Guid departmentId, DateTimeOffset completedSinceUtc, CancellationToken cancellationToken);

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

    /// <summary>
    /// Phiếu chờ ký mà người bệnh đã ký, lập theo các phiên bản mẫu <paramref name="templateVersionIds"/>: chờ phía bệnh viện
    /// xác nhận. Trả kèm các bước trung gian đã xác nhận để tầng ứng dụng tìm bước đang chờ.
    /// </summary>
    Task<IReadOnlyList<AwaitingConfirmationCandidate>> ListAwaitingConfirmationAsync(
        ConsentFormSearch scope, IReadOnlyCollection<Guid> templateVersionIds, CancellationToken cancellationToken);

    /// <summary>Dòng hiển thị trên trang tổng quan của các phiếu <paramref name="formIds"/>, thời điểm là lúc người bệnh ký.</summary>
    Task<IReadOnlyList<DashboardFormItem>> GetSignedFormItemsAsync(IReadOnlyCollection<Guid> formIds, CancellationToken cancellationToken);

    /// <summary>Phiếu nháp/chờ ký do người dùng <paramref name="userId"/> lập.</summary>
    Task<DashboardFormList> OpenFormsCreatedByAsync(Guid userId, int take, CancellationToken cancellationToken);
}

/// <summary>Báo cáo điều dưỡng hằng ngày, khoa báo cáo kèm số giường, số liệu HIS theo ngày (quyết định 31).</summary>
public interface INursingReportRepository
{
    Task<NursingDailyReport?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<NursingDailyReport?> FindAsync(Guid departmentId, DateOnly reportDate, CancellationToken cancellationToken);

    /// <summary>Báo cáo đã gửi gần nhất của khoa trước ngày <paramref name="before"/> (để điền sẵn nhân sự, chỉ tiêu).</summary>
    Task<NursingDailyReport?> FindLatestSubmittedBeforeAsync(Guid departmentId, DateOnly before, CancellationToken cancellationToken);

    /// <summary>Mọi báo cáo (nháp và đã gửi) của một ngày.</summary>
    Task<IReadOnlyList<NursingDailyReport>> ListByDateAsync(DateOnly reportDate, CancellationToken cancellationToken);

    /// <summary>
    /// Báo cáo đã gửi từ ngày <paramref name="from"/> đến ngày <paramref name="to"/> (dashboard, quyết định 47): chỉ số liệu và có
    /// phần nhập thêm không, không đọc (giải mã) nội dung phần nhập thêm.
    /// </summary>
    Task<IReadOnlyList<NursingSubmittedFigures>> ListSubmittedFiguresAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    void Add(NursingDailyReport report);

    Task<IReadOnlyList<NursingReportDepartment>> ListDepartmentsAsync(CancellationToken cancellationToken);

    void AddDepartment(NursingReportDepartment department);

    void RemoveDepartment(NursingReportDepartment department);

    Task<NursingDailyStatistics?> FindStatisticsAsync(DateOnly reportDate, CancellationToken cancellationToken);

    void AddStatistics(NursingDailyStatistics statistics);

    /// <summary>Số liệu HIS các khoa nhập tay trong ngày khi HIS lỗi (quyết định 35).</summary>
    Task<IReadOnlyList<NursingDepartmentStatistics>> ListDepartmentStatisticsAsync(DateOnly reportDate, CancellationToken cancellationToken);

    void AddDepartmentStatistics(NursingDepartmentStatistics statistics);

    void AddDelegation(NursingReportDelegation delegation);

    Task<NursingReportDelegation?> GetDelegationAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Ủy quyền báo cáo điều dưỡng (quyết định 48) mà <paramref name="userId"/> là người ủy quyền hoặc người được ủy quyền;
    /// <c>null</c> là mọi ủy quyền (Phòng Điều dưỡng). Mới tạo trước.
    /// </summary>
    Task<IReadOnlyList<NursingReportDelegation>> ListDelegationsAsync(Guid? userId, CancellationToken cancellationToken);

    /// <summary>Ủy quyền chưa hủy có ngày <paramref name="date"/> (bảng tổng hợp ghi người được ủy quyền trong ngày).</summary>
    Task<IReadOnlyList<NursingReportDelegation>> ListDelegationsCoveringAsync(DateOnly date, CancellationToken cancellationToken);

    /// <summary>Các ủy quyền theo mã (kể cả đã hủy), để ghi người ủy quyền của báo cáo đã gửi theo ủy quyền.</summary>
    Task<IReadOnlyList<NursingReportDelegation>> GetDelegationsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
}

public interface ISystemSettingRepository
{
    Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken);

    void Add(SystemSetting setting);

    void Remove(SystemSetting setting);
}

public interface IExportJobRepository
{
    void Add(ExportJob job);

    Task<PagedResult<ExportJobRecord>> SearchAsync(ExportJobSearch query, CancellationToken cancellationToken);
}

/// <summary>Phân công kiểm thử tính năng mới (quyết định 49).</summary>
public interface ITestAssignmentRepository
{
    void Add(TestAssignment assignment);

    Task<TestAssignment?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Phân công của một đợt kiểm thử (<c>null</c> là mọi đợt), mới tạo trước; tối đa 2.000 phân công.</summary>
    Task<IReadOnlyList<TestAssignment>> ListAsync(string? version, CancellationToken cancellationToken);

    /// <summary>Mọi phân công của một người (kể cả đã hủy).</summary>
    Task<IReadOnlyList<TestAssignment>> ListForAssigneeAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Các đợt kiểm thử đã có, đợt mới phân công trước.</summary>
    Task<IReadOnlyList<string>> ListVersionsAsync(CancellationToken cancellationToken);
}

/// <summary>Sổ theo dõi kết quả nội kiểm đường huyết tại giường (quyết định 57): danh mục máy đo, lô vật tư và sổ tháng.</summary>
public interface IQualityControlRepository
{
    void Add(QcMeter meter);

    void Add(QcLot lot);

    void Add(QcBook book);

    /// <summary>Mọi máy đo (kể cả máy ngưng sử dụng).</summary>
    Task<IReadOnlyList<QcMeter>> ListMetersAsync(CancellationToken cancellationToken);

    Task<QcMeter?> GetMeterAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> SerialExistsAsync(string serialNumber, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>Mọi lô que thử, dung dịch chứng (kể cả lô ngưng sử dụng).</summary>
    Task<IReadOnlyList<QcLot>> ListLotsAsync(CancellationToken cancellationToken);

    Task<QcLot?> GetLotAsync(Guid id, CancellationToken cancellationToken);

    Task<bool> LotExistsAsync(QcLotKind kind, string lotNumber, int? level, Guid? excludeId, CancellationToken cancellationToken);

    /// <summary>Sổ kèm mọi kết quả và lần xem xét.</summary>
    Task<QcBook?> GetBookAsync(Guid id, CancellationToken cancellationToken);

    Task<QcBook?> FindBookAsync(Guid meterId, Guid departmentId, int year, int month, CancellationToken cancellationToken);

    /// <summary>Các sổ của một tháng, kèm kết quả và lần xem xét (để đếm ngày đã ghi, đã xem xét).</summary>
    Task<IReadOnlyList<QcBook>> ListBooksAsync(int year, int month, CancellationToken cancellationToken);

    /// <summary>Kết quả còn hiệu lực ghi gần nhất của một máy (ở mọi sổ): gợi ý lô đang dùng khi ghi kết quả mới.</summary>
    Task<QcRun?> FindLatestRunAsync(Guid meterId, CancellationToken cancellationToken);
}

/// <summary>Cấp số phiếu tăng dần, không trùng (dùng sequence của CSDL).</summary>
public interface IConsentFormNumberGenerator
{
    Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}
