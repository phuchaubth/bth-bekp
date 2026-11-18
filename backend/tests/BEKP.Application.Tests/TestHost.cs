using BEKP.Application.Common;
using BEKP.Application.Dashboard;
using BEKP.Application.Inpatient;
using BEKP.Application.TimeStandards;
using BEKP.Testing;
using Microsoft.Extensions.Options;

namespace BEKP.Application.Tests;

internal sealed class TestHost
{
    // 09:42 in Asia/Ho_Chi_Minh.
    public static readonly DateTime Now = new(2026, 11, 17, 2, 42, 0, DateTimeKind.Utc);

    public TestHost(Dictionary<Domain.Timing.TimeStandardType, TimeStandardSetting>? standards = null)
    {
        Clock = new FakeClock(Now);
        Ward = new WardBuilder(Repository, Now);
        Calendar = new HospitalCalendar(Options.Create(new HospitalTimeOptions()));
        Catalog = new TimeStandardCatalog(Options.Create(new TimeStandardsOptions { Standards = standards ?? [] }));
        Queries = new DashboardQueryService(Repository, Clock, Calendar);
        Commands = new InpatientCommandService(Repository, Repository, Notifier, Audit, new FixedActor("his-integration"), Clock);
        TimeStandards = new TimeStandardsService(Repository, Clock, Calendar, Catalog);
    }

    public InMemoryInpatientRepository Repository { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    public RecordingAuditLog Audit { get; } = new();

    public FakeClock Clock { get; }

    public WardBuilder Ward { get; }

    public HospitalCalendar Calendar { get; }

    public TimeStandardCatalog Catalog { get; }

    public DashboardQueryService Queries { get; }

    public InpatientCommandService Commands { get; }

    public TimeStandardsService TimeStandards { get; }
}
