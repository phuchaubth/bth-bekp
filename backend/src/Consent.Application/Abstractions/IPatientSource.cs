using Consent.Domain.Patients;

namespace Consent.Application.Abstractions;

public enum PatientSourceStatus
{
    Found,
    NotFound,

    /// <summary>Không kết nối được nguồn (lỗi mạng, quá thời gian, sai cấu hình…): vẫn cho nhập tay.</summary>
    Unavailable,

    /// <summary>Chưa cấu hình nguồn thông tin người bệnh (<c>PatientSource:Provider</c> = <c>None</c>).</summary>
    Disabled,
}

/// <summary>Thông tin hành chính người bệnh lấy từ nguồn bên ngoài (API thông tin người bệnh). Trường nào thiếu thì rỗng.</summary>
public sealed record ExternalPatient(
    string? PatientCode,
    string? FullName,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? CitizenId,
    string? PhoneNumber,
    string? PermanentAddress,
    string? CurrentAddress,
    string? Ethnicity,
    string? Occupation,
    string? Nationality,
    string? HealthInsuranceNumber,
    DateOnly? CitizenIdIssueDate = null,
    string? OldIdNumber = null);

public sealed record PatientSourceResult(PatientSourceStatus Status, ExternalPatient? Patient)
{
    public static PatientSourceResult NotFound { get; } = new(PatientSourceStatus.NotFound, null);

    public static PatientSourceResult Unavailable { get; } = new(PatientSourceStatus.Unavailable, null);

    public static PatientSourceResult Disabled { get; } = new(PatientSourceStatus.Disabled, null);
}

/// <summary>
/// Nguồn thông tin hành chính người bệnh, chỉ đọc: tra theo mã người bệnh hoặc số CCCD (một trong hai). Hiện là dịch vụ
/// riêng "API thông tin người bệnh" (PatientInfo.Api) đọc CSDL bệnh viện; phần mềm này không giữ chuỗi kết nối CSDL đó.
/// </summary>
public interface IPatientSource
{
    bool IsEnabled { get; }

    Task<PatientSourceResult> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken);
}
