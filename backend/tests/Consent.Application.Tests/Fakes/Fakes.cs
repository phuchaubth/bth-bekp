using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Application.Auditing;
using Consent.Application.Exports;
using Consent.Application.Reports;
using Consent.Domain.Exports;
using Consent.Domain.Settings;
using Consent.Application.Patients;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;
using Consent.Application.ConsentForms;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;

namespace Consent.Application.Tests.Fakes;

internal sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Băm giả lập: "hashed:" + mật khẩu; "legacy:" + mật khẩu mô phỏng cần băm lại.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hashed:" + password;

    public PasswordVerification Verify(string passwordHash, string password) =>
        passwordHash == "hashed:" + password ? PasswordVerification.Success
        : passwordHash == "legacy:" + password ? PasswordVerification.SuccessRehashNeeded
        : PasswordVerification.Failed;
}

internal sealed class FakeTokenService(IClock clock) : ITokenService
{
    private int _counter;

    public AccessTokenClaims? LastClaims { get; private set; }

    public AccessToken CreateAccessToken(AccessTokenClaims claims)
    {
        LastClaims = claims;
        return new AccessToken($"access-{++_counter}", clock.UtcNow.AddMinutes(15));
    }

    public SigningTokenClaims? LastSigningClaims { get; private set; }

    public AccessToken CreateSigningToken(SigningTokenClaims claims)
    {
        LastSigningClaims = claims;
        return new AccessToken($"signing-{claims.FormId:N}-{claims.SessionId:N}", clock.UtcNow.AddMinutes(30));
    }

    public string GenerateRefreshToken() => "refresh-" + Guid.NewGuid().ToString("N");

    public string HashRefreshToken(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken))).ToLowerInvariant();
}

internal sealed class FakeRequestContext : IRequestContext
{
    public string? IpAddress => "127.0.0.1";

    public string? UserAgent => "unit-test";

    public string? CorrelationId => "trace-unit-test";
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public bool IsAuthenticated => UserId is not null;

    public string? UserName { get; set; }

    public Guid? DepartmentId { get; set; }

    public HashSet<string> Permissions { get; } = [];

    public HashSet<string> Roles { get; } = [];

    public Guid? SigningFormId { get; set; }

    public Guid? SigningSessionId { get; set; }

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public bool IsInRole(string roleCode) => Roles.Contains(roleCode);
}

/// <summary>"Cơ sở dữ liệu" trong bộ nhớ dùng chung cho các repository giả.</summary>
internal sealed class InMemoryStore : IUnitOfWork
{
    public List<User> Users { get; } = [];

    public List<Role> Roles { get; } = [];

    public List<Department> Departments { get; } = [];

    public List<RefreshToken> RefreshTokens { get; } = [];

    public List<AuditLog> AuditLogs { get; } = [];

    public List<Patient> Patients { get; } = [];

    public List<ConsentTemplate> ConsentTemplates { get; } = [];

    public List<ConsentForm> ConsentForms { get; } = [];

    public List<ExportJob> ExportJobs { get; } = [];

    public List<SystemSetting> SystemSettings { get; } = [];

    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryUserRepository(InMemoryStore store) : IUserRepository
{
    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Find(u => u.Id == id));

    public Task<User?> GetByUserNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Find(u => u.UserName == normalizedUserName));

    public Task<bool> UserNameExistsAsync(string normalizedUserName, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Exists(u => u.UserName == normalizedUserName));

    public Task<bool> AnyInDepartmentAsync(Guid departmentId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Exists(u => u.DepartmentId == departmentId));

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => Task.FromResult(store.Users.Count > 0);

    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludeUserId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Users.Count(u => u.IsActive && u.Id != excludeUserId && u.RoleIds.Contains(roleId)));

    public Task<PagedResult<UserListItemDto>> SearchAsync(UserQuery query, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var items = store.Users
            .Where(u => query.Keyword is null || u.FullName.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase))
            .Select(u => new UserListItemDto(u.Id, u.UserName, u.FullName, u.Title, u.DepartmentId, null, [], u.IsActive, u.IsLockedOut(nowUtc), u.LastLoginAtUtc))
            .ToList();
        return Task.FromResult(new PagedResult<UserListItemDto>(items, query.Page, query.PageSize, items.Count));
    }

    public void Add(User user) => store.Users.Add(user);
}

internal sealed class InMemoryRoleRepository(InMemoryStore store) : IRoleRepository
{
    public Task<IReadOnlyList<Role>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. store.Roles]);

    public Task<Role?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Roles.Find(r => r.Id == id));

    public Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. store.Roles.Where(r => ids.Contains(r.Id))]);
}

internal sealed class InMemoryDepartmentRepository(InMemoryStore store) : IDepartmentRepository
{
    public Task<IReadOnlyList<Department>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Department>>([.. store.Departments.Where(d => includeInactive || d.IsActive)]);

    public Task<Department?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Departments.Find(d => d.Id == id));

    public Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Departments.Exists(d => d.Code == normalizedCode && d.Id != excludeId));

    public void Add(Department department) => store.Departments.Add(department);

    public void Remove(Department department) => store.Departments.Remove(department);
}

internal sealed class InMemoryRefreshTokenRepository(InMemoryStore store) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(store.RefreshTokens.Find(t => t.TokenHash == tokenHash));

    public Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>([.. store.RefreshTokens.Where(t => t.FamilyId == familyId && !t.IsRevoked)]);

    public Task<IReadOnlyList<RefreshToken>> GetActiveByUserAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RefreshToken>>([.. store.RefreshTokens.Where(t => t.UserId == userId && !t.IsRevoked)]);

    public void Add(RefreshToken token) => store.RefreshTokens.Add(token);
}

internal sealed class InMemoryAuditLogRepository(InMemoryStore store) : IAuditLogRepository
{
    public void Add(AuditLog auditLog) => store.AuditLogs.Add(auditLog);

    public Task<PagedResult<AuditLogDto>> SearchAsync(AuditLogQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<AuditLogDto>([], query.Page, query.PageSize, 0));
}

internal sealed class InMemoryPatientRepository(InMemoryStore store) : IPatientRepository
{
    public Task<Patient?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.Patients.Find(p => p.Id == id));

    public Task<Patient?> FindByCitizenIdAsync(string citizenId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Patients.Find(p => p.CitizenId == citizenId));

    public Task<bool> PatientCodeExistsAsync(string patientCode, Guid? excludeId, CancellationToken cancellationToken) =>
        Task.FromResult(store.Patients.Exists(p => p.PatientCode == patientCode && p.Id != excludeId));

    public Task<PagedResult<PatientSummary>> SearchAsync(PatientQuery query, CancellationToken cancellationToken)
    {
        var items = store.Patients
            .Select(p => new PatientSummary(p.Id, p.PatientCode, p.FullName, p.DateOfBirth, p.Gender, p.CitizenId, p.HisVerifiedAtUtc, p.UpdatedAtUtc))
            .ToList();
        return Task.FromResult(new PagedResult<PatientSummary>(items, query.Page, query.PageSize, items.Count));
    }

    public void Add(Patient patient) => store.Patients.Add(patient);
}

/// <summary>HIS giả cho kiểm thử: trả kết quả cấu hình sẵn, có thể mô phỏng lỗi hoặc treo (để kiểm tra timeout).</summary>
internal sealed class StubHisGateway : IHisGateway
{
    public bool IsEnabled { get; set; } = true;

    public HisLookupResult Result { get; set; } = HisLookupResult.NotFound;

    public bool Throw { get; set; }

    public bool Hang { get; set; }

    public int Calls { get; private set; }

    public async Task<HisLookupResult> FindPatientByCitizenIdAsync(string citizenId, CancellationToken cancellationToken)
    {
        Calls++;
        if (Throw)
        {
            throw new HttpRequestException("HIS lỗi (giả lập).");
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        return Result;
    }

    public static HisLookupResult Found(string patientCode) => new(
        HisLookupStatus.Found,
        new HisPatient(patientCode, null, null, "BHYT", "DN4790000000002", null, new HisEncounter("TN001", null, "Khoa Khám bệnh", "Phòng khám 1")));
}

internal sealed class InMemoryConsentTemplateRepository(InMemoryStore store) : IConsentTemplateRepository
{
    public Task<IReadOnlyList<ConsentTemplate>> ListAsync(bool includeInactive, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConsentTemplate>>(store.ConsentTemplates.Where(t => includeInactive || t.IsActive).ToList());

    public Task<ConsentTemplate?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.ConsentTemplates.FirstOrDefault(t => t.Id == id));

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(store.ConsentTemplates.Any(t => t.Code == code));

    public Task<ConsentTemplateVersion?> GetVersionAsync(Guid versionId, CancellationToken cancellationToken) =>
        Task.FromResult(store.ConsentTemplates.SelectMany(t => t.Versions).FirstOrDefault(v => v.Id == versionId));

    public void Add(ConsentTemplate template) => store.ConsentTemplates.Add(template);
}

internal sealed class InMemoryConsentFormRepository(InMemoryStore store) : IConsentFormRepository
{
    public Task<ConsentForm?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(store.ConsentForms.FirstOrDefault(f => f.Id == id));

    public Task<PagedResult<ConsentFormListItemDto>> SearchAsync(ConsentFormSearch query, CancellationToken cancellationToken)
    {
        var rows = Filter(query).OrderByDescending(f => f.CreatedAtUtc).ToList();
        var items = rows
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(f =>
            {
                var template = store.ConsentTemplates.Single(t => t.Id == f.TemplateId);
                var patient = store.Patients.Single(p => p.Id == f.PatientId);
                return new ConsentFormListItemDto(
                    f.Id, f.FormNo, f.Status, template.Id, template.Code, template.Name,
                    template.Versions.Single(v => v.Id == f.TemplateVersionId).VersionNo,
                    patient.Id, patient.FullName, patient.PatientCode, f.DepartmentId,
                    store.Departments.FirstOrDefault(d => d.Id == f.DepartmentId)?.Name ?? string.Empty,
                    store.Users.FirstOrDefault(u => u.Id == f.CreatedByUserId)?.FullName ?? string.Empty,
                    f.CreatedAtUtc, f.UpdatedAtUtc);
            })
            .ToList();
        return Task.FromResult(new PagedResult<ConsentFormListItemDto>(items, query.Page, query.PageSize, rows.Count));
    }

    public Task<ConsentFormRef?> FindReplacementAsync(Guid formId, CancellationToken cancellationToken) =>
        Task.FromResult(store.ConsentForms.Where(f => f.ReplacesFormId == formId).Select(f => new ConsentFormRef(f.Id, f.FormNo))
            .FirstOrDefault());

    public Task<int> CountAsync(ConsentFormSearch query, CancellationToken cancellationToken) => Task.FromResult(Filter(query).Count());

    public async IAsyncEnumerable<ConsentFormExportRecord> StreamForExportAsync(
        ConsentFormSearch query, bool includeData, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var page = Filter(query).OrderBy(f => f.CreatedAtUtc).ThenBy(f => f.FormNo, StringComparer.Ordinal)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        foreach (var f in page)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var template = store.ConsentTemplates.Single(t => t.Id == f.TemplateId);
            var patient = store.Patients.Single(p => p.Id == f.PatientId);
            var representative = patient.Representatives.FirstOrDefault(r => r.Id == f.RepresentativeId);
            var hospital = f.Signatures.FirstOrDefault(s => s.Role == ConsentSignerRole.Hospital);
            yield return new ConsentFormExportRecord
            {
                Id = f.Id,
                FormNo = f.FormNo,
                Status = f.Status,
                TemplateCode = template.Code,
                TemplateName = template.Name,
                TemplateVersionId = f.TemplateVersionId,
                TemplateVersionNo = template.Versions.Single(v => v.Id == f.TemplateVersionId).VersionNo,
                DepartmentName = store.Departments.FirstOrDefault(d => d.Id == f.DepartmentId)?.Name ?? string.Empty,
                CreatedByName = store.Users.FirstOrDefault(u => u.Id == f.CreatedByUserId)?.FullName ?? string.Empty,
                CreatedAtUtc = f.CreatedAtUtc,
                SubmittedAtUtc = f.SubmittedAtUtc,
                CompletedAtUtc = f.CompletedAtUtc,
                CancelledAtUtc = f.CancelledAtUtc,
                CancelReason = f.CancelReason,
                RevokedAtUtc = f.RevokedAtUtc,
                RevokeReason = f.RevokeReason,
                Decision = f.Decision,
                SignerKind = f.SignerKind,
                PatientId = patient.Id,
                PatientName = patient.FullName,
                PatientCode = patient.PatientCode,
                PatientDateOfBirth = patient.DateOfBirth,
                PatientGender = patient.Gender,
                PatientCitizenId = patient.CitizenId,
                PatientPhoneNumber = patient.PhoneNumber,
                PatientAddress = patient.CurrentAddress ?? patient.PermanentAddress,
                RepresentativeId = representative?.Id,
                RepresentativeName = representative?.FullName,
                RepresentativeRelationship = representative?.Relationship,
                RepresentativeCitizenId = representative?.CitizenId,
                RepresentativePhoneNumber = representative?.PhoneNumber,
                SnapshotJson = f.SnapshotJson,
                PatientSignedAtUtc = f.Signatures.FirstOrDefault(s => s.Role != ConsentSignerRole.Hospital)?.SignedAtUtc,
                HospitalSignerName = hospital?.SignerName,
                HospitalSignerTitle = hospital?.SignerTitle,
                ReplacesFormNo = store.ConsentForms.FirstOrDefault(o => o.Id == f.ReplacesFormId)?.FormNo,
                SignedPdfSha256 = f.SignedPdfSha256,
                DataJson = includeData ? f.DataJson : null,
            };
        }
    }

    public void Add(ConsentForm form) => store.ConsentForms.Add(form);

    private IEnumerable<ConsentForm> Filter(ConsentFormSearch query) => Filter(store, query);

    internal static IEnumerable<ConsentForm> Filter(InMemoryStore store, ConsentFormSearch query) => store.ConsentForms
        .Where(f => query.ScopeAll || f.DepartmentId == query.ScopeDepartmentId || f.CreatedByUserId == query.ScopeUserId)
        .Where(f => query.Status is null || f.Status == query.Status)
        .Where(f => query.PatientId is null || f.PatientId == query.PatientId)
        .Where(f => query.TemplateId is null || f.TemplateId == query.TemplateId)
        .Where(f => query.DepartmentId is null || f.DepartmentId == query.DepartmentId)
        .Where(f => query.FromUtc is null || f.CreatedAtUtc >= query.FromUtc)
        .Where(f => query.ToUtc is null || f.CreatedAtUtc < query.ToUtc);
}

/// <summary>Thống kê tính trên dữ liệu trong bộ nhớ, cùng quy ước với bản SQL (chỉ số ngày = số giờ kể từ mốc / 24).</summary>
internal sealed class InMemoryConsentStatisticsRepository(InMemoryStore store) : IConsentStatisticsRepository
{
    public Task<IReadOnlyList<ConsentFormGroupCount>> CountByGroupAsync(ConsentFormSearch query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ConsentFormGroupCount>>([.. InMemoryConsentFormRepository.Filter(store, query)
            .GroupBy(f => (f.DepartmentId, f.TemplateId, f.Status, f.Decision, f.SignerKind))
            .Select(g => new ConsentFormGroupCount(g.Key.DepartmentId, g.Key.TemplateId, g.Key.Status, g.Key.Decision, g.Key.SignerKind, g.Count()))]);

    public Task<IReadOnlyList<DailyStatusCount>> CountByCreatedDayAsync(
        ConsentFormSearch query, DateTimeOffset originUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DailyStatusCount>>([.. InMemoryConsentFormRepository.Filter(store, query)
            .Where(f => f.CreatedAtUtc >= originUtc)
            .GroupBy(f => (Day: Day(originUtc, f.CreatedAtUtc), f.Status))
            .Select(g => new DailyStatusCount(g.Key.Day, g.Key.Status, g.Count()))]);

    public Task<IReadOnlyList<DailyCount>> CountCompletedByDayAsync(
        ConsentFormSearch scope, DateTimeOffset originUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<DailyCount>>([.. InMemoryConsentFormRepository.Filter(store, scope)
            .Where(f => f.CompletedAtUtc >= originUtc && f.CompletedAtUtc < toUtc)
            .GroupBy(f => Day(originUtc, f.CompletedAtUtc!.Value))
            .Select(g => new DailyCount(g.Key, g.Count()))]);

    public Task<IReadOnlyList<int>> CompletionSecondsAsync(ConsentFormSearch query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<int>>([.. InMemoryConsentFormRepository.Filter(store, query)
            .Where(f => f.Status == ConsentFormStatus.Completed && f.CompletedAtUtc is not null)
            .Select(f => (int)(f.CompletedAtUtc!.Value - f.CreatedAtUtc).TotalSeconds)]);

    public Task<int> CountClosedAsync(
        ConsentFormSearch scope, bool revoked, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken) =>
        Task.FromResult(InMemoryConsentFormRepository.Filter(store, scope)
            .Count(f => revoked ? f.RevokedAtUtc >= fromUtc && f.RevokedAtUtc < toUtc : f.CompletedAtUtc >= fromUtc && f.CompletedAtUtc < toUtc));

    public Task<DashboardFormList> AwaitingConfirmationAsync(
        ConsentFormSearch scope, IReadOnlyCollection<Guid> templateIds, int take, CancellationToken cancellationToken)
    {
        var forms = InMemoryConsentFormRepository.Filter(store, scope)
            .Where(f => f.Status == ConsentFormStatus.PendingSignature && templateIds.Contains(f.TemplateId) && f.PatientSignature is not null)
            .OrderBy(f => f.PatientSignature!.SignedAtUtc)
            .ToList();
        return Task.FromResult(new DashboardFormList(forms.Count, [.. forms.Take(take).Select(f => Item(f, f.PatientSignature!.SignedAtUtc))]));
    }

    public Task<DashboardFormList> OpenFormsCreatedByAsync(Guid userId, int take, CancellationToken cancellationToken)
    {
        var forms = store.ConsentForms
            .Where(f => f.CreatedByUserId == userId && f.Status is ConsentFormStatus.Draft or ConsentFormStatus.PendingSignature)
            .OrderByDescending(f => f.UpdatedAtUtc)
            .ToList();
        return Task.FromResult(new DashboardFormList(forms.Count, [.. forms.Take(take).Select(f => Item(f, f.UpdatedAtUtc))]));
    }

    private static int Day(DateTimeOffset origin, DateTimeOffset at) => (int)Math.Floor((at - origin).TotalHours) / 24;

    private DashboardFormItem Item(ConsentForm f, DateTimeOffset at)
    {
        var template = store.ConsentTemplates.Single(t => t.Id == f.TemplateId);
        return new DashboardFormItem(
            f.Id, f.FormNo, f.Status, store.Patients.Single(p => p.Id == f.PatientId).FullName, template.Code, template.Name,
            store.Departments.FirstOrDefault(d => d.Id == f.DepartmentId)?.Name ?? string.Empty, at);
    }
}

internal sealed class InMemorySystemSettingRepository(InMemoryStore store) : ISystemSettingRepository
{
    public Task<IReadOnlyList<SystemSetting>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SystemSetting>>([.. store.SystemSettings]);

    public void Add(SystemSetting setting) => store.SystemSettings.Add(setting);
}

internal sealed class InMemoryExportJobRepository(InMemoryStore store) : IExportJobRepository
{
    public void Add(ExportJob job) => store.ExportJobs.Add(job);

    public Task<PagedResult<ExportJobRecord>> SearchAsync(ExportJobSearch query, CancellationToken cancellationToken)
    {
        var jobs = store.ExportJobs
            .Where(j => query.RequestedByUserId is null || j.RequestedByUserId == query.RequestedByUserId)
            .OrderByDescending(j => j.RequestedAtUtc)
            .ToList();
        var items = jobs.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(j => new ExportJobRecord(
                j.Id, j.RequestedAtUtc, j.RequestedByUserId, store.Users.FirstOrDefault(u => u.Id == j.RequestedByUserId)?.FullName ?? string.Empty,
                j.Format, j.FiltersJson, j.Columns, j.IncludesContent, j.IncludesSensitiveData, j.RowCount, j.FileName, j.FileSizeBytes,
                j.FileSha256, j.DurationMs))
            .ToList();
        return Task.FromResult(new PagedResult<ExportJobRecord>(items, query.Page, query.PageSize, jobs.Count));
    }
}

/// <summary>Bộ ghi bảng giả: giữ lại tiêu đề và giá trị từng ô để kiểm tra; ghi một ít byte để có mã băm.</summary>
internal sealed class RecordingTabularWriter(ExportFormat format) : ITabularFileWriter
{
    public ExportFormat Format => format;

    public string ContentType => "application/x-test";

    public string FileExtension => format == ExportFormat.Xlsx ? "xlsx" : "csv";

    public IReadOnlyList<TabularColumn> Columns { get; private set; } = [];

    public List<IReadOnlyList<object?>> Rows { get; } = [];

    public IReadOnlyList<KeyValuePair<string, string>> Properties { get; private set; } = [];

    public IReadOnlyList<string> Headers => [.. Columns.Select(c => c.Header)];

    public object? Cell(int row, string header) => Rows[row][Headers.ToList().IndexOf(header)];

    public async Task<int> WriteAsync(TabularData data, Stream output, CancellationToken cancellationToken)
    {
        Columns = data.Columns;
        Properties = data.Properties;
        Rows.Clear();
        await foreach (var row in data.Rows.WithCancellation(cancellationToken))
        {
            Rows.Add(row);
        }

        await output.WriteAsync(Encoding.UTF8.GetBytes($"{format}:{Rows.Count}"), cancellationToken);
        return Rows.Count;
    }
}

internal sealed class SequentialFormNumbers : IConsentFormNumberGenerator
{
    private long _next;

    public Task<string> NextAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        Task.FromResult($"PDT-{nowUtc.Year}-{Interlocked.Increment(ref _next):000000}");
}

/// <summary>Kho tài liệu trong bộ nhớ; <see cref="Tamper"/> giả lập file bị sửa sau khi lưu.</summary>
internal sealed class InMemoryDocumentStore : IDocumentStore
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var key = $"2026/11/{Path.GetFileNameWithoutExtension(fileName)}-{Files.Count + 1}.pdf.enc";
        Files[key] = [.. content];
        return Task.FromResult(key);
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken) =>
        Files.TryGetValue(key, out var content)
            ? Task.FromResult<byte[]>([.. content])
            : throw new DocumentUnavailableException("Không tìm thấy file đã lưu.");

    public void Tamper(string key) => Files[key] = [.. Files[key], 0x20];
}

/// <summary>Ghi lại nội dung được yêu cầu in, trả về PDF giả.</summary>
internal sealed class RecordingPdfRenderer : IConsentPdfRenderer
{
    public ConsentPdfDocument? Last { get; private set; }

    public byte[] Render(ConsentPdfDocument document)
    {
        Last = document;
        return "%PDF-giả"u8.ToArray();
    }
}
