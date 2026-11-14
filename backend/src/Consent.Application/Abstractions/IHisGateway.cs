namespace Consent.Application.Abstractions;

public enum HisLookupStatus
{
    Found,
    NotFound,

    /// <summary>Không kết nối được HIS (lỗi mạng, quá thời gian, HIS bảo trì…).</summary>
    Unavailable,

    /// <summary>Chưa kết nối HIS (tắt trong cấu hình <c>His:Provider</c>): không tra cứu, không đối chiếu.</summary>
    Disabled,
}

/// <summary>Đợt khám/tiếp nhận hiện tại trên HIS.</summary>
public sealed record HisEncounter(
    string EncounterCode,
    DateTimeOffset? AdmittedAtUtc,
    string? DepartmentName,
    string? ClinicName);

/// <summary>Thông tin người bệnh lấy từ HIS.</summary>
public sealed record HisPatient(
    string PatientCode,
    string? FullName,
    DateOnly? DateOfBirth,
    // Đối tượng: "BHYT" hoặc "Dịch vụ".
    string CoverageType,
    string? HealthInsuranceNumber,
    DateOnly? HealthInsuranceValidTo,
    HisEncounter? CurrentEncounter);

public sealed record HisLookupResult(HisLookupStatus Status, HisPatient? Patient)
{
    public static HisLookupResult NotFound { get; } = new(HisLookupStatus.NotFound, null);

    public static HisLookupResult Unavailable { get; } = new(HisLookupStatus.Unavailable, null);

    public static HisLookupResult Disabled { get; } = new(HisLookupStatus.Disabled, null);
}

/// <summary>Cổng kết nối HIS (FPT eHospital), chỉ đọc. Hiện tạm chưa kết nối (mặc định tắt).</summary>
public interface IHisGateway
{
    /// <summary>
    /// <c>false</c> khi chưa kết nối HIS: hệ thống không tra cứu, không đối chiếu và giao diện ẩn mọi thông tin HIS.
    /// </summary>
    bool IsEnabled { get; }

    Task<HisLookupResult> FindPatientByCitizenIdAsync(string citizenId, CancellationToken cancellationToken);
}
