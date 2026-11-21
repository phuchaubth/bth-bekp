using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

/// <summary>Nội dung báo cáo điều dưỡng được ủy quyền (quyết định 48); chọn được cả hai.</summary>
[Flags]
public enum NursingDelegationScope
{
    None = 0,

    /// <summary>Báo cáo công tác điều dưỡng của khoa: lập, lưu nháp, gửi.</summary>
    Report = 1,

    /// <summary>Số liệu HIS khoa nhập tay khi HIS lỗi (quyết định 35).</summary>
    HisEntry = 2,
}

/// <summary>
/// Ủy quyền báo cáo điều dưỡng của khoa (quyết định 48). Người báo cáo của khoa (người ủy quyền) vắng thì tự vào phần mềm ủy
/// quyền cho người khác (người được ủy quyền) làm các nội dung đã chọn của khoa, từ ngày đến ngày (tính cả hai ngày). Người được
/// ủy quyền chỉ làm được trong thời gian đó, với báo cáo của các ngày trong thời gian đó. Báo cáo gửi theo ủy quyền ghi lại ủy
/// quyền để bảng tổng hợp ghi rõ. Không xóa ủy quyền: hủy thì ghi người hủy, lúc hủy (báo cáo đã gửi theo ủy quyền vẫn giữ).
/// </summary>
public sealed class NursingReportDelegation : Entity, IAuditableEntity
{
    public const int ReasonMaxLength = 200;

    /// <summary>Một lần ủy quyền dài tối đa bấy nhiêu ngày (tính cả hai ngày).</summary>
    public const int MaxDays = 366;

    private const NursingDelegationScope AllScopes = NursingDelegationScope.Report | NursingDelegationScope.HisEntry;

    private NursingReportDelegation(Guid id)
        : base(id)
    {
    }

    /// <summary>Khoa được ủy quyền báo cáo.</summary>
    public Guid DepartmentId { get; private set; }

    public Guid DelegatorUserId { get; private set; }

    public Guid DelegateUserId { get; private set; }

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public NursingDelegationScope Scope { get; private set; }

    /// <summary>Lý do (ví dụ nghỉ phép); không bắt buộc.</summary>
    public string? Reason { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Guid? RevokedByUserId { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public static NursingReportDelegation Create(
        Guid departmentId,
        Guid delegatorUserId,
        Guid delegateUserId,
        DateOnly startDate,
        DateOnly endDate,
        NursingDelegationScope scope,
        string? reason,
        DateTimeOffset nowUtc)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Thiếu khoa được ủy quyền báo cáo.");
        }

        if (delegatorUserId == Guid.Empty || delegateUserId == Guid.Empty)
        {
            throw new DomainException("Thiếu người ủy quyền hoặc người được ủy quyền.");
        }

        if (delegatorUserId == delegateUserId)
        {
            throw new DomainException("Không ủy quyền cho chính mình.");
        }

        if (endDate < startDate)
        {
            throw new DomainException("Ngày kết thúc phải sau hoặc bằng ngày bắt đầu.");
        }

        if (endDate.DayNumber - startDate.DayNumber + 1 > MaxDays)
        {
            throw new DomainException($"Ủy quyền tối đa {MaxDays} ngày.");
        }

        if (scope == NursingDelegationScope.None || (scope & ~AllScopes) != NursingDelegationScope.None)
        {
            throw new DomainException("Chọn ít nhất một nội dung ủy quyền.");
        }

        var text = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (text is { Length: > ReasonMaxLength })
        {
            throw new DomainException($"Lý do tối đa {ReasonMaxLength} ký tự.");
        }

        return new NursingReportDelegation(Guid.CreateVersion7())
        {
            DepartmentId = departmentId,
            DelegatorUserId = delegatorUserId,
            DelegateUserId = delegateUserId,
            StartDate = startDate,
            EndDate = endDate,
            Scope = scope,
            Reason = text,
            CreatedAtUtc = nowUtc,
        };
    }

    /// <summary>Ủy quyền chưa hủy, có ngày <paramref name="date"/> và có đủ nội dung <paramref name="scope"/>.</summary>
    public bool Covers(DateOnly date, NursingDelegationScope scope) =>
        !IsRevoked && scope != NursingDelegationScope.None && (Scope & scope) == scope && date >= StartDate && date <= EndDate;

    /// <summary>Hủy ủy quyền (người ủy quyền hoặc Phòng Điều dưỡng); báo cáo đã gửi theo ủy quyền vẫn giữ.</summary>
    public void Revoke(Guid userId, DateTimeOffset nowUtc)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người hủy ủy quyền.");
        }

        if (IsRevoked)
        {
            throw new DomainException("Ủy quyền đã được hủy.");
        }

        RevokedByUserId = userId;
        RevokedAtUtc = nowUtc;
    }
}
