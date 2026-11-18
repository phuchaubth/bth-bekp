using BEKP.Domain.Common;

namespace BEKP.Domain.Neutropenia;

public enum FebrileNeutropeniaStatus
{
    /// <summary>Waiting for antibiotics, less than 30 minutes since fever.</summary>
    OnTrack,

    /// <summary>Waiting for antibiotics, 30–59 minutes since fever.</summary>
    Warning,

    /// <summary>Waiting for antibiotics, 60 minutes or more (target missed).</summary>
    Overdue,

    /// <summary>Antibiotics started within 60 minutes.</summary>
    MetTarget,

    /// <summary>Antibiotics started after 60 minutes.</summary>
    MissedTarget,
}

/// <summary>Febrile neutropenia: time-to-antibiotic is tracked against a 60-minute target.</summary>
public sealed class FebrileNeutropeniaEpisode : Entity
{
    public const int TargetMinutes = 60;
    public const int WarningMinutes = 30;
    public const decimal MinimumFeverCelsius = 38.0m;
    public const decimal MaximumPlausibleCelsius = 43.0m;

    private FebrileNeutropeniaEpisode()
    {
    }

    public FebrileNeutropeniaEpisode(Guid id, Guid stayId, Guid departmentId, DateTime feverDetectedAtUtc, decimal temperatureCelsius)
        : base(id)
    {
        if (stayId == Guid.Empty || departmentId == Guid.Empty)
        {
            throw new DomainException("Stay and department are required.");
        }

        if (temperatureCelsius < MinimumFeverCelsius || temperatureCelsius > MaximumPlausibleCelsius)
        {
            throw new DomainException($"Fever temperature must be between {MinimumFeverCelsius} and {MaximumPlausibleCelsius} °C.");
        }

        StayId = stayId;
        DepartmentId = departmentId;
        FeverDetectedAtUtc = Guard.Utc(feverDetectedAtUtc, nameof(feverDetectedAtUtc));
        TemperatureCelsius = temperatureCelsius;
    }

    public Guid StayId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public DateTime FeverDetectedAtUtc { get; private set; }

    public decimal TemperatureCelsius { get; private set; }

    public DateTime? AntibioticStartedAtUtc { get; private set; }

    public bool AwaitingAntibiotic => AntibioticStartedAtUtc is null;

    public void RecordAntibioticStart(DateTime atUtc)
    {
        if (!AwaitingAntibiotic)
        {
            throw new DomainException("Antibiotic start has already been recorded.");
        }

        Guard.Utc(atUtc, nameof(atUtc));
        if (atUtc < FeverDetectedAtUtc)
        {
            throw new DomainException("Antibiotic start cannot be before fever detection.");
        }

        AntibioticStartedAtUtc = atUtc;
    }

    public int ElapsedMinutes(DateTime nowUtc)
    {
        var end = AntibioticStartedAtUtc ?? nowUtc;
        return Math.Max(0, (int)Math.Floor((end - FeverDetectedAtUtc).TotalMinutes));
    }

    public FebrileNeutropeniaStatus Evaluate(DateTime nowUtc)
    {
        var minutes = ElapsedMinutes(nowUtc);
        if (!AwaitingAntibiotic)
        {
            return minutes <= TargetMinutes ? FebrileNeutropeniaStatus.MetTarget : FebrileNeutropeniaStatus.MissedTarget;
        }

        return minutes switch
        {
            >= TargetMinutes => FebrileNeutropeniaStatus.Overdue,
            >= WarningMinutes => FebrileNeutropeniaStatus.Warning,
            _ => FebrileNeutropeniaStatus.OnTrack,
        };
    }
}
