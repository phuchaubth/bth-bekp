using BEKP.Application.Common;
using BEKP.Application.Dashboard;
using BEKP.Application.Inpatient;
using BEKP.Application.TimeStandards;
using BEKP.Domain.Beds;
using BEKP.Domain.Stays;
using BEKP.Persistence.Repositories;
using BEKP.Persistence.Seeding;
using BEKP.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BEKP.Persistence.Tests;

/// <summary>
/// Exercises EF Core mappings, LINQ translation and the synthetic seeder end-to-end on SQLite in-memory.
/// Production runs on SQL Server; provider-specific behaviour is covered by the SQL migration script.
/// </summary>
public sealed class PersistenceTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 17, 2, 42, 0, DateTimeKind.Utc);
    private readonly SqliteConnection connection = new("DataSource=:memory:");
    private BekpDbContext db = null!;

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        db = NewContext();
        await db.Database.EnsureCreatedAsync();
        var calendar = Calendar();
        Assert.True(await SyntheticDataSeeder.SeedAsync(db, Now, calendar.LocalDayStartUtc(Now), calendar.LocalDate(Now), CancellationToken.None));
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Fact]
    public async Task Seeder_IsIdempotent()
    {
        var calendar = Calendar();
        Assert.False(await SyntheticDataSeeder.SeedAsync(db, Now, calendar.LocalDayStartUtc(Now), calendar.LocalDate(Now), CancellationToken.None));
    }

    [Fact]
    public async Task Overview_ReadsSeededHospital()
    {
        await using var context = NewContext();
        var overview = await Queries(context).GetOverviewAsync(CancellationToken.None);

        Assert.Equal(8, overview.Departments.Count);
        Assert.Equal(272, overview.Totals.Capacity);
        Assert.Equal(249, overview.Totals.Occupied);
        Assert.Equal(60, overview.Totals.Neutropenic);
        Assert.Equal(16, overview.BloodInventory.Count);
        Assert.Equal(3, overview.FebrileNeutropenia.Count);
        Assert.Contains(overview.Alerts, a => a.Type == AlertType.BloodStockCritical && a.Subject == "Platelets:O");
        Assert.Contains(overview.Alerts, a => a.Type == AlertType.DepartmentOverCapacity && a.DepartmentCode == "DEP-0003");
        Assert.Contains(overview.Alerts, a => a.Type == AlertType.SterileRoomsFull && a.DepartmentCode == "DEP-0004");
        Assert.True(overview.DiseasePattern.Groups.Count >= 8);
        Assert.Equal(overview.Totals.Occupied, overview.DiseasePattern.ActiveTotal);
        Assert.True(overview.Totals.UnitsIssuedToday > 0);
        Assert.NotEmpty(overview.RecentEvents);
    }

    [Fact]
    public async Task Department_ReadsRoomsAndSterileZones()
    {
        await using var context = NewContext();
        var detail = await Queries(context).GetDepartmentAsync("DEP-0004", CancellationToken.None);

        Assert.Equal(12, detail.Summary.Capacity);
        Assert.Equal(["Khu vô trùng A", "Khu vô trùng B"], detail.Rooms.Select(r => r.Label));
        Assert.All(detail.Rooms.SelectMany(r => r.Beds), b => Assert.StartsWith("VT-", b.Code, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Commands_RoundTripThroughDatabase_AndTimeStandardsReadThem()
    {
        await using (var context = NewContext())
        {
            var repository = new InpatientRepository(context);
            var stay = (await repository.GetActiveStaysAsync(null, CancellationToken.None)).OrderBy(s => s.Code).First();
            await Commands(context).DischargeAsync(new DischargeStayCommand(stay.Code, null), CancellationToken.None);
        }

        await using (var context = NewContext())
        {
            var repository = new InpatientRepository(context);
            var cleaningBed = (await repository.GetBedsAsync(null, CancellationToken.None)).First(b => b.Status == BedStatus.Cleaning && !b.IsExtra);
            var department = (await repository.GetDepartmentsAsync(CancellationToken.None)).Single(d => d.Id == cleaningBed.DepartmentId);
            await Commands(context).UpdateBedStatusAsync(new UpdateBedStatusCommand(department.Code, cleaningBed.Code, BedStatus.Available), CancellationToken.None);
            await Commands(context).AdmitAsync(new AdmitStayCommand(department.Code, cleaningBed.Code, "ADM-900001", CareLevel.Level2, "C92.0", null, null), CancellationToken.None);
        }

        await using (var context = NewContext())
        {
            var repository = new InpatientRepository(context);
            var admitted = await repository.GetActiveStayByCodeAsync("ADM-900001", CancellationToken.None);
            Assert.NotNull(admitted);
            Assert.Equal(DateTimeKind.Utc, admitted.AdmittedAtUtc.Kind);

            var events = await repository.GetRecentEventsAsync(null, 3, CancellationToken.None);
            Assert.Equal("Admission", events[0].Type.ToString());

            var report = await new TimeStandardsService(repository, new FakeClock(Now), Calendar(), new TimeStandardCatalog(Options.Create(new TimeStandardsOptions())))
                .GetReportAsync(ReportPeriod.Last30Days, null, CancellationToken.None);
            Assert.All(report.Standards, s => Assert.True(s.Completed > 0));
        }
    }

    private static HospitalCalendar Calendar() => new(Options.Create(new HospitalTimeOptions()));

    private static DashboardQueryService Queries(BekpDbContext context) => new(new InpatientRepository(context), new FakeClock(Now), Calendar());

    private static InpatientCommandService Commands(BekpDbContext context) =>
        new(new InpatientRepository(context), context, new RecordingNotifier(), new RecordingAuditLog(), new FixedActor("test"), new FakeClock(Now));

    private BekpDbContext NewContext() => new(new DbContextOptionsBuilder<BekpDbContext>().UseSqlite(connection).Options);
}
