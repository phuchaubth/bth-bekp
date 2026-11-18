using BEKP.Application.Abstractions;
using BEKP.Application.Dashboard;
using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Testing;

public sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;

    public void Advance(TimeSpan by) => UtcNow += by;
}

public sealed class RecordingNotifier : IDashboardNotifier
{
    public List<OperationalEventDto> Published { get; } = [];

    public Task PublishAsync(OperationalEventDto change, CancellationToken cancellationToken)
    {
        Published.Add(change);
        return Task.CompletedTask;
    }
}

public sealed class RecordingAuditLog : IAuditLog
{
    public List<AuditEntry> Entries { get; } = [];

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

public sealed class FixedActor(string name) : ICurrentActor
{
    public string Name { get; } = name;
}

/// <summary>In-memory implementation of the persistence port for fast application and API tests.</summary>
public sealed class InMemoryInpatientRepository : IInpatientRepository, IUnitOfWork
{
    public List<Department> Departments { get; } = [];

    public List<Bed> Beds { get; } = [];

    public List<InpatientStay> Stays { get; } = [];

    public List<FebrileNeutropeniaEpisode> Episodes { get; } = [];

    public List<BloodRequest> Requests { get; } = [];

    public List<BloodInventoryItem> Inventory { get; } = [];

    public List<OperationalEvent> Events { get; } = [];

    public List<ProcessTiming> Timings { get; } = [];

    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken) =>
        Result<Department>(Departments.OrderBy(d => d.SortOrder));

    public Task<Department?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Departments.FirstOrDefault(d => d.Code == code));

    public Task<IReadOnlyList<Bed>> GetBedsAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        Result<Bed>(Beds.Where(b => departmentId == null || b.DepartmentId == departmentId).OrderBy(b => b.SortOrder));

    public Task<Bed?> GetBedAsync(Guid departmentId, string bedCode, CancellationToken cancellationToken) =>
        Task.FromResult(Beds.FirstOrDefault(b => b.DepartmentId == departmentId && b.Code == bedCode));

    public Task<Bed?> GetBedByIdAsync(Guid bedId, CancellationToken cancellationToken) =>
        Task.FromResult(Beds.FirstOrDefault(b => b.Id == bedId));

    public Task<IReadOnlyList<InpatientStay>> GetActiveStaysAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        Result<InpatientStay>(Stays.Where(s => s.IsActive && (departmentId == null || s.DepartmentId == departmentId)));

    public Task<InpatientStay?> GetActiveStayByCodeAsync(string stayCode, CancellationToken cancellationToken) =>
        Task.FromResult(Stays.FirstOrDefault(s => s.IsActive && s.Code == stayCode));

    public Task<bool> StayCodeExistsAsync(string stayCode, CancellationToken cancellationToken) =>
        Task.FromResult(Stays.Any(s => s.Code == stayCode));

    public Task<IReadOnlyDictionary<Guid, int>> CountAdmissionsByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        Count(Stays.Where(s => s.AdmittedAtUtc >= fromUtc).Select(s => (s.DepartmentId, 1)));

    public Task<IReadOnlyList<AdmissionCount>> CountAdmissionsByDiseaseAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken) =>
        Result<AdmissionCount>(Stays.Where(s => s.AdmittedAtUtc >= fromUtc && (departmentId == null || s.DepartmentId == departmentId))
            .GroupBy(s => new { s.DepartmentId, s.DiseaseGroup })
            .Select(g => new AdmissionCount(g.Key.DepartmentId, g.Key.DiseaseGroup, g.Count())));

    public Task<IReadOnlyDictionary<Guid, int>> CountDischargesByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        Count(Stays.Where(s => s.DischargedAtUtc >= fromUtc).Select(s => (s.DepartmentId, 1)));

    public Task<IReadOnlyList<FebrileNeutropeniaEpisode>> GetActiveEpisodesAsync(Guid? departmentId, DateTime recentSinceUtc, CancellationToken cancellationToken) =>
        Result<FebrileNeutropeniaEpisode>(Episodes.Where(e => (e.AntibioticStartedAtUtc == null || e.AntibioticStartedAtUtc >= recentSinceUtc) && (departmentId == null || e.DepartmentId == departmentId)));

    public Task<FebrileNeutropeniaEpisode?> GetEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
        Task.FromResult(Episodes.FirstOrDefault(e => e.Id == episodeId));

    public Task<bool> HasOpenEpisodeAsync(Guid stayId, CancellationToken cancellationToken) =>
        Task.FromResult(Episodes.Any(e => e.StayId == stayId && e.AwaitingAntibiotic));

    public Task<IReadOnlyList<BloodRequest>> GetPendingBloodRequestsAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        Result<BloodRequest>(Requests.Where(r => r.IsPending && (departmentId == null || r.DepartmentId == departmentId)));

    public Task<BloodRequest?> GetBloodRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        Task.FromResult(Requests.FirstOrDefault(r => r.Id == requestId));

    public Task<IReadOnlyDictionary<Guid, int>> SumUnitsIssuedByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        Count(Requests.Where(r => r.IssuedAtUtc >= fromUtc).Select(r => (r.DepartmentId, r.Units)));

    public Task<IReadOnlyList<BloodInventoryItem>> GetBloodInventoryAsync(CancellationToken cancellationToken) => Result<BloodInventoryItem>(Inventory);

    public Task<BloodInventoryItem?> GetInventoryItemAsync(BloodProductType product, AboGroup aboGroup, CancellationToken cancellationToken) =>
        Task.FromResult(Inventory.FirstOrDefault(i => i.Product == product && i.AboGroup == aboGroup));

    public Task<IReadOnlyList<OperationalEvent>> GetRecentEventsAsync(Guid? departmentId, int limit, CancellationToken cancellationToken) =>
        Result<OperationalEvent>(Events.Where(e => departmentId == null || e.DepartmentId == departmentId).OrderByDescending(e => e.OccurredAtUtc).Take(limit));

    public Task<IReadOnlyList<ProcessTiming>> GetProcessTimingsAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken) =>
        Result<ProcessTiming>(Timings.Where(t => t.CompletedAtUtc >= fromUtc && (departmentId == null || t.DepartmentId == departmentId)));

    public void Add(InpatientStay stay) => Stays.Add(stay);

    public void Add(ProcessTiming timing) => Timings.Add(timing);

    public void Add(FebrileNeutropeniaEpisode episode) => Episodes.Add(episode);

    public void Add(BloodRequest request) => Requests.Add(request);

    public void Add(BloodInventoryItem item) => Inventory.Add(item);

    public void Add(OperationalEvent operationalEvent) => Events.Add(operationalEvent);

    private static Task<IReadOnlyList<T>> Result<T>(IEnumerable<T> items) => Task.FromResult<IReadOnlyList<T>>(items.ToList());

    private static Task<IReadOnlyDictionary<Guid, int>> Count(IEnumerable<(Guid Key, int Value)> items) =>
        Task.FromResult<IReadOnlyDictionary<Guid, int>>(items.GroupBy(i => i.Key).ToDictionary(g => g.Key, g => g.Sum(i => i.Value)));
}

/// <summary>Builds small synthetic wards for tests (codes only, no personal data).</summary>
public sealed class WardBuilder(InMemoryInpatientRepository repository, DateTime now)
{
    private int stayNumber = 1;

    public Department Department(string code, string name, int beds, bool sterile = false, DepartmentGroup group = DepartmentGroup.MalignantHematology)
    {
        var department = new Department(Guid.NewGuid(), code, name, "Khoa mô phỏng", group, sterile, repository.Departments.Count);
        repository.Departments.Add(department);
        for (var i = 0; i < beds; i++)
        {
            repository.Beds.Add(new Bed(Guid.NewGuid(), department.Id, $"{100 + (i / 6)}-{(i % 6) + 1}", $"Phòng {100 + (i / 6)}", false, i, now.AddDays(-30)));
        }

        repository.Beds.Add(new Bed(Guid.NewGuid(), department.Id, "KT-1", "Kê thêm", true, 1000, now.AddDays(-30)));
        return department;
    }

    public InpatientStay Admit(Department department, string diagnosis = "C92.0", CareLevel care = CareLevel.Level3, bool neutropenic = false, bool extraBed = false, DateTime? admittedAt = null)
    {
        var bed = repository.Beds.First(b => b.DepartmentId == department.Id && b.Status == BedStatus.Available && b.IsExtra == extraBed);
        var at = admittedAt ?? now.AddDays(-3);
        bed.Occupy(at);
        var stay = new InpatientStay(Guid.NewGuid(), $"ADM-{stayNumber++:D6}", department.Id, bed.Id, at, care, diagnosis, null);
        if (neutropenic || care != CareLevel.Level3)
        {
            stay.UpdateClinicalStatus(care, diagnosis, neutropenic, false, false, null);
        }

        repository.Stays.Add(stay);
        return stay;
    }
}
