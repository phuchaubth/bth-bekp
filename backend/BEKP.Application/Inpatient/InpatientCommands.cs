using BEKP.Domain.Beds;
using BEKP.Domain.Stays;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Inpatient;

public sealed record AdmitStayCommand(
    string DepartmentCode,
    string BedCode,
    string StayCode,
    CareLevel CareLevel,
    string PrincipalDiagnosisCode,
    DateOnly? ExpectedDischargeDate,
    DateTimeOffset? AdmittedAt);

public sealed record DischargeStayCommand(string StayCode, DateTimeOffset? DischargedAt);

public sealed record UpdateClinicalStatusCommand(
    string StayCode,
    CareLevel CareLevel,
    string PrincipalDiagnosisCode,
    bool IsNeutropenic,
    bool IsTransfusing,
    bool HasChemotherapyToday,
    DateOnly? ExpectedDischargeDate);

public sealed record UpdateBedStatusCommand(string DepartmentCode, string BedCode, BedStatus Status);

public sealed record RecordFeverCommand(string StayCode, decimal TemperatureCelsius, DateTimeOffset? DetectedAt);

public sealed record RecordAntibioticStartCommand(Guid EpisodeId, DateTimeOffset? StartedAt);

public sealed record CreateBloodRequestCommand(
    string StayCode,
    BloodProductType Product,
    AboGroup AboGroup,
    bool RhDPositive,
    int Units,
    BloodRequestUrgency Urgency,
    BloodProductModifiers Modifiers);

public sealed record ChangeBloodRequestStatusCommand(Guid RequestId, BloodRequestStatus Status);

public sealed record SetBloodInventoryCommand(BloodProductType Product, AboGroup AboGroup, int UnitsAvailable, int MinimumUnits);
