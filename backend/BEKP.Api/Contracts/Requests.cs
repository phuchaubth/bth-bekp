using System.ComponentModel.DataAnnotations;
using BEKP.Domain.Beds;
using BEKP.Domain.Stays;
using BEKP.Domain.Transfusion;

namespace BEKP.Api.Contracts;

internal static class Patterns
{
    public const string Department = @"^DEP-\d{4}$";
    public const string Stay = @"^ADM-\d{6}$";
    public const string Bed = @"^[A-Z0-9]{1,6}(-[A-Z0-9]{1,6})?$";
    public const string Icd10 = @"^[A-TV-Z][0-9]{2}(\.[0-9A-Z]{1,2})?$";
}

public sealed class AdmitStayRequest
{
    [Required, RegularExpression(Patterns.Department)]
    public string DepartmentCode { get; init; } = string.Empty;

    [Required, RegularExpression(Patterns.Bed)]
    public string BedCode { get; init; } = string.Empty;

    [Required, RegularExpression(Patterns.Stay)]
    public string StayCode { get; init; } = string.Empty;

    [Required]
    public CareLevel? CareLevel { get; init; }

    [Required, RegularExpression(Patterns.Icd10)]
    public string PrincipalDiagnosisCode { get; init; } = string.Empty;

    public DateOnly? ExpectedDischargeDate { get; init; }

    public DateTimeOffset? AdmittedAt { get; init; }
}

public sealed class DischargeStayRequest
{
    public DateTimeOffset? DischargedAt { get; init; }
}

public sealed class UpdateClinicalStatusRequest
{
    [Required]
    public CareLevel? CareLevel { get; init; }

    [Required, RegularExpression(Patterns.Icd10)]
    public string PrincipalDiagnosisCode { get; init; } = string.Empty;

    public bool IsNeutropenic { get; init; }

    public bool IsTransfusing { get; init; }

    public bool HasChemotherapyToday { get; init; }

    public DateOnly? ExpectedDischargeDate { get; init; }
}

public sealed class UpdateBedStatusRequest
{
    [Required]
    public BedStatus? Status { get; init; }
}

public sealed class RecordFeverRequest
{
    [Range(typeof(decimal), "38.0", "43.0")]
    public decimal TemperatureCelsius { get; init; }

    public DateTimeOffset? DetectedAt { get; init; }
}

public sealed class RecordAntibioticStartRequest
{
    public DateTimeOffset? StartedAt { get; init; }
}

public sealed class CreateBloodRequestRequest
{
    [Required, RegularExpression(Patterns.Stay)]
    public string StayCode { get; init; } = string.Empty;

    [Required]
    public BloodProductType? Product { get; init; }

    [Required]
    public AboGroup? AboGroup { get; init; }

    public bool RhDPositive { get; init; } = true;

    [Range(1, BloodRequest.MaxUnits)]
    public int Units { get; init; }

    [Required]
    public BloodRequestUrgency? Urgency { get; init; }

    [MaxLength(4)]
    public IReadOnlyList<BloodProductModifiers> Modifiers { get; init; } = [];
}

public sealed class ChangeBloodRequestStatusRequest
{
    [Required]
    public BloodRequestStatus? Status { get; init; }
}

public sealed class SetBloodInventoryRequest
{
    [Range(0, BloodInventoryItem.MaxUnits)]
    public int UnitsAvailable { get; init; }

    [Range(0, BloodInventoryItem.MaxUnits)]
    public int MinimumUnits { get; init; }
}

public sealed record CreatedResponse(Guid Id);
