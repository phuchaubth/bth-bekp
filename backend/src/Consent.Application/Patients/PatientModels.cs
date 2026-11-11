using Consent.Application.Abstractions;
using Consent.Domain.Patients;

namespace Consent.Application.Patients;

public sealed record PatientQuery(string? Keyword = null, int Page = 1, int PageSize = 20);

/// <summary>Dòng dữ liệu tối thiểu cho danh sách (đã giải mã ở tầng Persistence, sẽ được che ở tầng Application).</summary>
public sealed record PatientSummary(
    Guid Id,
    string? PatientCode,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenId,
    DateTimeOffset? HisVerifiedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>Dòng danh sách người bệnh: số CCCD đã được che.</summary>
public sealed record PatientListItemDto(
    Guid Id,
    string? PatientCode,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenIdMasked,
    bool HisVerified,
    DateTimeOffset UpdatedAtUtc);

public sealed record RepresentativeDto(
    Guid Id,
    string FullName,
    RepresentativeRelationship Relationship,
    string? CitizenId,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? PhoneNumber,
    string? Address);

public sealed record PatientDetailDto(
    Guid Id,
    string? PatientCode,
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenId,
    string? OldIdNumber,
    DateOnly? CitizenIdIssueDate,
    string? PermanentAddress,
    string? CurrentAddress,
    string? PhoneNumber,
    string? Ethnicity,
    string? Occupation,
    string? Nationality,
    string? HealthInsuranceNumber,
    bool HisVerified,
    DateTimeOffset? HisVerifiedAtUtc,
    IReadOnlyList<RepresentativeDto> Representatives,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static PatientDetailDto From(Patient patient)
    {
        ArgumentNullException.ThrowIfNull(patient);
        return new PatientDetailDto(
            patient.Id,
            patient.PatientCode,
            patient.FullName,
            patient.DateOfBirth,
            patient.Gender,
            patient.CitizenId,
            patient.OldIdNumber,
            patient.CitizenIdIssueDate,
            patient.PermanentAddress,
            patient.CurrentAddress,
            patient.PhoneNumber,
            patient.Ethnicity,
            patient.Occupation,
            patient.Nationality,
            patient.HealthInsuranceNumber,
            patient.HisVerifiedAtUtc is not null,
            patient.HisVerifiedAtUtc,
            [.. patient.Representatives.OrderBy(r => r.CreatedAtUtc).Select(r => new RepresentativeDto(
                r.Id, r.FullName, r.Relationship, r.CitizenId, r.DateOfBirth, r.Gender, r.PhoneNumber, r.Address))],
            patient.CreatedAtUtc,
            patient.UpdatedAtUtc);
    }
}

/// <summary>Dữ liệu tạo/cập nhật người bệnh.</summary>
public sealed record PatientRequest(
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string? CitizenId = null,
    string? OldIdNumber = null,
    DateOnly? CitizenIdIssueDate = null,
    string? PermanentAddress = null,
    string? CurrentAddress = null,
    string? PhoneNumber = null,
    string? Ethnicity = null,
    string? Occupation = null,
    string? Nationality = null,
    string? HealthInsuranceNumber = null,
    string? PatientCode = null)
{
    public PatientDetails ToDetails() => new(
        FullName, DateOfBirth, Gender, CitizenId, OldIdNumber, CitizenIdIssueDate, PermanentAddress, CurrentAddress,
        PhoneNumber, Ethnicity, Occupation, Nationality, HealthInsuranceNumber, PatientCode);
}

public sealed record RepresentativeRequest(
    string FullName,
    RepresentativeRelationship Relationship,
    string? CitizenId = null,
    DateOnly? DateOfBirth = null,
    Gender? Gender = null,
    string? PhoneNumber = null,
    string? Address = null)
{
    public RepresentativeDetails ToDetails() => new(FullName, Relationship, CitizenId, DateOfBirth, Gender, PhoneNumber, Address);
}

public sealed record PatientLookupRequest(string CitizenId);

/// <summary>Kết quả tra cứu khi quét CCCD: hồ sơ đã có (nếu có) và thông tin từ HIS.</summary>
public sealed record PatientLookupResult(PatientDetailDto? ExistingPatient, HisLookupStatus HisStatus, HisPatient? HisPatient);
