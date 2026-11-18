using BEKP.Application.Abstractions;
using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;
using Microsoft.EntityFrameworkCore;

namespace BEKP.Persistence.Repositories;

internal sealed class InpatientRepository(BekpDbContext db) : IInpatientRepository
{
    private static readonly BloodRequestStatus[] PendingStatuses =
        [BloodRequestStatus.Requested, BloodRequestStatus.CrossMatching, BloodRequestStatus.Approved, BloodRequestStatus.Issued];

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken) =>
        await db.Departments.OrderBy(d => d.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Department?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Departments.FirstOrDefaultAsync(d => d.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Bed>> GetBedsAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        await db.Beds.Where(b => departmentId == null || b.DepartmentId == departmentId)
            .OrderBy(b => b.SortOrder).ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<Bed?> GetBedAsync(Guid departmentId, string bedCode, CancellationToken cancellationToken) =>
        db.Beds.FirstOrDefaultAsync(b => b.DepartmentId == departmentId && b.Code == bedCode, cancellationToken);

    public Task<Bed?> GetBedByIdAsync(Guid bedId, CancellationToken cancellationToken) =>
        db.Beds.FirstOrDefaultAsync(b => b.Id == bedId, cancellationToken);

    public async Task<IReadOnlyList<InpatientStay>> GetActiveStaysAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        await db.InpatientStays.Where(s => s.DischargedAtUtc == null && (departmentId == null || s.DepartmentId == departmentId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<InpatientStay?> GetActiveStayByCodeAsync(string stayCode, CancellationToken cancellationToken) =>
        db.InpatientStays.FirstOrDefaultAsync(s => s.Code == stayCode && s.DischargedAtUtc == null, cancellationToken);

    public Task<bool> StayCodeExistsAsync(string stayCode, CancellationToken cancellationToken) =>
        db.InpatientStays.AnyAsync(s => s.Code == stayCode, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountAdmissionsByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        await db.InpatientStays.Where(s => s.AdmittedAtUtc >= fromUtc)
            .GroupBy(s => s.DepartmentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<AdmissionCount>> CountAdmissionsByDiseaseAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken)
    {
        var rows = await db.InpatientStays
            .Where(s => s.AdmittedAtUtc >= fromUtc && (departmentId == null || s.DepartmentId == departmentId))
            .GroupBy(s => new { s.DepartmentId, s.DiseaseGroup })
            .Select(g => new { g.Key.DepartmentId, g.Key.DiseaseGroup, Count = g.Count() })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => new AdmissionCount(r.DepartmentId, r.DiseaseGroup, r.Count)).ToList();
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountDischargesByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        await db.InpatientStays.Where(s => s.DischargedAtUtc >= fromUtc)
            .GroupBy(s => s.DepartmentId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<FebrileNeutropeniaEpisode>> GetActiveEpisodesAsync(Guid? departmentId, DateTime recentSinceUtc, CancellationToken cancellationToken) =>
        await db.FebrileNeutropeniaEpisodes
            .Where(e => (e.AntibioticStartedAtUtc == null || e.AntibioticStartedAtUtc >= recentSinceUtc) && (departmentId == null || e.DepartmentId == departmentId))
            .OrderBy(e => e.FeverDetectedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<FebrileNeutropeniaEpisode?> GetEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
        db.FebrileNeutropeniaEpisodes.FirstOrDefaultAsync(e => e.Id == episodeId, cancellationToken);

    public Task<bool> HasOpenEpisodeAsync(Guid stayId, CancellationToken cancellationToken) =>
        db.FebrileNeutropeniaEpisodes.AnyAsync(e => e.StayId == stayId && e.AntibioticStartedAtUtc == null, cancellationToken);

    public async Task<IReadOnlyList<BloodRequest>> GetPendingBloodRequestsAsync(Guid? departmentId, CancellationToken cancellationToken) =>
        await db.BloodRequests
            .Where(r => PendingStatuses.Contains(r.Status) && (departmentId == null || r.DepartmentId == departmentId))
            .OrderBy(r => r.RequestedAtUtc)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<BloodRequest?> GetBloodRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        db.BloodRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> SumUnitsIssuedByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken) =>
        await db.BloodRequests.Where(r => r.IssuedAtUtc >= fromUtc)
            .GroupBy(r => r.DepartmentId)
            .Select(g => new { g.Key, Units = g.Sum(r => r.Units) })
            .ToDictionaryAsync(x => x.Key, x => x.Units, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<BloodInventoryItem>> GetBloodInventoryAsync(CancellationToken cancellationToken) =>
        await db.BloodInventory.ToListAsync(cancellationToken).ConfigureAwait(false);

    public Task<BloodInventoryItem?> GetInventoryItemAsync(BloodProductType product, AboGroup aboGroup, CancellationToken cancellationToken) =>
        db.BloodInventory.FirstOrDefaultAsync(i => i.Product == product && i.AboGroup == aboGroup, cancellationToken);

    public async Task<IReadOnlyList<OperationalEvent>> GetRecentEventsAsync(Guid? departmentId, int limit, CancellationToken cancellationToken) =>
        await db.OperationalEvents
            .Where(e => departmentId == null || e.DepartmentId == departmentId)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<ProcessTiming>> GetProcessTimingsAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken) =>
        await db.ProcessTimings
            .Where(t => t.CompletedAtUtc >= fromUtc && (departmentId == null || t.DepartmentId == departmentId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

    public void Add(InpatientStay stay) => db.InpatientStays.Add(stay);

    public void Add(ProcessTiming timing) => db.ProcessTimings.Add(timing);

    public void Add(FebrileNeutropeniaEpisode episode) => db.FebrileNeutropeniaEpisodes.Add(episode);

    public void Add(BloodRequest request) => db.BloodRequests.Add(request);

    public void Add(BloodInventoryItem item) => db.BloodInventory.Add(item);

    public void Add(OperationalEvent operationalEvent) => db.OperationalEvents.Add(operationalEvent);
}
