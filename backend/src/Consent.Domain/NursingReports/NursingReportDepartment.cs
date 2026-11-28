using Consent.Domain.Common;

namespace Consent.Domain.NursingReports;

/// <summary>
/// Một khoa phải gửi báo cáo điều dưỡng hằng ngày, kèm số giường thực kê (không tính băng ca) in ở bảng tổng hợp. Phòng
/// Điều dưỡng cài cho từng khoa và sửa khi khoa đổi số giường (quyết định 31), xếp thứ tự các khoa trên bảng tổng hợp (quyết
/// định 45).
/// </summary>
public sealed class NursingReportDepartment : Entity, IAuditableEntity
{
    public const int MaxBedCount = 10_000;

    private NursingReportDepartment(Guid id, Guid departmentId, int bedCount, int sortOrder, DateTimeOffset updatedAtUtc)
        : base(id)
    {
        DepartmentId = departmentId;
        BedCount = bedCount;
        SortOrder = sortOrder;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid DepartmentId { get; private set; }

    /// <summary>Số giường thực kê, không tính băng ca.</summary>
    public int BedCount { get; private set; }

    /// <summary>
    /// Thứ tự trên bảng tổng hợp (1 là đầu bảng). 0 là chưa xếp (khoa cài trước quyết định 45): đứng đầu, xếp theo tên viết tắt
    /// như trước, cho tới khi Phòng Điều dưỡng lưu lại danh sách.
    /// </summary>
    public int SortOrder { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static NursingReportDepartment Create(Guid departmentId, int bedCount, DateTimeOffset nowUtc, int sortOrder = 0)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Thiếu khoa báo cáo.");
        }

        EnsureBedCount(bedCount);
        EnsureSortOrder(sortOrder);
        return new NursingReportDepartment(Guid.CreateVersion7(), departmentId, bedCount, sortOrder, nowUtc);
    }

    public void SetSortOrder(int sortOrder, DateTimeOffset nowUtc)
    {
        EnsureSortOrder(sortOrder);
        if (SortOrder != sortOrder)
        {
            SortOrder = sortOrder;
            UpdatedAtUtc = nowUtc;
        }
    }

    private static void EnsureSortOrder(int sortOrder)
    {
        if (sortOrder < 0)
        {
            throw new DomainException("Thứ tự khoa không được âm.");
        }
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
