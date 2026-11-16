using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

/// <summary>
/// Một khoa phải gửi báo cáo điều dưỡng hằng ngày, kèm số giường thực kê (không tính băng ca) in ở bảng tổng hợp. Phòng
/// Điều dưỡng cài cho từng khoa và sửa khi khoa đổi số giường (quyết định 31).
/// </summary>
public sealed class NursingReportDepartment : Entity, IAuditableEntity
{
    public const int MaxBedCount = 10_000;

    private NursingReportDepartment(Guid id, Guid departmentId, int bedCount, DateTimeOffset updatedAtUtc)
        : base(id)
    {
        DepartmentId = departmentId;
        BedCount = bedCount;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid DepartmentId { get; private set; }

    /// <summary>Số giường thực kê, không tính băng ca.</summary>
    public int BedCount { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NursingReportDepartment Create(Guid departmentId, int bedCount, DateTimeOffset nowUtc)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Thiếu khoa báo cáo.");
        }

        EnsureBedCount(bedCount);
        return new NursingReportDepartment(Guid.CreateVersion7(), departmentId, bedCount, nowUtc);
    }

    public void SetBedCount(int bedCount, DateTimeOffset nowUtc)
    {
        EnsureBedCount(bedCount);
        if (BedCount != bedCount)
        {
            BedCount = bedCount;
            UpdatedAtUtc = nowUtc;
        }
    }

    private static void EnsureBedCount(int bedCount)
    {
        if (bedCount is < 0 or > MaxBedCount)
        {
            throw new DomainException($"Số giường phải từ 0 đến {MaxBedCount}.");
        }
    }
}
