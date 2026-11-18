using BEKP.Domain.Beds;
using BEKP.Domain.Departments;
using BEKP.Domain.Diseases;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Abstractions;

/// <summary>Persistence port for the inpatient operations model. Implemented by BEKP.Persistence.</summary>
public interface IInpatientRepository
{
    Task<IReadOnlyList<Department>> GetDepartmentsAsync(CancellationToken cancellationToken);

    Task<Department?> GetDepartmentByCodeAsync(string code, CancellationToken cancellationToken);

    Task<IReadOnlyList<Bed>> GetBedsAsync(Guid? departmentId, CancellationToken cancellationToken);

    Task<Bed?> GetBedAsync(Guid departmentId, string bedCode, CancellationToken cancellationToken);

    Task<Bed?> GetBedByIdAsync(Guid bedId, CancellationToken cancellationToken);

    Task<IReadOnlyList<InpatientStay>> GetActiveStaysAsync(Guid? departmentId, CancellationToken cancellationToken);

    Task<InpatientStay?> GetActiveStayByCodeAsync(string stayCode, CancellationToken cancellationToken);

    Task<bool> StayCodeExistsAsync(string stayCode, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountAdmissionsByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken);

    /// <summary>Admissions since <paramref name="fromUtc"/> grouped by department and disease group (discharged stays included).</summary>
    Task<IReadOnlyList<AdmissionCount>> CountAdmissionsByDiseaseAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> CountDischargesByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken);

    /// <summary>Episodes still awaiting antibiotics, plus those whose antibiotics started after <paramref name="recentSinceUtc"/>.</summary>
    Task<IReadOnlyList<FebrileNeutropeniaEpisode>> GetActiveEpisodesAsync(Guid? departmentId, DateTime recentSinceUtc, CancellationToken cancellationToken);

    Task<FebrileNeutropeniaEpisode?> GetEpisodeAsync(Guid episodeId, CancellationToken cancellationToken);

    Task<bool> HasOpenEpisodeAsync(Guid stayId, CancellationToken cancellationToken);

    Task<IReadOnlyList<BloodRequest>> GetPendingBloodRequestsAsync(Guid? departmentId, CancellationToken cancellationToken);

    Task<BloodRequest?> GetBloodRequestAsync(Guid requestId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, int>> SumUnitsIssuedByDepartmentAsync(DateTime fromUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<BloodInventoryItem>> GetBloodInventoryAsync(CancellationToken cancellationToken);

    Task<BloodInventoryItem?> GetInventoryItemAsync(BloodProductType product, AboGroup aboGroup, CancellationToken cancellationToken);

    Task<IReadOnlyList<OperationalEvent>> GetRecentEventsAsync(Guid? departmentId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProcessTiming>> GetProcessTimingsAsync(DateTime fromUtc, Guid? departmentId, CancellationToken cancellationToken);

    void Add(InpatientStay stay);

    void Add(ProcessTiming timing);

    void Add(FebrileNeutropeniaEpisode episode);

    void Add(BloodRequest request);

    void Add(BloodInventoryItem item);

    void Add(OperationalEvent operationalEvent);
}

public sealed record AdmissionCount(Guid DepartmentId, DiseaseGroup DiseaseGroup, int Count);
