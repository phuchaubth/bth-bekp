using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Domain.Auditing;
using Consent.Domain.Patients;
using Consent.Shared;
using Microsoft.Extensions.Options;

namespace Consent.Application.Patients;

/// <summary>Tra cứu nguồn thông tin người bệnh: <b>một</b> trong hai, mã người bệnh hoặc số CCCD.</summary>
public sealed record PatientSourceLookupRequest(string? PatientCode, string? CitizenId);

/// <param name="ExistingPatient">Hồ sơ đã có trong hệ thống (cùng CCCD hoặc cùng mã người bệnh), để mở thay vì tạo trùng.</param>
public sealed record PatientSourceLookupResult(PatientSourceStatus Status, ExternalPatient? Patient, PatientDetailDto? ExistingPatient);

/// <summary>
/// Lấy thông tin hành chính người bệnh từ nguồn bên ngoài (màn hình Tiếp nhận): nhân viên nhập mã người bệnh hoặc số CCCD,
/// hệ thống điền sẵn họ tên, ngày sinh, giới tính, địa chỉ… để kiểm tra rồi lưu. Lỗi nguồn không chặn việc nhập tay.
/// Ghi nhật ký tra cứu (không ghi mã hay số CCCD).
/// </summary>
public sealed class PatientSourceService(
    IPatientSource source,
    IPatientRepository patients,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IOptions<HisOptions> timeouts)
{
    public const int PatientCodeMaxLength = 50;

    public bool IsEnabled => source.IsEnabled;

    public async Task<Result<PatientSourceLookupResult>> LookupAsync(PatientSourceLookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var patientCode = string.IsNullOrWhiteSpace(request.PatientCode) ? null : request.PatientCode.Trim();
        var citizenId = string.IsNullOrWhiteSpace(request.CitizenId) ? null : request.CitizenId.Trim();
        if ((patientCode is null) == (citizenId is null))
        {
            return Error.ValidationField("patientCode", "Nhập mã người bệnh hoặc số CCCD (một trong hai).");
        }

        if (citizenId is not null && !CitizenIdNumber.HasValidFormat(citizenId))
        {
            return Error.ValidationField("citizenId", "Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.");
        }

        if (patientCode is { Length: > PatientCodeMaxLength })
        {
            return Error.ValidationField("patientCode", $"Mã người bệnh tối đa {PatientCodeMaxLength} ký tự.");
        }

        if (!source.IsEnabled)
        {
            return new PatientSourceLookupResult(PatientSourceStatus.Disabled, null, null);
        }

        var result = await FindAsync(patientCode, citizenId, cancellationToken).ConfigureAwait(false);
        var existing = await FindExistingAsync(result.Patient?.CitizenId ?? citizenId, cancellationToken).ConfigureAwait(false);

        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(
            clock.UtcNow,
            AuditActions.PatientSourceLookedUp,
            actor,
            nameof(Patient),
            existing?.Id.ToString(),
            JsonSerializer.Serialize(new { by = patientCode is null ? "citizenId" : "patientCode", status = result.Status.ToString() })));
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new PatientSourceLookupResult(result.Status, result.Patient, existing is null ? null : PatientDetailDto.From(existing));
    }

    private async Task<PatientSourceResult> FindAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeouts.Value.TimeoutSeconds)));
        try
        {
            return await source.FindAsync(patientCode, citizenId, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PatientSourceResult.Unavailable;
        }
#pragma warning disable CA1031 // Nguồn lỗi bất kỳ cũng không được chặn việc tiếp nhận người bệnh (cho phép nhập tay).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return PatientSourceResult.Unavailable;
        }
    }

    private async Task<Patient?> FindExistingAsync(string? citizenId, CancellationToken cancellationToken) =>
        citizenId is not null && CitizenIdNumber.HasValidFormat(citizenId)
            ? await patients.FindByCitizenIdAsync(citizenId, cancellationToken).ConfigureAwait(false)
            : null;
}
