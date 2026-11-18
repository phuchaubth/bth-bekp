using BEKP.Application.Abstractions;
using BEKP.Application.Common;
using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Diseases;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Occupancy;
using BEKP.Domain.Stays;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Dashboard;

public interface IDashboardQueryService
{
    Task<HospitalOverviewDto> GetOverviewAsync(CancellationToken cancellationToken);

    Task<DepartmentDetailDto> GetDepartmentAsync(string departmentCode, CancellationToken cancellationToken);

    Task<IReadOnlyList<DepartmentListItemDto>> GetDepartmentsAsync(CancellationToken cancellationToken);
}

internal sealed class DashboardQueryService(IInpatientRepository repository, IClock clock, HospitalCalendar calendar) : IDashboardQueryService
{
    /// <summary>Episodes whose antibiotics started within this window stay visible on the dashboard.</summary>
    internal static readonly TimeSpan RecentEpisodeWindow = TimeSpan.FromHours(2);

    internal const int RecentEventLimit = 30;

    internal const int DiseasePatternDays = 30;

    public async Task<IReadOnlyList<DepartmentListItemDto>> GetDepartmentsAsync(CancellationToken cancellationToken)
    {
        var departments = await repository.GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        return departments.OrderBy(d => d.SortOrder).Select(d => new DepartmentListItemDto(d.Code, d.Name, d.Group)).ToList();
    }

    public async Task<HospitalOverviewDto> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var snapshot = await LoadAsync(null, cancellationToken).ConfigureAwait(false);
        var summaries = snapshot.Departments.Select(snapshot.Summarize).ToList();
        var episodes = snapshot.Episodes.Select(snapshot.MapEpisode).ToList();
        var inventory = snapshot.Inventory
            .OrderBy(i => i.Product).ThenBy(i => i.AboGroup)
            .Select(i => new BloodInventoryDto(i.Product, i.AboGroup, i.UnitsAvailable, i.MinimumUnits, i.Level, i.UpdatedAtUtc))
            .ToList();

        var capacity = summaries.Sum(s => s.Capacity);
        var occupied = summaries.Sum(s => s.Occupied);
        var percent = OccupancyPolicy.Percent(occupied, capacity);
        var totals = new HospitalTotalsDto(
            capacity,
            occupied,
            percent,
            OccupancyPolicy.Classify(percent),
            summaries.Sum(s => s.Available),
            summaries.Sum(s => s.Cleaning),
            summaries.Where(s => s.IsSterileUnit).Sum(s => s.Available),
            summaries.Sum(s => s.Severe),
            summaries.Sum(s => s.Neutropenic),
            summaries.Sum(s => s.Transfusing),
            summaries.Sum(s => s.ChemotherapyToday),
            summaries.Sum(s => s.ExpectedDischargesToday),
            summaries.Sum(s => s.PendingBloodRequests),
            summaries.Sum(s => s.UnitsIssuedToday),
            summaries.Sum(s => s.AdmissionsToday),
            summaries.Sum(s => s.DischargesToday),
            episodes.Count,
            episodes.Count(e => e.AntibioticStartedAtUtc is null));

        return new HospitalOverviewDto(
            snapshot.Now,
            totals,
            summaries,
            AlertEngine.Evaluate(summaries, episodes, inventory),
            inventory,
            episodes,
            snapshot.DiseasePattern(),
            snapshot.Events.Select(snapshot.MapEvent).ToList());
    }

    public async Task<DepartmentDetailDto> GetDepartmentAsync(string departmentCode, CancellationToken cancellationToken)
    {
        var department = await repository.GetDepartmentByCodeAsync(departmentCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Department", departmentCode);

        var snapshot = await LoadAsync(department, cancellationToken).ConfigureAwait(false);
        var summary = snapshot.Summarize(department);
        var episodes = snapshot.Episodes.Select(snapshot.MapEpisode).ToList();
        var requests = snapshot.Requests
            .OrderByDescending(r => r.Urgency).ThenBy(r => r.RequestedAtUtc)
            .Select(snapshot.MapRequest)
            .ToList();

        var rooms = snapshot.Beds
            .Where(b => !b.IsExtra || b.Status == BedStatus.Occupied)
            .OrderBy(b => b.IsExtra).ThenBy(b => b.SortOrder)
            .GroupBy(b => b.IsExtra ? "Kê thêm" : b.RoomLabel)
            .Select(g => new RoomDto(g.Key, g.Select(snapshot.MapTile).ToList()))
            .ToList();

        var alerts = AlertEngine.Evaluate([summary], episodes, []);

        return new DepartmentDetailDto(
            snapshot.Now,
            summary,
            rooms,
            episodes,
            requests,
            alerts,
            snapshot.DiseasePattern(),
            snapshot.Events.Select(snapshot.MapEvent).ToList());
    }

    private async Task<Snapshot> LoadAsync(Department? only, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var dayStart = calendar.LocalDayStartUtc(now);
        var departmentId = only?.Id;

        var departments = only is null
            ? (await repository.GetDepartmentsAsync(ct).ConfigureAwait(false)).OrderBy(d => d.SortOrder).ToList()
            : [only];

        return new Snapshot(
            now,
            calendar.LocalDate(now),
            departments,
            await repository.GetBedsAsync(departmentId, ct).ConfigureAwait(false),
            await repository.GetActiveStaysAsync(departmentId, ct).ConfigureAwait(false),
            await repository.GetActiveEpisodesAsync(departmentId, now - RecentEpisodeWindow, ct).ConfigureAwait(false),
            await repository.GetPendingBloodRequestsAsync(departmentId, ct).ConfigureAwait(false),
            only is null ? await repository.GetBloodInventoryAsync(ct).ConfigureAwait(false) : [],
            await repository.CountAdmissionsByDepartmentAsync(dayStart, ct).ConfigureAwait(false),
            await repository.CountDischargesByDepartmentAsync(dayStart, ct).ConfigureAwait(false),
            await repository.SumUnitsIssuedByDepartmentAsync(dayStart, ct).ConfigureAwait(false),
            await repository.CountAdmissionsByDiseaseAsync(dayStart.AddDays(-(DiseasePatternDays - 1)), departmentId, ct).ConfigureAwait(false),
            await repository.GetRecentEventsAsync(departmentId, RecentEventLimit, ct).ConfigureAwait(false));
    }

    private sealed record Snapshot(
        DateTime Now,
        DateOnly Today,
        IReadOnlyList<Department> Departments,
        IReadOnlyList<Bed> Beds,
        IReadOnlyList<InpatientStay> Stays,
        IReadOnlyList<FebrileNeutropeniaEpisode> Episodes,
        IReadOnlyList<BloodRequest> Requests,
        IReadOnlyList<BloodInventoryItem> Inventory,
        IReadOnlyDictionary<Guid, int> AdmissionsToday,
        IReadOnlyDictionary<Guid, int> DischargesToday,
        IReadOnlyDictionary<Guid, int> UnitsIssuedToday,
        IReadOnlyList<AdmissionCount> RecentAdmissions,
        IReadOnlyList<OperationalEvent> Events)
    {
        private Dictionary<Guid, InpatientStay>? staysByBed;
        private Dictionary<Guid, InpatientStay>? staysById;
        private Dictionary<Guid, Bed>? bedsById;
        private Dictionary<Guid, Department>? departmentsById;

        private Dictionary<Guid, InpatientStay> StaysByBed => staysByBed ??= Stays.GroupBy(s => s.BedId).ToDictionary(g => g.Key, g => g.First());

        private Dictionary<Guid, InpatientStay> StaysById => staysById ??= Stays.ToDictionary(s => s.Id);

        private Dictionary<Guid, Bed> BedsById => bedsById ??= Beds.ToDictionary(b => b.Id);

        private Dictionary<Guid, Department> DepartmentsById => departmentsById ??= Departments.ToDictionary(d => d.Id);

        public bool ExpectedToday(InpatientStay stay) => stay.ExpectedDischargeDate is { } date && date <= Today;

        public DepartmentSummaryDto Summarize(Department department)
        {
            var beds = Beds.Where(b => b.DepartmentId == department.Id).OrderBy(b => b.SortOrder).ToList();
            var stays = Stays.Where(s => s.DepartmentId == department.Id).ToList();
            var planned = beds.Where(b => !b.IsExtra).ToList();
            var capacity = planned.Count;
            var occupied = stays.Count;
            var percent = OccupancyPolicy.Percent(occupied, capacity);

            var cells = beds
                .Where(b => !b.IsExtra || b.Status == BedStatus.Occupied)
                .OrderBy(b => b.IsExtra).ThenBy(b => b.SortOrder)
                .Select(b => new BedCellDto(b.Code, CellState(b), b.IsExtra))
                .ToList();

            return new DepartmentSummaryDto(
                department.Code,
                department.Name,
                department.Description,
                department.Group,
                department.IsSterileUnit,
                capacity,
                occupied,
                planned.Count(b => b.Status == BedStatus.Available),
                planned.Count(b => b.Status == BedStatus.Cleaning),
                beds.Count(b => b.IsExtra && b.Status == BedStatus.Occupied),
                percent,
                OccupancyPolicy.Classify(percent),
                stays.Count(s => s.IsSevere),
                stays.Count(s => s.IsNeutropenic),
                stays.Count(s => s.IsTransfusing),
                stays.Count(s => s.HasChemotherapyToday),
                stays.Count(ExpectedToday),
                Requests.Count(r => r.DepartmentId == department.Id),
                UnitsIssuedToday.GetValueOrDefault(department.Id),
                AdmissionsToday.GetValueOrDefault(department.Id),
                DischargesToday.GetValueOrDefault(department.Id),
                cells);
        }

        public BedTileDto MapTile(Bed bed)
        {
            BedOccupantDto? occupant = null;
            if (bed.Status == BedStatus.Occupied && StaysByBed.TryGetValue(bed.Id, out var stay))
            {
                occupant = new BedOccupantDto(
                    stay.Code,
                    stay.DayOfStay(Now),
                    stay.CareLevel,
                    stay.PrincipalDiagnosisCode,
                    stay.DiseaseGroup,
                    stay.IsSevere,
                    stay.IsNeutropenic,
                    stay.IsTransfusing,
                    stay.HasChemotherapyToday,
                    ExpectedToday(stay),
                    stay.ExpectedDischargeDate);
            }

            return new BedTileDto(bed.Code, bed.Status, bed.IsExtra, occupant);
        }

        public FebrileNeutropeniaDto MapEpisode(FebrileNeutropeniaEpisode episode)
        {
            var department = DepartmentsById.GetValueOrDefault(episode.DepartmentId);
            var stay = StaysById.GetValueOrDefault(episode.StayId);
            var bed = stay is null ? null : BedsById.GetValueOrDefault(stay.BedId);
            return new FebrileNeutropeniaDto(
                episode.Id,
                stay?.Code ?? "—",
                department?.Code ?? string.Empty,
                department?.Name ?? string.Empty,
                bed?.Code,
                episode.FeverDetectedAtUtc,
                episode.TemperatureCelsius,
                episode.AntibioticStartedAtUtc,
                episode.ElapsedMinutes(Now),
                episode.Evaluate(Now));
        }

        public BloodRequestDto MapRequest(BloodRequest request)
        {
            var stay = StaysById.GetValueOrDefault(request.StayId);
            var bed = stay is null ? null : BedsById.GetValueOrDefault(stay.BedId);
            var department = DepartmentsById.GetValueOrDefault(request.DepartmentId);
            var modifiers = Enum.GetValues<BloodProductModifiers>()
                .Where(m => m != BloodProductModifiers.None && request.Modifiers.HasFlag(m))
                .ToList();
            return new BloodRequestDto(
                request.Id,
                stay?.Code,
                department?.Code ?? string.Empty,
                bed?.Code,
                request.Product,
                request.AboGroup,
                request.RhDPositive,
                request.Units,
                request.Urgency,
                modifiers,
                request.Status,
                request.RequestedAtUtc,
                request.StatusChangedAtUtc);
        }

        public OperationalEventDto MapEvent(OperationalEvent e)
        {
            var name = e.DepartmentId is { } id ? DepartmentsById.GetValueOrDefault(id)?.Name : null;
            return new OperationalEventDto(e.Id, e.Type, e.OccurredAtUtc, e.DepartmentCode, name, e.StayCode, e.Subject, e.Value);
        }

        public DiseasePatternDto DiseasePattern()
        {
            var active = Stays.GroupBy(s => s.DiseaseGroup).ToDictionary(g => g.Key, g => g.Count());
            var admitted = RecentAdmissions.GroupBy(a => a.DiseaseGroup).ToDictionary(g => g.Key, g => g.Sum(a => a.Count));
            var groups = active.Keys.Union(admitted.Keys)
                .Select(g => new DiseasePatternEntryDto(g, active.GetValueOrDefault(g), admitted.GetValueOrDefault(g)))
                .OrderByDescending(e => e.ActiveInpatients)
                .ThenByDescending(e => e.AdmissionsLast30Days)
                .ThenBy(e => e.Group)
                .ToList();
            return new DiseasePatternDto(Stays.Count, admitted.Values.Sum(), groups);
        }

        private BedCellState CellState(Bed bed)
        {
            switch (bed.Status)
            {
                case BedStatus.Available:
                    return BedCellState.Available;
                case BedStatus.Cleaning:
                    return BedCellState.Cleaning;
                case BedStatus.OutOfService:
                    return BedCellState.OutOfService;
            }

            if (!StaysByBed.TryGetValue(bed.Id, out var stay))
            {
                return BedCellState.Occupied;
            }

            if (stay.IsSevere)
            {
                return BedCellState.Severe;
            }

            if (stay.IsNeutropenic)
            {
                return BedCellState.Neutropenic;
            }

            return ExpectedToday(stay) ? BedCellState.ExpectedDischarge : BedCellState.Occupied;
        }
    }
}
