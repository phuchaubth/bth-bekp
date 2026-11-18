namespace Consent.Application.Abstractions;

/// <summary>Một khoa/phòng trong danh mục của HIS (quyết định 42): mã và tên như HIS trả về.</summary>
public sealed record ExternalDepartment(string Code, string Name);

public enum DepartmentSourceStatus
{
    Found,

    /// <summary>API thông tin người bệnh chưa cấu hình câu truy vấn danh mục khoa/phòng (<c>PatientInfo__DepartmentQuery</c>).</summary>
    NotConfigured,

    /// <summary>Không lấy được (lỗi mạng, quá thời gian, CSDL bệnh viện lỗi, phản hồi lạ).</summary>
    Unavailable,

    /// <summary>Chưa cấu hình API thông tin người bệnh (<c>PatientSource:Provider</c> khác <c>Api</c>).</summary>
    Disabled,
}

public sealed record DepartmentSourceResult(DepartmentSourceStatus Status, IReadOnlyList<ExternalDepartment> Items)
{
    public static DepartmentSourceResult NotConfigured { get; } = new(DepartmentSourceStatus.NotConfigured, []);

    public static DepartmentSourceResult Unavailable { get; } = new(DepartmentSourceStatus.Unavailable, []);

    public static DepartmentSourceResult Disabled { get; } = new(DepartmentSourceStatus.Disabled, []);
}

/// <summary>
/// Danh mục khoa/phòng của HIS, lấy qua API thông tin người bệnh (quyết định 42); câu truy vấn do phòng CNTT bệnh viện viết.
/// Khi đã cấu hình API, danh mục khoa/phòng của phần mềm chỉ cập nhật từ HIS, không thêm, sửa, xóa trong phần mềm.
/// </summary>
public interface IDepartmentSource
{
    /// <summary>Đã cấu hình API thông tin người bệnh: danh mục khoa/phòng lấy từ HIS.</summary>
    bool IsEnabled { get; }

    Task<DepartmentSourceResult> GetAsync(CancellationToken cancellationToken);
}
