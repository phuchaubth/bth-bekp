using Consent.Domain.Common;

namespace Consent.Domain.Patients;

/// <summary>Thông tin nhân thân dùng để tạo/cập nhật người bệnh.</summary>
public sealed record PatientDetails(
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
    string? PatientCode);

/// <summary>Người bệnh. Số CCCD, CMND cũ, số điện thoại, mã BHYT được mã hóa khi lưu (tầng Persistence).</summary>
public sealed class Patient : Entity, IAuditableEntity
{
    public const int FullNameMaxLength = 200;
    public const int AddressMaxLength = 500;
    public const int PatientCodeMaxLength = 50;
    public const int EthnicityMaxLength = 50;
    public const int OccupationMaxLength = 100;
    public const int NationalityMaxLength = 100;
    public const int PhoneNumberMaxLength = 20;
    public const int HealthInsuranceNumberMaxLength = 20;
    public const int OldIdNumberMaxLength = 12;

    private readonly List<LegalRepresentative> _representatives = [];

    private Patient(Guid id, string fullName, DateOnly dateOfBirth, Gender gender, DateTimeOffset createdAtUtc)
        : base(id)
    {
        FullName = fullName;
        DateOfBirth = dateOfBirth;
        Gender = gender;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Mã người bệnh trên HIS.</summary>
    public string? PatientCode { get; private set; }

    public string FullName { get; private set; }

    public DateOnly DateOfBirth { get; private set; }

    public Gender Gender { get; private set; }

    [SensitiveData]
    public string? CitizenId { get; private set; }

    [SensitiveData]
    public string? OldIdNumber { get; private set; }

    public DateOnly? CitizenIdIssueDate { get; private set; }

    public string? PermanentAddress { get; private set; }

    public string? CurrentAddress { get; private set; }

    [SensitiveData]
    public string? PhoneNumber { get; private set; }

    public string? Ethnicity { get; private set; }

    public string? Occupation { get; private set; }

    public string? Nationality { get; private set; }

    [SensitiveData]
    public string? HealthInsuranceNumber { get; private set; }

    /// <summary>Thời điểm đối chiếu thành công với HIS gần nhất; <c>null</c> = chưa đối chiếu HIS.</summary>
    public DateTimeOffset? HisVerifiedAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<LegalRepresentative> Representatives => _representatives;

    public static Patient Create(PatientDetails details, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(details);
        var patient = new Patient(Guid.CreateVersion7(), RequireName(details.FullName), details.DateOfBirth, details.Gender, nowUtc);
        patient.Apply(details, nowUtc);
        return patient;
    }

    public void Update(PatientDetails details, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(details);
        var patientCode = Clean(details.PatientCode);
        if (!string.Equals(patientCode, PatientCode, StringComparison.Ordinal))
        {
            // Đổi mã người bệnh thì kết quả đối chiếu HIS trước đó không còn đúng.
            HisVerifiedAtUtc = null;
        }

        Apply(details, nowUtc);
    }

    /// <summary>Đánh dấu đã đối chiếu thành công với HIS (mã người bệnh lấy từ HIS).</summary>
    public void MarkHisVerified(string patientCode, DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(patientCode);
        PatientCode = patientCode.Trim();
        HisVerifiedAtUtc = nowUtc;
    }

    public LegalRepresentative AddRepresentative(RepresentativeDetails details, DateTimeOffset nowUtc)
    {
        var representative = LegalRepresentative.Create(Id, details, nowUtc);
        _representatives.Add(representative);
        UpdatedAtUtc = nowUtc;
        return representative;
    }

    public LegalRepresentative UpdateRepresentative(Guid representativeId, RepresentativeDetails details, DateTimeOffset nowUtc)
    {
        var representative = FindRepresentative(representativeId);
        representative.Update(details);
        UpdatedAtUtc = nowUtc;
        return representative;
    }

    public void RemoveRepresentative(Guid representativeId, DateTimeOffset nowUtc)
    {
        _representatives.Remove(FindRepresentative(representativeId));
        UpdatedAtUtc = nowUtc;
    }

    private LegalRepresentative FindRepresentative(Guid representativeId) =>
        _representatives.Find(r => r.Id == representativeId)
        ?? throw new DomainException("Không tìm thấy người đại diện của người bệnh.");

    private void Apply(PatientDetails details, DateTimeOffset nowUtc)
    {
        var citizenId = Clean(details.CitizenId);
        if (citizenId is not null)
        {
            var error = CitizenIdNumber.CheckConsistency(citizenId, details.DateOfBirth, details.Gender);
            if (error is not null)
            {
                throw new DomainException(error);
            }
        }

        FullName = RequireName(details.FullName);
        DateOfBirth = details.DateOfBirth;
        Gender = details.Gender;
        CitizenId = citizenId;
        OldIdNumber = Clean(details.OldIdNumber);
        CitizenIdIssueDate = details.CitizenIdIssueDate;
        PermanentAddress = Clean(details.PermanentAddress);
        CurrentAddress = Clean(details.CurrentAddress);
        PhoneNumber = Clean(details.PhoneNumber);
        Ethnicity = Clean(details.Ethnicity);
        Occupation = Clean(details.Occupation);
        Nationality = Clean(details.Nationality);
        HealthInsuranceNumber = Clean(details.HealthInsuranceNumber)?.ToUpperInvariant();
        PatientCode = Clean(details.PatientCode);
        UpdatedAtUtc = nowUtc;
    }

    private static string RequireName(string fullName)
    {
        var value = (fullName ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > FullNameMaxLength)
        {
            throw new DomainException($"Họ tên người bệnh phải có từ 1 đến {FullNameMaxLength} ký tự.");
        }

        return value;
    }

    internal static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
