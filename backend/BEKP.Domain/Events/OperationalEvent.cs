using BEKP.Domain.Common;

namespace BEKP.Domain.Events;

public enum OperationalEventType
{
    Admission,
    Discharge,
    BedStatusChanged,
    ClinicalStatusChanged,
    FeverRecorded,
    AntibioticStarted,
    BloodRequested,
    BloodRequestStatusChanged,
    BloodInventoryUpdated,
}

/// <summary>A realtime activity entry shown in the live event feed. Holds codes only, never personal data.</summary>
public sealed class OperationalEvent : Entity
{
    public const int SubjectMaxLength = 80;

    private OperationalEvent()
    {
    }

    public OperationalEvent(Guid id, OperationalEventType type, DateTime occurredAtUtc, Guid? departmentId, string? departmentCode, string? stayCode, string? subject, string? value)
        : base(id)
    {
        if (departmentCode is not null && !BusinessCodes.IsDepartmentCode(departmentCode))
        {
            throw new DomainException("Department code is invalid.");
        }

        if (stayCode is not null && !BusinessCodes.IsStayCode(stayCode))
        {
            throw new DomainException("Stay code is invalid.");
        }

        Type = type;
        OccurredAtUtc = Guard.Utc(occurredAtUtc, nameof(occurredAtUtc));
        DepartmentId = departmentId;
        DepartmentCode = departmentCode;
        StayCode = stayCode;
        Subject = subject is null ? null : Guard.Text(subject, nameof(Subject), SubjectMaxLength);
        Value = value is null ? null : Guard.Text(value, nameof(Value), SubjectMaxLength);
    }

    public OperationalEventType Type { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public Guid? DepartmentId { get; private set; }

    public string? DepartmentCode { get; private set; }

    public string? StayCode { get; private set; }

    /// <summary>Short machine-oriented subject, e.g. a bed code or "2 RedCells O+".</summary>
    public string? Subject { get; private set; }

    /// <summary>Optional value, e.g. the new status or the recorded temperature.</summary>
    public string? Value { get; private set; }
}
