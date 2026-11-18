using BEKP.Domain.Occupancy;

namespace BEKP.Domain.Tests;

public sealed class OccupancyPolicyTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(45, 48, 93.8)]
    [InlineData(52, 50, 104.0)]
    [InlineData(1, 3, 33.3)]
    public void Percent_RoundsToOneDecimal(int occupied, int capacity, double expected) =>
        Assert.Equal((decimal)expected, OccupancyPolicy.Percent(occupied, capacity));

    [Theory]
    [InlineData(84.9, OccupancyLevel.Normal)]
    [InlineData(85.0, OccupancyLevel.High)]
    [InlineData(94.9, OccupancyLevel.High)]
    [InlineData(95.0, OccupancyLevel.Critical)]
    [InlineData(100.0, OccupancyLevel.Critical)]
    [InlineData(100.1, OccupancyLevel.OverCapacity)]
    public void Classify_UsesConfiguredThresholds(double percent, OccupancyLevel expected) =>
        Assert.Equal(expected, OccupancyPolicy.Classify((decimal)percent));

    [Fact]
    public void Percent_RejectsNegativeCounts() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => OccupancyPolicy.Percent(-1, 10));
}
