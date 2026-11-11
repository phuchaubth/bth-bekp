using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Domain.Auditing;
using Consent.Domain.Common;
using Consent.Domain.Patients;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.Patients;

/// <summary>
/// Quản lý người bệnh: tìm kiếm (số CCCD được che), xem hồ sơ (có ghi nhật ký truy cập), tra cứu khi quét CCCD
/// (hồ sơ đã có + HIS), tạo/cập nhật và người đại diện.
/// </summary>
public sealed class PatientService(
    IPatientRepository patients,
    IHisGateway his,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IOptions<HisOptions> hisOptions,
    IValidator<PatientRequest> patientValidator,
    IValidator<RepresentativeRequest> representativeValidator,
    IValidator<PatientLookupRequest> lookupValidator)
{
    private static readonly Error NotFound = Error.NotFound("patients.not_found", "Không tìm thấy người bệnh.");

    public async Task<PagedResult<PatientListItemDto>> SearchAsync(PatientQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var result = await patients.SearchAsync(query with { Keyword = query.Keyword?.Trim(), Page = page, PageSize = pageSize }, cancellationToken)
            .ConfigureAwait(false);
        var items = result.Items
            .Select(p => new PatientListItemDto(
                p.Id,
                p.PatientCode,
                p.FullName,
                p.DateOfBirth,
                p.Gender,
                SensitiveMask.CitizenId(p.CitizenId),
                p.HisVerifiedAtUtc is not null,
                p.UpdatedAtUtc))
            .ToList();
        return new PagedResult<PatientListItemDto>(items, result.Page, result.PageSize, result.TotalCount);
    }

    /// <summary>Xem hồ sơ đầy đủ. Mỗi lần xem đều được ghi nhật ký (ai xem hồ sơ nào, lúc nào).</summary>
    public async Task<Result<PatientDetailDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var patient = await patients.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (patient is null)
        {
            return NotFound;
        }

        AddAudit(AuditActions.PatientViewed, patient.Id, null);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PatientDetailDto.From(patient);
    }

    /// <summary>Tra cứu khi quét CCCD: hồ sơ đã có trong hệ thống và (nếu đã kết nối) thông tin người bệnh trên HIS.</summary>
    public async Task<Result<PatientLookupResult>> LookupAsync(PatientLookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await lookupValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var citizenId = request.CitizenId.Trim();
        var existing = await patients.FindByCitizenIdAsync(citizenId, cancellationToken).ConfigureAwait(false);
        var hisResult = await LookupHisAsync(citizenId, cancellationToken).ConfigureAwait(false);

        // Không ghi số CCCD vào nhật ký.
        AddAudit(AuditActions.PatientLookedUp, existing?.Id, new { existing = existing is not null, his = hisResult.Status.ToString() });
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PatientLookupResult(
            existing is null ? null : PatientDetailDto.From(existing), hisResult.Status, hisResult.Patient);
    }

    public async Task<Result<PatientDetailDto>> CreateAsync(PatientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await patientValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        if (await CheckUniquenessAsync(request, null, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        var patient = Patient.Create(request.ToDetails(), clock.UtcNow);
        await ApplyHisVerificationAsync(patient, cancellationToken).ConfigureAwait(false);
        patients.Add(patient);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PatientDetailDto.From(patient);
    }

    public async Task<Result<PatientDetailDto>> UpdateAsync(Guid id, PatientRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await patientValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var patient = await patients.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        if (patient is null)
        {
            return NotFound;
        }

        if (await CheckUniquenessAsync(request, id, cancellationToken).ConfigureAwait(false) is { } conflict)
        {
            return conflict;
        }

        patient.Update(request.ToDetails(), clock.UtcNow);
        await ApplyHisVerificationAsync(patient, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PatientDetailDto.From(patient);
    }

    public Task<Result<PatientDetailDto>> AddRepresentativeAsync(
        Guid patientId, RepresentativeRequest request, CancellationToken cancellationToken) =>
        ChangeRepresentativesAsync(patientId, request, (patient, details) => patient.AddRepresentative(details, clock.UtcNow), cancellationToken);

    public Task<Result<PatientDetailDto>> UpdateRepresentativeAsync(
        Guid patientId, Guid representativeId, RepresentativeRequest request, CancellationToken cancellationToken) =>
        ChangeRepresentativesAsync(
            patientId,
            request,
            (patient, details) => patient.UpdateRepresentative(representativeId, details, clock.UtcNow),
            cancellationToken,
            representativeId);

    public async Task<Result<PatientDetailDto>> RemoveRepresentativeAsync(
        Guid patientId, Guid representativeId, CancellationToken cancellationToken)
    {
        var patient = await patients.GetByIdAsync(patientId, cancellationToken).ConfigureAwait(false);
        if (patient is null)
        {
            return NotFound;
        }

        if (patient.Representatives.All(r => r.Id != representativeId))
        {
            return RepresentativeNotFound;
        }

        patient.RemoveRepresentative(representativeId, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PatientDetailDto.From(patient);
    }

    private static Error RepresentativeNotFound { get; } =
        Error.NotFound("patients.representative_not_found", "Không tìm thấy người đại diện.");

    private async Task<Result<PatientDetailDto>> ChangeRepresentativesAsync(
        Guid patientId,
        RepresentativeRequest request,
        Action<Patient, RepresentativeDetails> change,
        CancellationToken cancellationToken,
        Guid? representativeId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await representativeValidator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var patient = await patients.GetByIdAsync(patientId, cancellationToken).ConfigureAwait(false);
        if (patient is null)
        {
            return NotFound;
        }

        if (representativeId is { } id && patient.Representatives.All(r => r.Id != id))
        {
            return RepresentativeNotFound;
        }

        change(patient, request.ToDetails());
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return PatientDetailDto.From(patient);
    }

    private async Task<Error?> CheckUniquenessAsync(PatientRequest request, Guid? patientId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.CitizenId))
        {
            var existing = await patients.FindByCitizenIdAsync(request.CitizenId.Trim(), cancellationToken).ConfigureAwait(false);
            if (existing is not null && existing.Id != patientId)
            {
                return Error.Conflict("patients.duplicate_citizen_id", "Đã có hồ sơ người bệnh với số CCCD này.")
                    with { Metadata = new Dictionary<string, object?> { ["existingPatientId"] = existing.Id } };
            }
        }

        if (!string.IsNullOrWhiteSpace(request.PatientCode)
            && await patients.PatientCodeExistsAsync(request.PatientCode.Trim(), patientId, cancellationToken).ConfigureAwait(false))
        {
            return Error.Conflict("patients.duplicate_patient_code", "Mã người bệnh đã được dùng cho hồ sơ khác.");
        }

        return null;
    }

    /// <summary>
    /// Đối chiếu với HIS khi lưu (máy chủ tự kiểm tra, không tin cờ do client gửi lên).
    /// HIS không khả dụng thì vẫn lưu và hồ sơ ở trạng thái "chưa đối chiếu HIS". Chưa kết nối HIS thì bỏ qua.
    /// </summary>
    private async Task ApplyHisVerificationAsync(Patient patient, CancellationToken cancellationToken)
    {
        if (!his.IsEnabled || patient.CitizenId is null)
        {
            return;
        }

        var result = await LookupHisAsync(patient.CitizenId, cancellationToken).ConfigureAwait(false);
        if (result is { Status: HisLookupStatus.Found, Patient: { } hisPatient }
            && (patient.PatientCode is null || string.Equals(patient.PatientCode, hisPatient.PatientCode, StringComparison.OrdinalIgnoreCase)))
        {
            patient.MarkHisVerified(hisPatient.PatientCode, clock.UtcNow);
        }
    }

    private async Task<HisLookupResult> LookupHisAsync(string citizenId, CancellationToken cancellationToken)
    {
        if (!his.IsEnabled)
        {
            return HisLookupResult.Disabled;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, hisOptions.Value.TimeoutSeconds)));
        try
        {
            return await his.FindPatientByCitizenIdAsync(citizenId, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HisLookupResult.Unavailable;
        }
#pragma warning disable CA1031 // HIS lỗi bất kỳ cũng không được chặn việc tiếp nhận người bệnh (cho phép nhập tay).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return HisLookupResult.Unavailable;
        }
    }

    private void AddAudit(string action, Guid? patientId, object? details)
    {
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(
            clock.UtcNow,
            action,
            actor,
            nameof(Patient),
            patientId?.ToString(),
            details is null ? null : JsonSerializer.Serialize(details)));
    }
}
