using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Domain.Authorization;
using Consent.Domain.Identity;
using Consent.Domain.NursingReports;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.NursingReports;

/// <summary>Trạng thái ủy quyền so với hôm nay (giờ Việt Nam).</summary>
public enum NursingDelegationStatus
{
    /// <summary>Chưa tới ngày bắt đầu.</summary>
    Upcoming,

    /// <summary>Đang trong thời gian ủy quyền.</summary>
    Active,

    /// <summary>Đã qua ngày kết thúc.</summary>
    Expired,

    /// <summary>Đã hủy.</summary>
    Revoked,
}

/// <summary>
/// Một ủy quyền báo cáo điều dưỡng (quyết định 48). <see cref="DepartmentCode"/>: tên viết tắt của khoa (như bảng tổng hợp).
/// <see cref="Contents"/>: nội dung được ủy quyền, <c>report</c> (báo cáo của khoa) và/hoặc <c>hisEntry</c> (số liệu HIS khi
/// HIS lỗi). <see cref="CanRevoke"/>: người xem hủy được (người ủy quyền hoặc Phòng Điều dưỡng, ủy quyền chưa hủy, chưa hết hạn).
/// </summary>
public sealed record NursingDelegationDto(
    Guid Id,
    Guid DepartmentId,
    string DepartmentCode,
    string DepartmentName,
    Guid DelegatorUserId,
    string DelegatorName,
    Guid DelegateUserId,
    string DelegateName,
    DateOnly StartDate,
    DateOnly EndDate,
    IReadOnlyList<string> Contents,
    string? Reason,
    NursingDelegationStatus Status,
    DateTimeOffset CreatedAtUtc,
    string? RevokedBy,
    DateTimeOffset? RevokedAtUtc,
    bool CanRevoke);

/// <summary>
/// Ủy quyền mới: khoa (một khoa của người ủy quyền; bỏ trống là khoa chính), người được ủy quyền, từ ngày đến ngày (tính cả hai
/// ngày), nội dung (ít nhất một) và lý do (không bắt buộc).
/// </summary>
public sealed record CreateNursingDelegationRequest(
    Guid? DepartmentId,
    Guid DelegateUserId,
    DateOnly StartDate,
    DateOnly EndDate,
    bool Report,
    bool HisEntry,
    string? Reason);

/// <summary>Người có thể được ủy quyền: tài khoản đang dùng, có quyền lập báo cáo điều dưỡng và thuộc một khoa.</summary>
public sealed record NursingDelegateCandidateDto(Guid UserId, string FullName, string? Title, string DepartmentName);

/// <summary>Cách ghi người làm theo ủy quyền trên báo cáo khoa và bảng tổng hợp (quyết định 48).</summary>
public static class NursingDelegationText
{
    public const string ReportContent = "report";

    public const string HisEntryContent = "hisEntry";

    /// <summary>"Họ tên (được Người ủy quyền ủy quyền)"; không theo ủy quyền thì chỉ họ tên.</summary>
    public static string Person(string name, string? delegator) => delegator is null ? name : $"{name} (được {delegator} ủy quyền)";

    public static IReadOnlyList<string> Contents(NursingDelegationScope scope) =>
    [
        .. scope.HasFlag(NursingDelegationScope.Report) ? [ReportContent] : Array.Empty<string>(),
        .. scope.HasFlag(NursingDelegationScope.HisEntry) ? [HisEntryContent] : Array.Empty<string>(),
    ];
}

/// <summary>
/// Ủy quyền báo cáo điều dưỡng khi người báo cáo vắng (quyết định 48). Người lập báo cáo của khoa tự ủy quyền cho người khác làm
/// báo cáo của khoa và/hoặc nhập số liệu HIS của khoa, từ ngày đến ngày. Người được ủy quyền phải là tài khoản đang dùng có quyền
/// lập báo cáo điều dưỡng (thuộc khoa bất kỳ). Người ủy quyền hoặc Phòng Điều dưỡng hủy được ủy quyền chưa hết hạn. Phòng Điều
/// dưỡng xem được mọi ủy quyền. Quyền làm báo cáo theo ủy quyền được kiểm tra ở <see cref="NursingReportService"/>.
/// </summary>
public sealed class NursingDelegationService(
    INursingReportRepository reports,
    IDepartmentRepository departments,
    IUserRepository users,
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IClock clock,
    ISystemSettingRepository systemSettings)
{
    private static readonly Error Forbidden = Error.Forbidden(
        "nursing_delegations.forbidden", "Chỉ người lập báo cáo điều dưỡng của khoa ủy quyền được; Phòng Điều dưỡng xem được mọi ủy quyền.");

    private bool CanSubmit => currentUser.HasPermission(Permissions.NursingReportsSubmit);

    private bool CanManage => currentUser.HasPermission(Permissions.NursingReportsManage);

    private bool CanReadAll => currentUser.HasPermission(Permissions.NursingReportsRead) || CanManage;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>
    /// Ủy quyền người dùng đã tạo hoặc được giao, mới tạo trước; <paramref name="all"/> (Phòng Điều dưỡng) là mọi ủy quyền.
    /// </summary>
    public async Task<Result<IReadOnlyList<NursingDelegationDto>>> ListAsync(bool all, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || (all ? !CanReadAll : !(CanSubmit || CanReadAll)))
        {
            return Forbidden;
        }

        var items = await reports.ListDelegationsAsync(all ? null : userId, cancellationToken).ConfigureAwait(false);
        return Result.Success(await ToDtosAsync(items, cancellationToken).ConfigureAwait(false));
    }

    public async Task<Result<NursingDelegationDto>> CreateAsync(CreateNursingDelegationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CanSubmit || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var today = Today;

        // Khoa: một khoa của người ủy quyền (khoa chính hoặc khoa khác, quyết định 41). Không dùng phạm vi toàn viện: chỉ người của
        // khoa ủy quyền báo cáo của khoa.
        var departmentId = request.DepartmentId ?? currentUser.DepartmentId;
        var department = departmentId is { } id && currentUser.DepartmentIds.Contains(id)
            ? await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : null;
        if (department is not { IsActive: true })
        {
            errors["departmentId"] = ["Chọn một khoa của bạn (khoa đang dùng)."];
        }
        else
        {
            var reporting = (await reports.ListDepartmentsAsync(cancellationToken).ConfigureAwait(false)).Any(d => d.DepartmentId == department.Id);
            var hisLines = NursingHisEntry.LinesFor(department.Id, reporting, await AssignmentsAsync(cancellationToken).ConfigureAwait(false));
            if (!request.Report && !request.HisEntry)
            {
                errors["contents"] = ["Chọn ít nhất một nội dung ủy quyền."];
            }
            else if (request.Report && !reporting)
            {
                errors["contents"] = ["Khoa chưa có trong danh sách khoa báo cáo điều dưỡng nên không ủy quyền báo cáo của khoa được."];
            }
            else if (request.HisEntry && hisLines.Count == 0)
            {
                errors["contents"] = ["Khoa không có số liệu HIS phải nhập nên không ủy quyền nhập số liệu HIS được."];
            }
        }

        if (request.DelegateUserId == userId)
        {
            errors["delegateUserId"] = ["Không ủy quyền cho chính mình."];
        }
        else if (!await IsCandidateAsync(request.DelegateUserId, cancellationToken).ConfigureAwait(false))
        {
            errors["delegateUserId"] = ["Người được ủy quyền phải là tài khoản đang dùng, thuộc một khoa, có quyền lập báo cáo điều dưỡng."];
        }

        if (request.StartDate < today)
        {
            errors["startDate"] = ["Ngày bắt đầu từ hôm nay trở đi."];
        }
        else if (request.EndDate < request.StartDate)
        {
            errors["endDate"] = ["Ngày kết thúc phải sau hoặc bằng ngày bắt đầu."];
        }
        else if (request.EndDate.DayNumber - request.StartDate.DayNumber + 1 > NursingReportDelegation.MaxDays)
        {
            errors["endDate"] = [$"Ủy quyền tối đa {NursingReportDelegation.MaxDays} ngày."];
        }

        if (request.Reason?.Trim() is { Length: > NursingReportDelegation.ReasonMaxLength })
        {
            errors["reason"] = [$"Lý do tối đa {NursingReportDelegation.ReasonMaxLength} ký tự."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation", "Dữ liệu không hợp lệ.", errors);
        }

        var scope = (request.Report ? NursingDelegationScope.Report : NursingDelegationScope.None)
            | (request.HisEntry ? NursingDelegationScope.HisEntry : NursingDelegationScope.None);
        var delegation = NursingReportDelegation.Create(
            department!.Id, userId, request.DelegateUserId, request.StartDate, request.EndDate, scope, request.Reason, clock.UtcNow);
        reports.AddDelegation(delegation);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([delegation], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Hủy ủy quyền (người ủy quyền hoặc Phòng Điều dưỡng); báo cáo đã gửi theo ủy quyền vẫn ghi ủy quyền.</summary>
    public async Task<Result<NursingDelegationDto>> RevokeAsync(Guid id, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        var delegation = await reports.GetDelegationAsync(id, cancellationToken).ConfigureAwait(false);
        if (delegation is null || (delegation.DelegatorUserId != userId && delegation.DelegateUserId != userId && !CanReadAll))
        {
            return Error.NotFound("nursing_delegations.not_found", "Không tìm thấy ủy quyền.");
        }

        if (delegation.DelegatorUserId != userId && !CanManage)
        {
            return Error.Forbidden("nursing_delegations.forbidden", "Chỉ người ủy quyền hoặc Phòng Điều dưỡng hủy được ủy quyền.");
        }

        if (delegation.IsRevoked)
        {
            return Error.BusinessRule("nursing_delegations.revoked", "Ủy quyền đã được hủy.");
        }

        if (delegation.EndDate < Today)
        {
            return Error.BusinessRule("nursing_delegations.expired", "Ủy quyền đã hết hạn, không cần hủy.");
        }

        delegation.Revoke(userId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return (await ToDtosAsync([delegation], cancellationToken).ConfigureAwait(false))[0];
    }

    /// <summary>Người có thể được ủy quyền (trừ chính người dùng), theo tên.</summary>
    public async Task<Result<IReadOnlyList<NursingDelegateCandidateDto>>> CandidatesAsync(CancellationToken cancellationToken)
    {
        if (!CanSubmit || currentUser.UserId is not { } userId)
        {
            return Forbidden;
        }

        var names = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id, d => d.Name);
        IReadOnlyList<NursingDelegateCandidateDto> candidates = [.. (await CandidateUsersAsync(cancellationToken).ConfigureAwait(false))
            .Where(u => u.Id != userId)
            .OrderBy(u => u.FullName, VietnameseComparer.Instance)
            .Select(u => new NursingDelegateCandidateDto(u.Id, u.FullName, u.Title, names.GetValueOrDefault(u.DepartmentId!.Value, string.Empty)))];
        return Result.Success(candidates);
    }

    /// <summary>Tài khoản đang dùng, thuộc một khoa, có vai trò được quyền lập báo cáo điều dưỡng.</summary>
    private async Task<IReadOnlyList<User>> CandidateUsersAsync(CancellationToken cancellationToken)
    {
        var roleIds = (await roles.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => r.Permissions.Any(p => p.PermissionCode == Permissions.NursingReportsSubmit))
            .Select(r => r.Id)
            .ToList();
        return [.. (await users.ListActiveInRolesAsync(roleIds, cancellationToken).ConfigureAwait(false)).Where(u => u.DepartmentId is not null)];
    }

    private async Task<bool> IsCandidateAsync(Guid userId, CancellationToken cancellationToken) =>
        (await CandidateUsersAsync(cancellationToken).ConfigureAwait(false)).Any(u => u.Id == userId);

    private async Task<IReadOnlyDictionary<string, Guid>> AssignmentsAsync(CancellationToken cancellationToken) =>
        NursingHisEntry.ParseAssignments(
            (await systemSettings.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(s => s.Key == SettingKeys.NursingHisEntryDepartments)?.Value);

    private async Task<IReadOnlyList<NursingDelegationDto>> ToDtosAsync(
        IReadOnlyList<NursingReportDelegation> delegations, CancellationToken cancellationToken)
    {
        var allDepartments = (await departments.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false)).ToDictionary(d => d.Id);
        var names = new Dictionary<Guid, string>();
        foreach (var id in delegations.SelectMany(d => new[] { d.DelegatorUserId, d.DelegateUserId, d.RevokedByUserId ?? Guid.Empty }).Distinct())
        {
            if (id != Guid.Empty && await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) is { } user)
            {
                names[id] = user.FullName;
            }
        }

        var today = Today;
        var userId = currentUser.UserId;
        return [.. delegations.Select(d =>
        {
            var status = d.IsRevoked ? NursingDelegationStatus.Revoked
                : today < d.StartDate ? NursingDelegationStatus.Upcoming
                : today > d.EndDate ? NursingDelegationStatus.Expired
                : NursingDelegationStatus.Active;
            allDepartments.TryGetValue(d.DepartmentId, out var department);
            return new NursingDelegationDto(
                d.Id,
                d.DepartmentId,
                department?.ShortName ?? department?.Code ?? string.Empty,
                department?.Name ?? string.Empty,
                d.DelegatorUserId,
                names.GetValueOrDefault(d.DelegatorUserId, "Không rõ"),
                d.DelegateUserId,
                names.GetValueOrDefault(d.DelegateUserId, "Không rõ"),
                d.StartDate,
                d.EndDate,
                NursingDelegationText.Contents(d.Scope),
                d.Reason,
                status,
                d.CreatedAtUtc,
                d.RevokedByUserId is { } revokedBy ? names.GetValueOrDefault(revokedBy, "Không rõ") : null,
                d.RevokedAtUtc,
                CanRevoke: status is NursingDelegationStatus.Upcoming or NursingDelegationStatus.Active
                    && (d.DelegatorUserId == userId || CanManage));
        })];
    }
}
