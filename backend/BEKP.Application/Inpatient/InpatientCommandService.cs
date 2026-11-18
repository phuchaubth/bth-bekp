using System.Globalization;
using BEKP.Application.Abstractions;
using BEKP.Application.Common;
using BEKP.Application.Dashboard;
using BEKP.Application.TimeStandards;
using BEKP.Domain.Beds;
using BEKP.Domain.Common;
using BEKP.Domain.Diseases;
using BEKP.Domain.Events;
using BEKP.Domain.Neutropenia;
using BEKP.Domain.Stays;
using BEKP.Domain.Timing;
using BEKP.Domain.Transfusion;

namespace BEKP.Application.Inpatient;

/// <summary>Ingests inpatient events from HIS / LIS / blood bank integrations and broadcasts realtime changes.</summary>
public interface IInpatientCommandService
{
    Task AdmitAsync(AdmitStayCommand command, CancellationToken cancellationToken);

    Task DischargeAsync(DischargeStayCommand command, CancellationToken cancellationToken);

    Task UpdateClinicalStatusAsync(UpdateClinicalStatusCommand command, CancellationToken cancellationToken);

    Task UpdateBedStatusAsync(UpdateBedStatusCommand command, CancellationToken cancellationToken);

    Task<Guid> RecordFeverAsync(RecordFeverCommand command, CancellationToken cancellationToken);

    Task RecordAntibioticStartAsync(RecordAntibioticStartCommand command, CancellationToken cancellationToken);

    Task<Guid> CreateBloodRequestAsync(CreateBloodRequestCommand command, CancellationToken cancellationToken);

    Task ChangeBloodRequestStatusAsync(ChangeBloodRequestStatusCommand command, CancellationToken cancellationToken);

    Task SetBloodInventoryAsync(SetBloodInventoryCommand command, CancellationToken cancellationToken);
}

internal sealed class InpatientCommandService(
    IInpatientRepository repository,
    IUnitOfWork unitOfWork,
    IDashboardNotifier notifier,
    IAuditLog auditLog,
    ICurrentActor actor,
    IClock clock) : IInpatientCommandService
{
    /// <summary>Tolerated clock skew for timestamps sent by integrations.</summary>
    internal static readonly TimeSpan FutureTolerance = TimeSpan.FromMinutes(5);

    internal static readonly TimeSpan MaxBackdating = TimeSpan.FromDays(7);

    public async Task AdmitAsync(AdmitStayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
        var admittedAt = ResolveTimestamp(command.AdmittedAt, now, nameof(command.AdmittedAt));
        new Validator()
            .Require(BusinessCodes.IsDepartmentCode(command.DepartmentCode), nameof(command.DepartmentCode), "Must match DEP-0000.")
            .Require(BusinessCodes.IsBedCode(command.BedCode), nameof(command.BedCode), "Invalid bed code.")
            .Require(BusinessCodes.IsStayCode(command.StayCode), nameof(command.StayCode), "Must match ADM-000000.")
            .Require(Enum.IsDefined(command.CareLevel), nameof(command.CareLevel), "Unknown care level.")
            .Require(DiseaseClassifier.IsValidIcd10(command.PrincipalDiagnosisCode), nameof(command.PrincipalDiagnosisCode), "Must be an ICD-10 code, e.g. C92.0.")
            .ThrowIfInvalid();

        var department = await repository.GetDepartmentByCodeAsync(command.DepartmentCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Department", command.DepartmentCode);
        var bed = await repository.GetBedAsync(department.Id, command.BedCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Bed", command.BedCode);
        if (await repository.StayCodeExistsAsync(command.StayCode, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException($"Stay {command.StayCode} already exists.");
        }

        bed.Occupy(now);
        repository.Add(new InpatientStay(Guid.NewGuid(), command.StayCode, department.Id, bed.Id, admittedAt, command.CareLevel, command.PrincipalDiagnosisCode, command.ExpectedDischargeDate));

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.Admission, now, department.Id, department.Code, command.StayCode, bed.Code, null), "inpatient.admit", "InpatientStay", command.StayCode, cancellationToken).ConfigureAwait(false);
    }

    public async Task DischargeAsync(DischargeStayCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
        var dischargedAt = ResolveTimestamp(command.DischargedAt, now, nameof(command.DischargedAt));
        var stay = await GetActiveStayAsync(command.StayCode, cancellationToken).ConfigureAwait(false);
        var bed = await repository.GetBedByIdAsync(stay.BedId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Bed", stay.BedId.ToString());
        var department = await GetDepartmentCodeAsync(stay.DepartmentId, cancellationToken).ConfigureAwait(false);

        stay.Discharge(dischargedAt);
        bed.Release(now);

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.Discharge, now, stay.DepartmentId, department, stay.Code, bed.Code, null), "inpatient.discharge", "InpatientStay", stay.Code, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateClinicalStatusAsync(UpdateClinicalStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator()
            .Require(Enum.IsDefined(command.CareLevel), nameof(command.CareLevel), "Unknown care level.")
            .Require(DiseaseClassifier.IsValidIcd10(command.PrincipalDiagnosisCode), nameof(command.PrincipalDiagnosisCode), "Must be an ICD-10 code, e.g. C92.0.")
            .ThrowIfInvalid();
        var now = clock.UtcNow;
        var stay = await GetActiveStayAsync(command.StayCode, cancellationToken).ConfigureAwait(false);
        var department = await GetDepartmentCodeAsync(stay.DepartmentId, cancellationToken).ConfigureAwait(false);
        var bed = await repository.GetBedByIdAsync(stay.BedId, cancellationToken).ConfigureAwait(false);

        stay.UpdateClinicalStatus(command.CareLevel, command.PrincipalDiagnosisCode, command.IsNeutropenic, command.IsTransfusing, command.HasChemotherapyToday, command.ExpectedDischargeDate);

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.ClinicalStatusChanged, now, stay.DepartmentId, department, stay.Code, bed?.Code, null), "inpatient.clinical-status", "InpatientStay", stay.Code, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateBedStatusAsync(UpdateBedStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator()
            .Require(BusinessCodes.IsDepartmentCode(command.DepartmentCode), nameof(command.DepartmentCode), "Must match DEP-0000.")
            .Require(BusinessCodes.IsBedCode(command.BedCode), nameof(command.BedCode), "Invalid bed code.")
            .Require(Enum.IsDefined(command.Status) && command.Status != BedStatus.Occupied, nameof(command.Status), "Status must be Available, Cleaning or OutOfService.")
            .ThrowIfInvalid();
        var now = clock.UtcNow;
        var department = await repository.GetDepartmentByCodeAsync(command.DepartmentCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Department", command.DepartmentCode);
        var bed = await repository.GetBedAsync(department.Id, command.BedCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Bed", command.BedCode);

        var previous = bed.Status;
        var cleaningStartedAt = bed.StatusChangedAtUtc;
        bed.SetHousekeepingStatus(command.Status, now);
        if (previous == BedStatus.Cleaning && command.Status == BedStatus.Available)
        {
            repository.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardType.BedTurnaround, department.Id, cleaningStartedAt, now, bed.Code));
        }

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.BedStatusChanged, now, department.Id, department.Code, null, bed.Code, command.Status.ToString()), "bed.status", "Bed", $"{department.Code}/{bed.Code}", cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> RecordFeverAsync(RecordFeverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator()
            .Require(
                command.TemperatureCelsius >= FebrileNeutropeniaEpisode.MinimumFeverCelsius && command.TemperatureCelsius <= FebrileNeutropeniaEpisode.MaximumPlausibleCelsius,
                nameof(command.TemperatureCelsius),
                $"Must be between {FebrileNeutropeniaEpisode.MinimumFeverCelsius} and {FebrileNeutropeniaEpisode.MaximumPlausibleCelsius} °C.")
            .ThrowIfInvalid();
        var now = clock.UtcNow;
        var detectedAt = ResolveTimestamp(command.DetectedAt, now, nameof(command.DetectedAt));
        var stay = await GetActiveStayAsync(command.StayCode, cancellationToken).ConfigureAwait(false);
        if (!stay.IsNeutropenic)
        {
            throw new DomainException($"Stay {stay.Code} is not flagged as severely neutropenic.");
        }

        if (await repository.HasOpenEpisodeAsync(stay.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new DomainException($"Stay {stay.Code} already has an episode awaiting antibiotics.");
        }

        var episode = new FebrileNeutropeniaEpisode(Guid.NewGuid(), stay.Id, stay.DepartmentId, detectedAt, command.TemperatureCelsius);
        repository.Add(episode);
        var department = await GetDepartmentCodeAsync(stay.DepartmentId, cancellationToken).ConfigureAwait(false);
        var bed = await repository.GetBedByIdAsync(stay.BedId, cancellationToken).ConfigureAwait(false);

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.FeverRecorded, now, stay.DepartmentId, department, stay.Code, bed?.Code, command.TemperatureCelsius.ToString("0.0", CultureInfo.InvariantCulture)), "fn.fever", "FebrileNeutropeniaEpisode", episode.Id.ToString(), cancellationToken).ConfigureAwait(false);
        return episode.Id;
    }

    public async Task RecordAntibioticStartAsync(RecordAntibioticStartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = clock.UtcNow;
        var startedAt = ResolveTimestamp(command.StartedAt, now, nameof(command.StartedAt));
        var episode = await repository.GetEpisodeAsync(command.EpisodeId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Febrile neutropenia episode", command.EpisodeId.ToString());

        episode.RecordAntibioticStart(startedAt);
        var stays = await repository.GetActiveStaysAsync(episode.DepartmentId, cancellationToken).ConfigureAwait(false);
        var stayCode = stays.FirstOrDefault(s => s.Id == episode.StayId)?.Code;
        repository.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardType.FebrileNeutropeniaAntibiotic, episode.DepartmentId, episode.FeverDetectedAtUtc, startedAt, stayCode));
        var department = await GetDepartmentCodeAsync(episode.DepartmentId, cancellationToken).ConfigureAwait(false);

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.AntibioticStarted, now, episode.DepartmentId, department, stayCode, null, episode.ElapsedMinutes(now).ToString(CultureInfo.InvariantCulture)), "fn.antibiotic", "FebrileNeutropeniaEpisode", episode.Id.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<Guid> CreateBloodRequestAsync(CreateBloodRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator()
            .Require(Enum.IsDefined(command.Product), nameof(command.Product), "Unknown blood product.")
            .Require(Enum.IsDefined(command.AboGroup), nameof(command.AboGroup), "Unknown ABO group.")
            .Require(Enum.IsDefined(command.Urgency), nameof(command.Urgency), "Unknown urgency.")
            .Require(command.Units is >= 1 and <= BloodRequest.MaxUnits, nameof(command.Units), $"Must be between 1 and {BloodRequest.MaxUnits}.")
            .Require(((int)command.Modifiers & ~0b1111) == 0, nameof(command.Modifiers), "Unknown modifier.")
            .ThrowIfInvalid();
        var now = clock.UtcNow;
        var stay = await GetActiveStayAsync(command.StayCode, cancellationToken).ConfigureAwait(false);
        var request = new BloodRequest(Guid.NewGuid(), stay.Id, stay.DepartmentId, command.Product, command.AboGroup, command.RhDPositive, command.Units, command.Urgency, command.Modifiers, now);
        repository.Add(request);
        var department = await GetDepartmentCodeAsync(stay.DepartmentId, cancellationToken).ConfigureAwait(false);

        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.BloodRequested, now, stay.DepartmentId, department, stay.Code, Describe(request), command.Urgency.ToString()), "blood.request", "BloodRequest", request.Id.ToString(), cancellationToken).ConfigureAwait(false);
        return request.Id;
    }

    public async Task ChangeBloodRequestStatusAsync(ChangeBloodRequestStatusCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator().Require(Enum.IsDefined(command.Status), nameof(command.Status), "Unknown status.").ThrowIfInvalid();
        var now = clock.UtcNow;
        var request = await repository.GetBloodRequestAsync(command.RequestId, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Blood request", command.RequestId.ToString());

        request.ChangeStatus(command.Status, now);
        var stays = await repository.GetActiveStaysAsync(request.DepartmentId, cancellationToken).ConfigureAwait(false);
        var stayCode = stays.FirstOrDefault(s => s.Id == request.StayId)?.Code;

        if (command.Status == BloodRequestStatus.Issued)
        {
            var stock = await repository.GetInventoryItemAsync(request.Product, request.AboGroup, cancellationToken).ConfigureAwait(false);
            stock?.Withdraw(request.Units, now);
            repository.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardsService.IssueStandardFor(request.Urgency), request.DepartmentId, request.RequestedAtUtc, now, stayCode));
        }
        else if (command.Status == BloodRequestStatus.Transfusing && request.IssuedAtUtc is { } issuedAt)
        {
            repository.Add(new ProcessTiming(Guid.NewGuid(), TimeStandardType.TransfusionStartAfterIssue, request.DepartmentId, issuedAt, now, stayCode));
        }

        var department = await GetDepartmentCodeAsync(request.DepartmentId, cancellationToken).ConfigureAwait(false);
        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.BloodRequestStatusChanged, now, request.DepartmentId, department, stayCode, Describe(request), command.Status.ToString()), "blood.request.status", "BloodRequest", request.Id.ToString(), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetBloodInventoryAsync(SetBloodInventoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        new Validator()
            .Require(Enum.IsDefined(command.Product), nameof(command.Product), "Unknown blood product.")
            .Require(Enum.IsDefined(command.AboGroup), nameof(command.AboGroup), "Unknown ABO group.")
            .Require(command.UnitsAvailable is >= 0 and <= BloodInventoryItem.MaxUnits, nameof(command.UnitsAvailable), "Out of range.")
            .Require(command.MinimumUnits is >= 0 and <= BloodInventoryItem.MaxUnits, nameof(command.MinimumUnits), "Out of range.")
            .ThrowIfInvalid();
        var now = clock.UtcNow;
        var item = await repository.GetInventoryItemAsync(command.Product, command.AboGroup, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            repository.Add(new BloodInventoryItem(Guid.NewGuid(), command.Product, command.AboGroup, command.UnitsAvailable, command.MinimumUnits, now));
        }
        else
        {
            item.Update(command.UnitsAvailable, command.MinimumUnits, now);
        }

        var subject = $"{command.Product} {command.AboGroup}";
        await CommitAsync(new OperationalEvent(Guid.NewGuid(), OperationalEventType.BloodInventoryUpdated, now, null, null, null, subject, command.UnitsAvailable.ToString(CultureInfo.InvariantCulture)), "blood.inventory", "BloodInventoryItem", subject, cancellationToken).ConfigureAwait(false);
    }

    internal static DateTime ResolveTimestamp(DateTimeOffset? value, DateTime now, string field)
    {
        if (value is null)
        {
            return now;
        }

        var utc = value.Value.UtcDateTime;
        if (utc > now + FutureTolerance)
        {
            throw new RequestValidationException(field, "Must not be in the future.");
        }

        if (utc < now - MaxBackdating)
        {
            throw new RequestValidationException(field, "Must not be older than 7 days.");
        }

        return utc;
    }

    private static string Describe(BloodRequest request) =>
        string.Create(CultureInfo.InvariantCulture, $"{request.Units} {request.Product} {request.AboGroup}{(request.RhDPositive ? "+" : "-")}");

    private async Task<InpatientStay> GetActiveStayAsync(string stayCode, CancellationToken cancellationToken)
    {
        new Validator().Require(BusinessCodes.IsStayCode(stayCode), "StayCode", "Must match ADM-000000.").ThrowIfInvalid();
        return await repository.GetActiveStayByCodeAsync(stayCode, cancellationToken).ConfigureAwait(false)
            ?? throw new ResourceNotFoundException("Active stay", stayCode);
    }

    private async Task<string?> GetDepartmentCodeAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var departments = await repository.GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        return departments.FirstOrDefault(d => d.Id == departmentId)?.Code;
    }

    private async Task CommitAsync(OperationalEvent operationalEvent, string action, string entityType, string entityKey, CancellationToken cancellationToken)
    {
        repository.Add(operationalEvent);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await auditLog.WriteAsync(new AuditEntry(actor.Name, action, entityType, entityKey, operationalEvent.OccurredAtUtc), cancellationToken).ConfigureAwait(false);

        var departments = await repository.GetDepartmentsAsync(cancellationToken).ConfigureAwait(false);
        var name = departments.FirstOrDefault(d => d.Id == operationalEvent.DepartmentId)?.Name;
        await notifier.PublishAsync(
            new OperationalEventDto(operationalEvent.Id, operationalEvent.Type, operationalEvent.OccurredAtUtc, operationalEvent.DepartmentCode, name, operationalEvent.StayCode, operationalEvent.Subject, operationalEvent.Value),
            cancellationToken).ConfigureAwait(false);
    }
}
