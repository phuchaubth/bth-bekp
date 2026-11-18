using BEKP.Domain.Common;

namespace BEKP.Domain.Beds;

public enum BedStatus
{
    Available,
    Occupied,
    Cleaning,
    OutOfService,
}

/// <summary>A physical bed. Extra beds (giường kê thêm) are not part of planned capacity.</summary>
public sealed class Bed : Entity
{
    public const int RoomLabelMaxLength = 40;

    private Bed()
    {
        Code = string.Empty;
        RoomLabel = string.Empty;
    }

    public Bed(Guid id, Guid departmentId, string code, string roomLabel, bool isExtra, int sortOrder, DateTime createdAtUtc)
        : base(id)
    {
        if (departmentId == Guid.Empty)
        {
            throw new DomainException("Department is required.");
        }

        if (!BusinessCodes.IsBedCode(code))
        {
            throw new DomainException("Bed code is invalid.");
        }

        DepartmentId = departmentId;
        Code = code;
        RoomLabel = Guard.Text(roomLabel, nameof(RoomLabel), RoomLabelMaxLength);
        IsExtra = isExtra;
        SortOrder = sortOrder;
        Status = BedStatus.Available;
        StatusChangedAtUtc = Guard.Utc(createdAtUtc, nameof(createdAtUtc));
    }

    public Guid DepartmentId { get; private set; }

    public string Code { get; private set; }

    public string RoomLabel { get; private set; }

    public bool IsExtra { get; private set; }

    public int SortOrder { get; private set; }

    public BedStatus Status { get; private set; }

    public DateTime StatusChangedAtUtc { get; private set; }

    public void Occupy(DateTime atUtc)
    {
        if (Status != BedStatus.Available)
        {
            throw new DomainException($"Bed {Code} is not available.");
        }

        Change(BedStatus.Occupied, atUtc);
    }

    /// <summary>Called on discharge or transfer: the bed must be cleaned before the next admission.</summary>
    public void Release(DateTime atUtc)
    {
        if (Status != BedStatus.Occupied)
        {
            throw new DomainException($"Bed {Code} is not occupied.");
        }

        Change(BedStatus.Cleaning, atUtc);
    }

    /// <summary>Housekeeping / maintenance status changes. Occupancy only changes through admission and discharge.</summary>
    public void SetHousekeepingStatus(BedStatus status, DateTime atUtc)
    {
        if (status == BedStatus.Occupied)
        {
            throw new DomainException("A bed becomes occupied only through an admission.");
        }

        if (Status == BedStatus.Occupied)
        {
            throw new DomainException($"Bed {Code} is occupied; discharge or transfer the patient first.");
        }

        Change(status, atUtc);
    }

    private void Change(BedStatus status, DateTime atUtc)
    {
        Status = status;
        StatusChangedAtUtc = Guard.Utc(atUtc, nameof(atUtc));
    }
}
