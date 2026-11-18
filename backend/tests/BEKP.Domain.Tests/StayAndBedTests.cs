using BEKP.Domain.Beds;
using BEKP.Domain.Common;
using BEKP.Domain.Diseases;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;

namespace BEKP.Domain.Tests;

public sealed class StayAndBedTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 2, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("ADM-123")]
    [InlineData("Nguyen Van A")]
    [InlineData("BN-000001")]
    public void Stay_RejectsAnythingButAnonymousCode(string code) =>
        Assert.Throws<DomainException>(() => new InpatientStay(Guid.NewGuid(), code, Guid.NewGuid(), Guid.NewGuid(), Now, CareLevel.Level3, "C92.0", null));

    [Fact]
    public void Stay_Discharge_IsFinal_AndClearsActiveTreatments()
    {
        var stay = new InpatientStay(Guid.NewGuid(), "ADM-000001", Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(-2), CareLevel.Level3, "D56.1", null);
        stay.UpdateClinicalStatus(CareLevel.Level2, "D56.1", false, true, false, null);
        stay.Discharge(Now);

        Assert.False(stay.IsActive);
        Assert.False(stay.IsTransfusing);
        Assert.Throws<DomainException>(() => stay.Discharge(Now));
        Assert.Throws<DomainException>(() => stay.UpdateClinicalStatus(CareLevel.Level3, "D56.1", false, false, false, null));
    }

    [Fact]
    public void Stay_DayOfStay_StartsAtOne()
    {
        var stay = new InpatientStay(Guid.NewGuid(), "ADM-000002", Guid.NewGuid(), Guid.NewGuid(), Now, CareLevel.Level3, "C91.0", null);
        Assert.Equal(1, stay.DayOfStay(Now.AddHours(3)));
        Assert.Equal(3, stay.DayOfStay(Now.AddDays(2).AddHours(1)));
    }

    [Fact]
    public void Bed_Lifecycle_OccupiedOnlyThroughAdmission()
    {
        var bed = new Bed(Guid.NewGuid(), Guid.NewGuid(), "201-1", "Phòng 201", false, 0, Now);
        Assert.Throws<DomainException>(() => bed.SetHousekeepingStatus(BedStatus.Occupied, Now));

        bed.Occupy(Now);
        Assert.Throws<DomainException>(() => bed.Occupy(Now));
        Assert.Throws<DomainException>(() => bed.SetHousekeepingStatus(BedStatus.Available, Now));

        bed.Release(Now.AddDays(1));
        Assert.Equal(BedStatus.Cleaning, bed.Status);
        bed.SetHousekeepingStatus(BedStatus.Available, Now.AddDays(1).AddMinutes(40));
        Assert.Equal(BedStatus.Available, bed.Status);
    }

    [Theory]
    [InlineData("C92.0", DiseaseGroup.AcuteMyeloidLeukemia)]
    [InlineData("C91.0", DiseaseGroup.AcuteLymphoblasticLeukemia)]
    [InlineData("C91.1", DiseaseGroup.ChronicLeukemia)]
    [InlineData("C83.3", DiseaseGroup.NonHodgkinLymphoma)]
    [InlineData("C81.1", DiseaseGroup.HodgkinLymphoma)]
    [InlineData("C90.0", DiseaseGroup.MultipleMyeloma)]
    [InlineData("D56.1", DiseaseGroup.Thalassemia)]
    [InlineData("D61.9", DiseaseGroup.AplasticAnemia)]
    [InlineData("D66", DiseaseGroup.Hemophilia)]
    [InlineData("D69.3", DiseaseGroup.ImmuneThrombocytopenia)]
    [InlineData("D69.6", DiseaseGroup.Other)]
    [InlineData("J18.9", DiseaseGroup.Other)]
    public void DiseaseClassifier_MapsIcd10ToHematologyGroup(string code, DiseaseGroup expected)
    {
        Assert.True(DiseaseClassifier.IsValidIcd10(code));
        Assert.Equal(expected, DiseaseClassifier.Classify(code));
    }

    [Theory]
    [InlineData("")]
    [InlineData("c92.0")]
    [InlineData("C9")]
    [InlineData("U07.1")]
    [InlineData("Bạch cầu cấp")]
    public void DiseaseClassifier_RejectsInvalidCodes(string code) => Assert.False(DiseaseClassifier.IsValidIcd10(code));

    [Fact]
    public void ProcessTiming_ComputesWholeMinutes()
    {
        var timing = new ProcessTiming(Guid.NewGuid(), TimeStandardType.BedTurnaround, Guid.NewGuid(), Now, Now.AddMinutes(42).AddSeconds(50), "201-1");
        Assert.Equal(42, timing.DurationMinutes);
        Assert.Throws<DomainException>(() => new ProcessTiming(Guid.NewGuid(), TimeStandardType.BedTurnaround, Guid.NewGuid(), Now, Now.AddMinutes(-1), null));
    }
}
