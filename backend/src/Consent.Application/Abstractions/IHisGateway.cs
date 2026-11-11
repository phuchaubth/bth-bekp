namespace Consent.Application.Abstractions;

public enum HisLookupStatus
{
    Found,
    NotFound,

    /// <summary>Không kết nối được HIS (lỗi mạng, quá thời gian, HIS bảo trì…).</summary>
    Unavailable,
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
}

/// <summary>Cổng kết nối HIS (FPT eHospital). Giai đoạn 1 chỉ đọc.</summary>
public interface IHisGateway
{
    Task<HisLookupResult> FindPatientByCitizenIdAsync(string citizenId, CancellationToken cancellationToken);
}
