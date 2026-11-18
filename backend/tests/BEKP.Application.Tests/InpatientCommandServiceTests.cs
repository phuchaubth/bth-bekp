using BEKP.Application.Common;
using BEKP.Application.Inpatient;
using BEKP.Domain.Beds;
using BEKP.Domain.Common;
using BEKP.Domain.Events;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Tests;

public sealed class InpatientCommandServiceTests
{
    [Fact]
    public async Task Admit_OccupiesBed_PersistsAuditsAndBroadcasts()
    {
        var host = new TestHost();
        host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 2);

        await host.Commands.AdmitAsync(new AdmitStayCommand("DEP-0001", "100-1", "ADM-123456", CareLevel.Level3, "C92.0", null, null), CancellationToken.None);

        Assert.Equal(BedStatus.Occupied, host.Repository.Beds.Single(b => b.Code == "100-1").Status);
        Assert.Equal(1, host.Repository.SaveCount);
        var audit = Assert.Single(host.Audit.Entries);
        Assert.Equal("his-integration", audit.Actor);
        Assert.Equal("inpatient.admit", audit.Action);
        var published = Assert.Single(host.Notifier.Published);
        Assert.Equal(OperationalEventType.Admission, published.Type);
        Assert.Equal("DEP-0001", published.DepartmentCode);
        Assert.Equal("Bạch cầu cấp người lớn", published.DepartmentName);
    }

    [Fact]
    public async Task Admit_ValidatesAllFields()
    {
        var host = new TestHost();
        var ex = await Assert.ThrowsAsync<RequestValidationException>(() =>
            host.Commands.AdmitAsync(new AdmitStayCommand("KHOA-1", "bed 1", "Nguyen Van A", (CareLevel)9, "leukemia", null, null), CancellationToken.None));

        Assert.Equal(
            ["BedCode", "CareLevel", "DepartmentCode", "PrincipalDiagnosisCode", "StayCode"],
            ex.Errors.Keys.Order());
        Assert.Empty(host.Notifier.Published);
    }

    [Fact]
    public async Task Admit_IntoOccupiedBed_IsRejected()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        host.Ward.Admit(department);

        await Assert.ThrowsAsync<DomainException>(() =>
            host.Commands.AdmitAsync(new AdmitStayCommand("DEP-0001", "100-1", "ADM-999999", CareLevel.Level3, "C92.0", null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Admit_RejectsFutureTimestamp()
    {
        var host = new TestHost();
        host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            host.Commands.AdmitAsync(new AdmitStayCommand("DEP-0001", "100-1", "ADM-000777", CareLevel.Level3, "C92.0", null, new DateTimeOffset(TestHost.Now.AddHours(1))), CancellationToken.None));
    }

    [Fact]
    public async Task Discharge_ReleasesBedForCleaning_ThenCleaningRecordsTurnaroundTime()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        var stay = host.Ward.Admit(department);

        await host.Commands.DischargeAsync(new DischargeStayCommand(stay.Code, null), CancellationToken.None);
        Assert.False(stay.IsActive);
        Assert.Equal(BedStatus.Cleaning, host.Repository.Beds.Single(b => !b.IsExtra).Status);

        host.Clock.Advance(TimeSpan.FromMinutes(42));
        await host.Commands.UpdateBedStatusAsync(new UpdateBedStatusCommand("DEP-0001", "100-1", BedStatus.Available), CancellationToken.None);

        var timing = Assert.Single(host.Repository.Timings);
        Assert.Equal(TimeStandardType.BedTurnaround, timing.Standard);
        Assert.Equal(42, timing.DurationMinutes);
    }

    [Fact]
    public async Task UpdateBedStatus_CannotSetOccupied() =>
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            new TestHost().Commands.UpdateBedStatusAsync(new UpdateBedStatusCommand("DEP-0001", "100-1", BedStatus.Occupied), CancellationToken.None));

    [Fact]
    public async Task Fever_RequiresNeutropenicStay_AndAntibioticRecordsTimeToTreatment()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 2);
        var regular = host.Ward.Admit(department);
        var neutropenic = host.Ward.Admit(department, neutropenic: true, care: CareLevel.Level2);

        await Assert.ThrowsAsync<DomainException>(() => host.Commands.RecordFeverAsync(new RecordFeverCommand(regular.Code, 38.5m, null), CancellationToken.None));

        var episodeId = await host.Commands.RecordFeverAsync(new RecordFeverCommand(neutropenic.Code, 38.9m, null), CancellationToken.None);
        await Assert.ThrowsAsync<DomainException>(() => host.Commands.RecordFeverAsync(new RecordFeverCommand(neutropenic.Code, 39.0m, null), CancellationToken.None));

        host.Clock.Advance(TimeSpan.FromMinutes(35));
        await host.Commands.RecordAntibioticStartAsync(new RecordAntibioticStartCommand(episodeId, null), CancellationToken.None);

        var timing = Assert.Single(host.Repository.Timings);
        Assert.Equal(TimeStandardType.FebrileNeutropeniaAntibiotic, timing.Standard);
        Assert.Equal(35, timing.DurationMinutes);
        Assert.Equal(neutropenic.Code, timing.Subject);
    }

    [Theory]
    [InlineData(37.5)]
    [InlineData(45.0)]
    public async Task Fever_RejectsOutOfRangeTemperature(double temperature) =>
        await Assert.ThrowsAsync<RequestValidationException>(() =>
            new TestHost().Commands.RecordFeverAsync(new RecordFeverCommand("ADM-000001", (decimal)temperature, null), CancellationToken.None));

    [Fact]
    public async Task BloodRequest_IssueWithdrawsStock_AndMeasuresIssueAndBedsideTimes()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        var stay = host.Ward.Admit(department, care: CareLevel.Level1);
        host.Repository.Inventory.Add(new BloodInventoryItem(Guid.NewGuid(), BloodProductType.Platelets, AboGroup.O, 10, 20, TestHost.Now));

        var id = await host.Commands.CreateBloodRequestAsync(
            new CreateBloodRequestCommand(stay.Code, BloodProductType.Platelets, AboGroup.O, true, 1, BloodRequestUrgency.Emergency, BloodProductModifiers.Irradiated | BloodProductModifiers.Leukoreduced),
            CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromMinutes(10));
        await host.Commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(id, BloodRequestStatus.Approved), CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromMinutes(15));
        await host.Commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(id, BloodRequestStatus.Issued), CancellationToken.None);
        host.Clock.Advance(TimeSpan.FromMinutes(20));
        await host.Commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(id, BloodRequestStatus.Transfusing), CancellationToken.None);

        Assert.Equal(9, host.Repository.Inventory.Single().UnitsAvailable);
        Assert.Equal(25, host.Repository.Timings.Single(t => t.Standard == TimeStandardType.BloodIssueEmergency).DurationMinutes);
        Assert.Equal(20, host.Repository.Timings.Single(t => t.Standard == TimeStandardType.TransfusionStartAfterIssue).DurationMinutes);
        Assert.Equal("1 Platelets O+", host.Notifier.Published[0].Subject);
    }

    [Fact]
    public async Task BloodRequest_InvalidTransition_IsConflict()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        var stay = host.Ward.Admit(department);
        var id = await host.Commands.CreateBloodRequestAsync(new CreateBloodRequestCommand(stay.Code, BloodProductType.RedCells, AboGroup.A, true, 2, BloodRequestUrgency.Routine, BloodProductModifiers.None), CancellationToken.None);

        await Assert.ThrowsAsync<DomainException>(() => host.Commands.ChangeBloodRequestStatusAsync(new ChangeBloodRequestStatusCommand(id, BloodRequestStatus.Completed), CancellationToken.None));
    }

    [Fact]
    public async Task SetInventory_CreatesThenUpdates()
    {
        var host = new TestHost();
        await host.Commands.SetBloodInventoryAsync(new SetBloodInventoryCommand(BloodProductType.RedCells, AboGroup.O, 186, 150), CancellationToken.None);
        await host.Commands.SetBloodInventoryAsync(new SetBloodInventoryCommand(BloodProductType.RedCells, AboGroup.O, 140, 150), CancellationToken.None);

        var item = Assert.Single(host.Repository.Inventory);
        Assert.Equal(140, item.UnitsAvailable);
        Assert.Equal(StockLevel.Low, item.Level);
        Assert.Equal(2, host.Notifier.Published.Count);
    }

    [Fact]
    public async Task UnknownStay_Is404() =>
        await Assert.ThrowsAsync<ResourceNotFoundException>(() =>
            new TestHost().Commands.DischargeAsync(new DischargeStayCommand("ADM-424242", null), CancellationToken.None));
}
