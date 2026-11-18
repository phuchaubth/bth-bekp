using BEKP.Application.Common;
using BEKP.Application.Dashboard;
using BEKP.Domain.Beds;
using BEKP.Domain.Diseases;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Occupancy;
using BEKP.Domain.Stays;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Tests;

public sealed class DashboardQueryServiceTests
{
    [Fact]
    public async Task Overview_AggregatesOccupancyAndHematologyCounts()
    {
        var host = new TestHost();
        var leukemia = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 4);
        var thal = host.Ward.Department("DEP-0005", "Thalassemia", beds: 4, group: Domain.Departments.DepartmentGroup.BenignHematology);
        host.Ward.Admit(leukemia, care: CareLevel.Level1);
        host.Ward.Admit(leukemia, neutropenic: true, care: CareLevel.Level2);
        host.Ward.Admit(leukemia, diagnosis: "C91.0");
        host.Ward.Admit(thal, diagnosis: "D56.1");
        host.Repository.Beds.First(b => b.DepartmentId == thal.Id && b.Status == BedStatus.Available).SetHousekeepingStatus(BedStatus.Cleaning, TestHost.Now);

        var overview = await host.Queries.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(8, overview.Totals.Capacity);
        Assert.Equal(4, overview.Totals.Occupied);
        Assert.Equal(50.0m, overview.Totals.OccupancyPercent);
        Assert.Equal(3, overview.Totals.Available);
        Assert.Equal(1, overview.Totals.Cleaning);
        Assert.Equal(1, overview.Totals.Severe);
        Assert.Equal(1, overview.Totals.Neutropenic);

        var row = overview.Departments.Single(d => d.Code == "DEP-0001");
        Assert.Equal(75.0m, row.OccupancyPercent);
        Assert.Equal(OccupancyLevel.Normal, row.OccupancyLevel);
        Assert.Equal(4, row.Beds.Count);
        Assert.Contains(row.Beds, c => c.State == BedCellState.Severe);
        Assert.Contains(row.Beds, c => c.State == BedCellState.Neutropenic);
    }

    [Fact]
    public async Task Overview_CountsExtraBedsAsOverCapacity_AndRaisesAlert()
    {
        var host = new TestHost();
        var pediatrics = host.Ward.Department("DEP-0003", "Huyết học Nhi", beds: 2, group: Domain.Departments.DepartmentGroup.PediatricHematology);
        host.Ward.Admit(pediatrics, diagnosis: "C91.0");
        host.Ward.Admit(pediatrics, diagnosis: "C91.0");
        host.Ward.Admit(pediatrics, diagnosis: "C91.0", extraBed: true);

        var overview = await host.Queries.GetOverviewAsync(CancellationToken.None);
        var row = overview.Departments.Single();

        Assert.Equal(2, row.Capacity);
        Assert.Equal(3, row.Occupied);
        Assert.Equal(1, row.ExtraBedsInUse);
        Assert.Equal(150.0m, row.OccupancyPercent);
        Assert.Equal(OccupancyLevel.OverCapacity, row.OccupancyLevel);
        Assert.Contains(row.Beds, c => c.IsExtra);
        var alert = Assert.Single(overview.Alerts, a => a.Type == AlertType.DepartmentOverCapacity);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Equal(1, alert.Reference);
    }

    [Fact]
    public async Task Overview_ReportsFebrileNeutropenia_WithElapsedTimeAndAlert()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 2);
        var stay = host.Ward.Admit(department, neutropenic: true, care: CareLevel.Level2);
        host.Repository.Episodes.Add(new FebrileNeutropeniaEpisode(Guid.NewGuid(), stay.Id, department.Id, TestHost.Now.AddMinutes(-42), 38.9m));

        var overview = await host.Queries.GetOverviewAsync(CancellationToken.None);

        var episode = Assert.Single(overview.FebrileNeutropenia);
        Assert.Equal(42, episode.ElapsedMinutes);
        Assert.Equal(FebrileNeutropeniaStatus.Warning, episode.Status);
        Assert.Equal(stay.Code, episode.StayCode);
        Assert.Equal(1, overview.Totals.FebrileNeutropeniaAwaitingAntibiotic);
        var alert = Assert.Single(overview.Alerts, a => a.Type == AlertType.FebrileNeutropeniaAntibioticDelay);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
    }

    [Fact]
    public async Task Overview_FlagsCriticalAndLowBloodStock()
    {
        var host = new TestHost();
        host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 1);
        host.Repository.Inventory.Add(new BloodInventoryItem(Guid.NewGuid(), BloodProductType.Platelets, AboGroup.O, 9, 20, TestHost.Now));
        host.Repository.Inventory.Add(new BloodInventoryItem(Guid.NewGuid(), BloodProductType.RedCells, AboGroup.B, 96, 110, TestHost.Now));
        host.Repository.Inventory.Add(new BloodInventoryItem(Guid.NewGuid(), BloodProductType.Plasma, AboGroup.A, 160, 120, TestHost.Now));

        var overview = await host.Queries.GetOverviewAsync(CancellationToken.None);

        Assert.Equal(3, overview.BloodInventory.Count);
        Assert.Equal("Platelets:O", Assert.Single(overview.Alerts, a => a.Type == AlertType.BloodStockCritical).Subject);
        Assert.Equal("RedCells:B", Assert.Single(overview.Alerts, a => a.Type == AlertType.BloodStockLow).Subject);
        Assert.Equal(AlertSeverity.Critical, overview.Alerts[0].Severity);
    }

    [Fact]
    public async Task Overview_BuildsDiseasePattern_FromActiveAndRecentAdmissions()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 6);
        host.Ward.Admit(department, diagnosis: "C92.0");
        host.Ward.Admit(department, diagnosis: "C92.0");
        host.Ward.Admit(department, diagnosis: "C91.0");
        var old = host.Ward.Admit(department, diagnosis: "D61.9", admittedAt: TestHost.Now.AddDays(-45));
        old.Discharge(TestHost.Now.AddDays(-40));

        var pattern = (await host.Queries.GetOverviewAsync(CancellationToken.None)).DiseasePattern;

        Assert.Equal(3, pattern.ActiveTotal);
        Assert.Equal(3, pattern.AdmissionsTotal);
        Assert.Equal(DiseaseGroup.AcuteMyeloidLeukemia, pattern.Groups[0].Group);
        Assert.Equal(2, pattern.Groups[0].ActiveInpatients);
        Assert.DoesNotContain(pattern.Groups, g => g.Group == DiseaseGroup.AplasticAnemia);
    }

    [Fact]
    public async Task Department_GroupsBedsByRoom_AndExposesOccupantFlagsWithoutPersonalData()
    {
        var host = new TestHost();
        var department = host.Ward.Department("DEP-0001", "Bạch cầu cấp người lớn", beds: 8);
        var stay = host.Ward.Admit(department, neutropenic: true, care: CareLevel.Level2);

        var detail = await host.Queries.GetDepartmentAsync("DEP-0001", CancellationToken.None);

        Assert.Equal(["Phòng 100", "Phòng 101"], detail.Rooms.Select(r => r.Label));
        var occupant = detail.Rooms.SelectMany(r => r.Beds).Single(b => b.Occupant is not null).Occupant!;
        Assert.Equal(stay.Code, occupant.StayCode);
        Assert.True(occupant.IsNeutropenic);
        Assert.Equal(4, occupant.DayOfStay);
        Assert.Equal(DiseaseGroup.AcuteMyeloidLeukemia, occupant.DiseaseGroup);
    }

    [Fact]
    public async Task Department_UnknownCode_Throws404() =>
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => new TestHost().Queries.GetDepartmentAsync("DEP-9999", CancellationToken.None));
}
