using BEKP.Domain.Common;
using BEKP.Domain.Neutropenia;

namespace BEKP.Domain.Tests;

public sealed class FebrileNeutropeniaEpisodeTests
{
    private static readonly DateTime Fever = new(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc);

    private static FebrileNeutropeniaEpisode NewEpisode() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fever, 38.9m);

    [Theory]
    [InlineData(10, FebrileNeutropeniaStatus.OnTrack)]
    [InlineData(30, FebrileNeutropeniaStatus.Warning)]
    [InlineData(59, FebrileNeutropeniaStatus.Warning)]
    [InlineData(60, FebrileNeutropeniaStatus.Overdue)]
    public void Evaluate_WhileAwaitingAntibiotic(int minutes, FebrileNeutropeniaStatus expected) =>
        Assert.Equal(expected, NewEpisode().Evaluate(Fever.AddMinutes(minutes)));

    [Theory]
    [InlineData(35, FebrileNeutropeniaStatus.MetTarget)]
    [InlineData(60, FebrileNeutropeniaStatus.MetTarget)]
    [InlineData(61, FebrileNeutropeniaStatus.MissedTarget)]
    public void Evaluate_AfterAntibiotic_FreezesElapsedTime(int minutes, FebrileNeutropeniaStatus expected)
    {
        var episode = NewEpisode();
        episode.RecordAntibioticStart(Fever.AddMinutes(minutes));

        Assert.Equal(expected, episode.Evaluate(Fever.AddHours(5)));
        Assert.Equal(minutes, episode.ElapsedMinutes(Fever.AddHours(5)));
    }

    [Fact]
    public void RecordAntibioticStart_RejectsTimeBeforeFever() =>
        Assert.Throws<DomainException>(() => NewEpisode().RecordAntibioticStart(Fever.AddMinutes(-1)));

    [Fact]
    public void RecordAntibioticStart_CanOnlyBeRecordedOnce()
    {
        var episode = NewEpisode();
        episode.RecordAntibioticStart(Fever.AddMinutes(20));
        Assert.Throws<DomainException>(() => episode.RecordAntibioticStart(Fever.AddMinutes(30)));
    }

    [Theory]
    [InlineData(37.9)]
    [InlineData(43.1)]
    public void Constructor_RejectsNonFebrileOrImplausibleTemperature(double temperature) =>
        Assert.Throws<DomainException>(() => new FebrileNeutropeniaEpisode(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Fever, (decimal)temperature));

    [Fact]
    public void Constructor_RequiresUtcTimestamp() =>
        Assert.Throws<DomainException>(() => new FebrileNeutropeniaEpisode(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.SpecifyKind(Fever, DateTimeKind.Local), 38.5m));
}
