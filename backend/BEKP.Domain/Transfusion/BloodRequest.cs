using BEKP.Domain.Common;

namespace BEKP.Domain.Transfusion;

/// <summary>A request for blood products sent by a ward to the transfusion department.</summary>
public sealed class BloodRequest : Entity
{
    public const int MaxUnits = 20;

    private static readonly Dictionary<BloodRequestStatus, BloodRequestStatus[]> AllowedTransitions = new()
    {
        [BloodRequestStatus.Requested] = [BloodRequestStatus.CrossMatching, BloodRequestStatus.Approved, BloodRequestStatus.Cancelled],
        [BloodRequestStatus.CrossMatching] = [BloodRequestStatus.Approved, BloodRequestStatus.Cancelled],
        [BloodRequestStatus.Approved] = [BloodRequestStatus.Issued, BloodRequestStatus.Cancelled],
        [BloodRequestStatus.Issued] = [BloodRequestStatus.Transfusing, BloodRequestStatus.Completed],
        [BloodRequestStatus.Transfusing] = [BloodRequestStatus.Completed],
        [BloodRequestStatus.Completed] = [],
        [BloodRequestStatus.Cancelled] = [],
    };

    private BloodRequest()
    {
    }

    public BloodRequest(
        Guid id,
        Guid stayId,
        Guid departmentId,
        BloodProductType product,
        AboGroup aboGroup,
        bool rhDPositive,
        int units,
        BloodRequestUrgency urgency,
        BloodProductModifiers modifiers,
        DateTime requestedAtUtc)
        : base(id)
    {
        if (stayId == Guid.Empty || departmentId == Guid.Empty)
        {
            throw new DomainException("Stay and department are required.");
        }

        if (units is < 1 or > MaxUnits)
        {
            throw new DomainException($"Units must be between 1 and {MaxUnits}.");
        }

        StayId = stayId;
        DepartmentId = departmentId;
        Product = product;
        AboGroup = aboGroup;
        RhDPositive = rhDPositive;
        Units = units;
        Urgency = urgency;
        Modifiers = modifiers;
        RequestedAtUtc = Guard.Utc(requestedAtUtc, nameof(requestedAtUtc));
        Status = BloodRequestStatus.Requested;
        StatusChangedAtUtc = requestedAtUtc;
    }

    public Guid StayId { get; private set; }

    public Guid DepartmentId { get; private set; }

    public BloodProductType Product { get; private set; }

    public AboGroup AboGroup { get; private set; }

    public bool RhDPositive { get; private set; }

    public int Units { get; private set; }

    public BloodRequestUrgency Urgency { get; private set; }

    public BloodProductModifiers Modifiers { get; private set; }

    public BloodRequestStatus Status { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public DateTime StatusChangedAtUtc { get; private set; }

    public DateTime? IssuedAtUtc { get; private set; }

    public DateTime? TransfusionStartedAtUtc { get; private set; }

    public bool IsPending => Status is BloodRequestStatus.Requested or BloodRequestStatus.CrossMatching or BloodRequestStatus.Approved or BloodRequestStatus.Issued;

    public static bool CanTransition(BloodRequestStatus from, BloodRequestStatus to) => AllowedTransitions[from].Contains(to);

    public void ChangeStatus(BloodRequestStatus next, DateTime atUtc)
    {
        if (!CanTransition(Status, next))
        {
            throw new DomainException($"Blood request cannot move from {Status} to {next}.");
        }

        Guard.Utc(atUtc, nameof(atUtc));
        if (atUtc < StatusChangedAtUtc)
        {
            throw new DomainException("Status change cannot be earlier than the previous change.");
        }

        Status = next;
        StatusChangedAtUtc = atUtc;
        if (next == BloodRequestStatus.Issued)
        {
            IssuedAtUtc = atUtc;
        }
        else if (next == BloodRequestStatus.Transfusing)
        {
            TransfusionStartedAtUtc = atUtc;
        }
    }
}
