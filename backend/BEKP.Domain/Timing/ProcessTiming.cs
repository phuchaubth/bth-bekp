using BEKP.Domain.Common;

namespace BEKP.Domain.Timing;

/// <summary>Regulated process intervals whose duration is measured against a configured target.</summary>
public enum TimeStandardType
{
    /// <summary>Blood request to issue, emergency urgency.</summary>
    BloodIssueEmergency,

    /// <summary>Blood request to issue, urgent.</summary>
    BloodIssueUrgent,

    /// <summary>Blood request to issue, routine.</summary>
    BloodIssueRoutine,

    /// <summary>Blood issued to transfusion started at the bedside.</summary>
    TransfusionStartAfterIssue,

    /// <summary>Fever detected in a neutropenic patient to first antibiotic dose.</summary>
    FebrileNeutropeniaAntibiotic,

    /// <summary>Bed released for cleaning to available for the next admission.</summary>
    BedTurnaround,
}

/// <summary>One completed, measured interval of a regulated process.</summary>
public sealed class ProcessTiming : Entity
{
    private ProcessTiming()
    {
    }

    public ProcessTiming(Guid id, TimeStandardType standard, Guid departmentId, DateTime startedAtUtc, DateTime completedAtUtc, string? subject)
        : base(id)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Department is required.");
        }

        Guard.Utc(startedAtUtc, nameof(startedAtUtc));
        Guard.Utc(completedAtUtc, nameof(completedAtUtc));
        if (completedAtUtc < startedAtUtc)
        {
            throw new DomainException("Completion cannot be before start.");
        }

        Standard = standard;
        DepartmentId = departmentId;
        StartedAtUtc = startedAtUtc;
        CompletedAtUtc = completedAtUtc;
        DurationMinutes = (int)Math.Floor((completedAtUtc - startedAtUtc).TotalMinutes);
        Subject = subject is null ? null : Guard.Text(subject, nameof(Subject), 40);
    }

    public TimeStandardType Standard { get; private set; }

    public Guid DepartmentId { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime CompletedAtUtc { get; private set; }

    public int DurationMinutes { get; private set; }

    /// <summary>Code of the measured item (stay code or bed code), never personal data.</summary>
    public string? Subject { get; private set; }
}
