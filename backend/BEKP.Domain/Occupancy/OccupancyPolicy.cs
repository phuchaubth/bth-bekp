namespace BEKP.Domain.Occupancy;

public enum OccupancyLevel
{
    Normal,
    High,
    Critical,
    OverCapacity,
}

/// <summary>Bed occupancy thresholds: &lt; 85% normal, 85–94% high, ≥ 95% critical, &gt; 100% over capacity.</summary>
public static class OccupancyPolicy
{
    public const decimal HighThreshold = 85m;
    public const decimal CriticalThreshold = 95m;
    public const decimal FullThreshold = 100m;

    public static decimal Percent(int occupied, int capacity)
    {
        if (occupied < 0 || capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occupied), "Counts must not be negative.");
        }

        return capacity == 0 ? 0m : Math.Round(occupied * 100m / capacity, 1, MidpointRounding.AwayFromZero);
    }

    public static OccupancyLevel Classify(decimal percent) => percent switch
    {
        > FullThreshold => OccupancyLevel.OverCapacity,
        >= CriticalThreshold => OccupancyLevel.Critical,
        >= HighThreshold => OccupancyLevel.High,
        _ => OccupancyLevel.Normal,
    };
}
