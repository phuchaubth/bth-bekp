using BEKP.Domain.Common;
using BEKP.Domain.Diseases;

namespace BEKP.Domain.Stays;

/// <summary>Nursing care level (cấp chăm sóc). Level 1 is the most intensive.</summary>
public enum CareLevel
{
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
}

/// <summary>
/// An inpatient episode identified only by an anonymous stay code (ADM-000000).
/// No names, dates of birth or medical record numbers are stored by design.
/// </summary>
public sealed class InpatientStay : Entity
{
    private InpatientStay()
    {
        Code = string.Empty;
        PrincipalDiagnosisCode = string.Empty;
    }

    public InpatientStay(Guid id, string code, Guid departmentId, Guid bedId, DateTime admittedAtUtc, CareLevel careLevel, string principalDiagnosisCode, DateOnly? expectedDischargeDate)
        : base(id)
    {
        if (!BusinessCodes.IsStayCode(code))
        {
            throw new DomainException("Stay code must match ADM-000000.");
        }

        if (departmentId == Guid.Empty || bedId == Guid.Empty)
        {
            throw new DomainException("Department and bed are required.");
        }

        Code = code;
        PrincipalDiagnosisCode = string.Empty;
        SetDiagnosis(principalDiagnosisCode);
        DepartmentId = departmentId;
        BedId = bedId;
        AdmittedAtUtc = Guard.Utc(admittedAtUtc, nameof(admittedAtUtc));
        CareLevel = careLevel;
        ExpectedDischargeDate = expectedDischargeDate;
    }

    public string Code { get; private set; }

    public Guid DepartmentId { get; private set; }

    public Guid BedId { get; private set; }

    public DateTime AdmittedAtUtc { get; private set; }

    public DateTime? DischargedAtUtc { get; private set; }

    public CareLevel CareLevel { get; private set; }

    /// <summary>ICD-10 code of the principal diagnosis (code only, no free text).</summary>
    public string PrincipalDiagnosisCode { get; private set; }

    public DiseaseGroup DiseaseGroup { get; private set; }

    /// <summary>Severe neutropenia (ANC &lt; 0.5 G/L) requiring protective isolation.</summary>
    public bool IsNeutropenic { get; private set; }

    public bool IsTransfusing { get; private set; }

    public bool HasChemotherapyToday { get; private set; }

    public DateOnly? ExpectedDischargeDate { get; private set; }

    public bool IsActive => DischargedAtUtc is null;

    public bool IsSevere => CareLevel == CareLevel.Level1;

    public int DayOfStay(DateTime nowUtc) => Math.Max(1, (int)Math.Floor((nowUtc - AdmittedAtUtc).TotalDays) + 1);

    public void UpdateClinicalStatus(CareLevel careLevel, string principalDiagnosisCode, bool isNeutropenic, bool isTransfusing, bool hasChemotherapyToday, DateOnly? expectedDischargeDate)
    {
        EnsureActive();
        SetDiagnosis(principalDiagnosisCode);
        CareLevel = careLevel;
        IsNeutropenic = isNeutropenic;
        IsTransfusing = isTransfusing;
        HasChemotherapyToday = hasChemotherapyToday;
        ExpectedDischargeDate = expectedDischargeDate;
    }

    public void Discharge(DateTime atUtc)
    {
        EnsureActive();
        Guard.Utc(atUtc, nameof(atUtc));
        if (atUtc < AdmittedAtUtc)
        {
            throw new DomainException("Discharge cannot be before admission.");
        }

        DischargedAtUtc = atUtc;
        IsTransfusing = false;
        HasChemotherapyToday = false;
    }

    private void SetDiagnosis(string principalDiagnosisCode)
    {
        if (!DiseaseClassifier.IsValidIcd10(principalDiagnosisCode))
        {
            throw new DomainException("Principal diagnosis must be an ICD-10 code, e.g. C92.0.");
        }

        PrincipalDiagnosisCode = principalDiagnosisCode;
        DiseaseGroup = DiseaseClassifier.Classify(principalDiagnosisCode);
    }

    private void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException($"Stay {Code} is already discharged.");
        }
    }
}
