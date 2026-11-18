using BEKP.Domain.Common;

namespace BEKP.Domain.Transfusion;

/// <summary>Available units of one blood product for one ABO group, with its minimum stock level.</summary>
public sealed class BloodInventoryItem : Entity
{
    public const int MaxUnits = 100_000;

    private BloodInventoryItem()
    {
    }

    public BloodInventoryItem(Guid id, BloodProductType product, AboGroup aboGroup, int unitsAvailable, int minimumUnits, DateTime updatedAtUtc)
        : base(id)
    {
        Product = product;
        AboGroup = aboGroup;
        Update(unitsAvailable, minimumUnits, updatedAtUtc);
    }

    public BloodProductType Product { get; private set; }

    public AboGroup AboGroup { get; private set; }

    public int UnitsAvailable { get; private set; }

    public int MinimumUnits { get; private set; }

    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Critical below half of the minimum, low below the minimum.</summary>
    public StockLevel Level
    {
        get
        {
            if (UnitsAvailable * 2 < MinimumUnits)
            {
                return StockLevel.Critical;
            }

            return UnitsAvailable < MinimumUnits ? StockLevel.Low : StockLevel.Adequate;
        }
    }

    public void Update(int unitsAvailable, int minimumUnits, DateTime updatedAtUtc)
    {
        if (unitsAvailable is < 0 or > MaxUnits || minimumUnits is < 0 or > MaxUnits)
        {
            throw new DomainException($"Units must be between 0 and {MaxUnits}.");
        }

        UnitsAvailable = unitsAvailable;
        MinimumUnits = minimumUnits;
        UpdatedAtUtc = Guard.Utc(updatedAtUtc, nameof(updatedAtUtc));
    }

    /// <summary>Units leave stock when issued. Stock never goes below zero (the blood bank system stays authoritative).</summary>
    public void Withdraw(int units, DateTime atUtc)
    {
        if (units < 1)
        {
            throw new DomainException("Units to withdraw must be positive.");
        }

        UnitsAvailable = Math.Max(0, UnitsAvailable - units);
        UpdatedAtUtc = Guard.Utc(atUtc, nameof(atUtc));
    }
}
