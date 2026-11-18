using Consent.Domain.Common;

namespace Consent.Domain.Patients;

public sealed record RepresentativeDetails(
    string FullName,
    RepresentativeRelationship Relationship,
    string? CitizenId,
    DateOnly? DateOfBirth,
    Gender? Gender,
    string? PhoneNumber,
    string? Address);

/// <summary>Người đại diện hợp pháp/thân nhân có thể ký thay người bệnh.</summary>
public sealed class LegalRepresentative : Entity, IAuditableEntity
{
    private LegalRepresentative(Guid id, Guid patientId, string fullName, RepresentativeRelationship relationship, DateTimeOffset createdAtUtc)
        : base(id)
    {
        PatientId = patientId;
        FullName = fullName;
        Relationship = relationship;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid PatientId { get; private set; }

    public string FullName { get; private set; }

    public RepresentativeRelationship Relationship { get; private set; }

    [SensitiveData]
    public string? CitizenId { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public Gender? Gender { get; private set; }

    [SensitiveData]
    public string? PhoneNumber { get; private set; }

    public string? Address { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static LegalRepresentative Create(Guid patientId, RepresentativeDetails details, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(details);
        var representative = new LegalRepresentative(Guid.CreateVersion7(), patientId, details.FullName ?? string.Empty, details.Relationship, nowUtc);
        representative.Update(details);
        return representative;
    }

    internal void Update(RepresentativeDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        var fullName = (details.FullName ?? string.Empty).Trim();
        if (fullName.Length == 0 || fullName.Length > Patient.FullNameMaxLength)
        {
            throw new DomainException("Họ tên người đại diện không hợp lệ.");
        }

        var citizenId = Patient.Clean(details.CitizenId);
        if (citizenId is not null && !CitizenIdNumber.HasValidFormat(citizenId))
        {
            throw new DomainException("Số CCCD của người đại diện không hợp lệ.");
        }

        if (citizenId is not null && details.DateOfBirth is { } dob && details.Gender is { } gender
            && CitizenIdNumber.CheckConsistency(citizenId, dob, gender) is { } error)
        {
            throw new DomainException(error);
        }

        FullName = fullName;
        Relationship = details.Relationship;
        CitizenId = citizenId;
        DateOfBirth = details.DateOfBirth;
        Gender = details.Gender;
        PhoneNumber = Patient.Clean(details.PhoneNumber);
        Address = Patient.Clean(details.Address);
    }
}
