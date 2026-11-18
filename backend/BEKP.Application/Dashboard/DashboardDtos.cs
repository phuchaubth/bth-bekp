using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Diseases;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Occupancy;
using BEKP.Domain.Stays;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Dashboard;

public sealed record OperationalEventDto(
    Guid Id,
    OperationalEventType Type,
    DateTime OccurredAtUtc,
    string? DepartmentCode,
    string? DepartmentName,
    string? StayCode,
    string? Subject,
    string? Value);

public sealed record HospitalTotalsDto(
    int Capacity,
    int Occupied,
    decimal OccupancyPercent,
    OccupancyLevel OccupancyLevel,
    int Available,
    int Cleaning,
    int SterileRoomsAvailable,
    int Severe,
    int Neutropenic,
    int Transfusing,
    int ChemotherapyToday,
    int ExpectedDischargesToday,
    int PendingBloodRequests,
    int UnitsIssuedToday,
    int AdmissionsToday,
    int DischargesToday,
    int FebrileNeutropeniaActive,
    int FebrileNeutropeniaAwaitingAntibiotic);

public enum BedCellState
{
    Available,
    Cleaning,
    OutOfService,
    Occupied,
    Severe,
    Neutropenic,
    ExpectedDischarge,
}

public sealed record BedCellDto(string Code, BedCellState State, bool IsExtra);

public sealed record DepartmentSummaryDto(
    string Code,
    string Name,
    string Description,
    DepartmentGroup Group,
    bool IsSterileUnit,
    int Capacity,
    int Occupied,
    int Available,
    int Cleaning,
    int ExtraBedsInUse,
    decimal OccupancyPercent,
    OccupancyLevel OccupancyLevel,
    int Severe,
    int Neutropenic,
    int Transfusing,
    int ChemotherapyToday,
    int ExpectedDischargesToday,
    int PendingBloodRequests,
    int UnitsIssuedToday,
    int AdmissionsToday,
    int DischargesToday,
    IReadOnlyList<BedCellDto> Beds);

public sealed record FebrileNeutropeniaDto(
    Guid Id,
    string StayCode,
    string DepartmentCode,
    string DepartmentName,
    string? BedCode,
    DateTime FeverDetectedAtUtc,
    decimal TemperatureCelsius,
    DateTime? AntibioticStartedAtUtc,
    int ElapsedMinutes,
    FebrileNeutropeniaStatus Status);

public sealed record BloodRequestDto(
    Guid Id,
    string? StayCode,
    string DepartmentCode,
    string? BedCode,
    BloodProductType Product,
    AboGroup AboGroup,
    bool RhDPositive,
    int Units,
    BloodRequestUrgency Urgency,
    IReadOnlyList<BloodProductModifiers> Modifiers,
    BloodRequestStatus Status,
    DateTime RequestedAtUtc,
    DateTime StatusChangedAtUtc);

public sealed record BloodInventoryDto(
    BloodProductType Product,
    AboGroup AboGroup,
    int UnitsAvailable,
    int MinimumUnits,
    StockLevel Level,
    DateTime UpdatedAtUtc);

public enum AlertType
{
    DepartmentOverCapacity,
    DepartmentFull,
    SterileRoomsFull,
    FebrileNeutropeniaAntibioticDelay,
    BloodStockCritical,
    BloodStockLow,
    HighNeutropeniaLoad,
    PendingBloodRequests,
}

public enum AlertSeverity
{
    Critical,
    Warning,
}

/// <summary>Structured alert; the client formats the message (Vietnamese UI) from type and values.</summary>
public sealed record AlertDto(
    AlertType Type,
    AlertSeverity Severity,
    string? DepartmentCode,
    string? DepartmentName,
    string? Subject,
    decimal? Value,
    decimal? Reference,
    DateTime? SinceUtc);

public sealed record DiseasePatternEntryDto(DiseaseGroup Group, int ActiveInpatients, int AdmissionsLast30Days);

public sealed record DiseasePatternDto(int ActiveTotal, int AdmissionsTotal, IReadOnlyList<DiseasePatternEntryDto> Groups);

public sealed record HospitalOverviewDto(
    DateTime GeneratedAtUtc,
    HospitalTotalsDto Totals,
    IReadOnlyList<DepartmentSummaryDto> Departments,
    IReadOnlyList<AlertDto> Alerts,
    IReadOnlyList<BloodInventoryDto> BloodInventory,
    IReadOnlyList<FebrileNeutropeniaDto> FebrileNeutropenia,
    DiseasePatternDto DiseasePattern,
    IReadOnlyList<OperationalEventDto> RecentEvents);

public sealed record BedOccupantDto(
    string StayCode,
    int DayOfStay,
    CareLevel CareLevel,
    string PrincipalDiagnosisCode,
    DiseaseGroup DiseaseGroup,
    bool IsSevere,
    bool IsNeutropenic,
    bool IsTransfusing,
    bool HasChemotherapyToday,
    bool ExpectedDischargeToday,
    DateOnly? ExpectedDischargeDate);

public sealed record BedTileDto(string Code, BedStatus Status, bool IsExtra, BedOccupantDto? Occupant);

public sealed record RoomDto(string Label, IReadOnlyList<BedTileDto> Beds);

public sealed record DepartmentDetailDto(
    DateTime GeneratedAtUtc,
    DepartmentSummaryDto Summary,
    IReadOnlyList<RoomDto> Rooms,
    IReadOnlyList<FebrileNeutropeniaDto> FebrileNeutropenia,
    IReadOnlyList<BloodRequestDto> BloodRequests,
    IReadOnlyList<AlertDto> Alerts,
    DiseasePatternDto DiseasePattern,
    IReadOnlyList<OperationalEventDto> RecentEvents);

public sealed record DepartmentListItemDto(string Code, string Name, DepartmentGroup Group);
