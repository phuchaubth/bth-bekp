using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

/// <summary>
/// Số liệu HIS của một ngày do một khoa nhập tay khi HIS lỗi (quyết định 35). Mỗi khoa nhập số thuộc nhiệm vụ của khoa:
/// khoa phải báo cáo nhập "NB PK chờ nhập viện" của khoa mình; khoa được Phòng Điều dưỡng giao nhập tổng số người bệnh đến
/// khám hoặc số liệu chẩn đoán hình ảnh. Phòng Điều dưỡng không nhập thay, chỉ tổng hợp: bảng tổng hợp dùng số lấy từ HIS
/// trước, thiếu thì dùng số khoa nhập. Không có thông tin người bệnh.
/// </summary>
public sealed class NursingDepartmentStatistics : Entity, IAuditableEntity
{
    public const int ValuesMaxLength = 2_000;

    private NursingDepartmentStatistics(Guid id, Guid departmentId, DateOnly reportDate)
        : base(id)
    {
        DepartmentId = departmentId;
        ReportDate = reportDate;
        ValuesJson = "{}";
    }

    public Guid DepartmentId { get; private set; }

    public DateOnly ReportDate { get; private set; }

    /// <summary>Số liệu dạng JSON; cấu trúc do tầng Application quy định.</summary>
    public string ValuesJson { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    /// <summary>Ủy quyền mà người nhập lần cuối dùng (quyết định 48); <c>null</c> khi người của khoa tự nhập.</summary>
    public Guid? DelegationId { get; private set; }

    public static NursingDepartmentStatistics Create(
        Guid departmentId, DateOnly reportDate, string valuesJson, Guid userId, DateTimeOffset nowUtc, Guid? delegationId = null)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Thiếu khoa nhập số liệu.");
        }

        var statistics = new NursingDepartmentStatistics(Guid.CreateVersion7(), departmentId, reportDate);
        statistics.Set(valuesJson, userId, nowUtc, delegationId);
        return statistics;
    }

    public void Set(string valuesJson, Guid userId, DateTimeOffset nowUtc, Guid? delegationId = null)
    {
        var values = (valuesJson ?? string.Empty).Trim();
        if (values.Length == 0 || values.Length > ValuesMaxLength)
        {
            throw new DomainException("Số liệu khoa nhập không hợp lệ.");
        }

        if (userId == Guid.Empty)
        {
            throw new DomainException("Thiếu người nhập số liệu.");
        }

        ValuesJson = values;
        UpdatedByUserId = userId;
        UpdatedAtUtc = nowUtc;
        DelegationId = delegationId;
    }
}
