using Consent.Application.Abstractions;
using Consent.Application.Patients;
using Consent.Domain.ConsentForms;
using Consent.Shared;
using Microsoft.Extensions.Options;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Gắn phiếu với lượt tiếp nhận trên HIS (quyết định 25): khi bệnh viện bật, người ký hoàn tất phiếu (tiếp nhận, bác sĩ…)
/// chọn một lượt tiếp nhận của người bệnh — mặc định các lượt trong ngày, mới nhất trước, hoặc từ ngày đến ngày — kèm các
/// phiếu đã gắn vào từng lượt. Lượt chọn được máy chủ tra lại HIS (đúng người bệnh, đúng ngày) rồi mới gắn.
/// </summary>
public sealed class ConsentEncounterService(
    ConsentFormService formService,
    IConsentFormRepository forms,
    IPatientRepository patients,
    IEncounterSource source,
    IClock clock,
    IOptions<HisOptions> timeouts)
{
    /// <summary>Khoảng ngày dài nhất được tra (bằng giới hạn của API thông tin người bệnh).</summary>
    public const int MaxDays = 366;

    private static readonly Error NotFound = Error.NotFound("consent_forms.not_found", "Không tìm thấy phiếu.");

    private static readonly Error BundleNotFound = Error.NotFound("consent_forms.bundle_not_found", "Không tìm thấy bộ phiếu.");

    private static readonly Error Disabled = Error.BusinessRule(
        "consent_forms.encounters_disabled", "Bệnh viện chưa bật gắn phiếu với lượt tiếp nhận.");

    private static readonly Error Unavailable = Error.BusinessRule(
        "consent_forms.encounters_unavailable", "Không lấy được danh sách tiếp nhận từ HIS. Vui lòng thử lại sau ít phút.");

    private static readonly Error NoPatientKey = Error.BusinessRule(
        "consent_forms.encounters_no_patient_key",
        "Hồ sơ người bệnh chưa có mã y tế hay số CCCD nên chưa tìm được lượt tiếp nhận. Hãy bổ sung hồ sơ rồi thử lại.");

    public bool IsEnabled => source.IsEnabled;

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    public async Task<Result<EncounterListDto>> ListForFormAsync(Guid formId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var form = await formService.LoadInScopeAsync(formId, cancellationToken).ConfigureAwait(false);
        return form is null ? NotFound : await ListAsync(form.PatientId, from, to, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<EncounterListDto>> ListForBundleAsync(Guid bundleId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        var bundle = await formService.LoadBundleInScopeAsync(bundleId, cancellationToken).ConfigureAwait(false);
        return bundle.Count == 0 ? BundleNotFound : await ListAsync(bundle[0].PatientId, from, to, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lượt tiếp nhận gắn khi hoàn tất: chưa bật thì không gắn (null); đã bật thì bắt buộc chọn, và lượt phải có trên HIS
    /// trong ngày đã chọn của đúng người bệnh.
    /// </summary>
    internal async Task<Result<ConsentEncounter?>> ResolveAsync(
        Guid patientId, string? encounterNo, DateOnly? encounterDate, CancellationToken cancellationToken)
    {
        if (!source.IsEnabled)
        {
            return (ConsentEncounter?)null;
        }

        var number = encounterNo?.Trim();
        if (string.IsNullOrEmpty(number) || encounterDate is not { } date)
        {
            return Error.ValidationField("encounterNo", "Vui lòng chọn lượt tiếp nhận của người bệnh mà phiếu thuộc về.");
        }

        var found = await FindAsync(patientId, date, date, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var match = found.Value.FirstOrDefault(e => string.Equals(e.EncounterNo, number, StringComparison.OrdinalIgnoreCase));
        return match is null
            ? Error.ValidationField("encounterNo", "Lượt tiếp nhận đã chọn không còn trên HIS hoặc không thuộc người bệnh này; hãy tải lại danh sách.")
            : new ConsentEncounter(match.EncounterNo, ToHospitalTime(match.ReceivedAt), match.Place, match.ReceivedBy);
    }

    private async Task<Result<EncounterListDto>> ListAsync(Guid patientId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken)
    {
        if (!source.IsEnabled)
        {
            return Disabled;
        }

        var start = from ?? Today;
        var end = to ?? start;
        if (end < start || end.DayNumber - start.DayNumber >= MaxDays)
        {
            return Error.ValidationField("from", $"Khoảng ngày không hợp lệ (từ ngày không sau đến ngày, tối đa {MaxDays} ngày).");
        }

        var found = await FindAsync(patientId, start, end, cancellationToken).ConfigureAwait(false);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var numbers = found.Value.Select(e => e.EncounterNo).ToList();
        var attached = await forms.ListByEncountersAsync(patientId, numbers, cancellationToken).ConfigureAwait(false);

        List<EncounterOptionDto> items =
        [
            .. found.Value
                .OrderByDescending(e => e.ReceivedAt)
                .Select(e => new EncounterOptionDto(
                    e.EncounterNo,
                    ToHospitalTime(e.ReceivedAt),
                    e.Place,
                    e.ReceivedBy,
                    [.. attached
                        .Where(f => string.Equals(f.EncounterNo, e.EncounterNo, StringComparison.OrdinalIgnoreCase))
                        .Select(f => new EncounterFormDto(f.Id, f.FormNo, f.TemplateName, f.Status))])),
        ];
        return new EncounterListDto(start, end, items);
    }

    /// <summary>Tra HIS theo mã y tế (ưu tiên) hoặc số CCCD trong hồ sơ người bệnh; lỗi hay quá thời gian thì báo không lấy được.</summary>
    private async Task<Result<IReadOnlyList<ExternalEncounter>>> FindAsync(
        Guid patientId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var patient = await patients.GetByIdAsync(patientId, cancellationToken).ConfigureAwait(false);
        if (patient is null || (patient.PatientCode is null && patient.CitizenId is null))
        {
            return NoPatientKey;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, timeouts.Value.TimeoutSeconds)));
        EncounterSourceResult result;
        try
        {
            result = await source.FindAsync(
                patient.PatientCode, patient.PatientCode is null ? patient.CitizenId : null, from, to, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable;
        }
#pragma warning disable CA1031 // Nguồn lỗi bất kỳ chỉ báo "không lấy được", không làm hỏng màn hình xác nhận.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return Unavailable;
        }

        if (result.Status != EncounterSourceStatus.Found)
        {
            return Unavailable;
        }

        // Số tiếp nhận quá dài (dữ liệu HIS bất thường) không gắn được vào phiếu: không đưa lên danh sách để chọn.
        List<ExternalEncounter> usable =
        [
            .. result.Encounters.Where(e => e.EncounterNo.Trim() is { Length: > 0 and <= ConsentEncounter.NumberMaxLength }),
        ];
        return usable;
    }

    /// <summary>Giờ HIS là giờ bệnh viện (UTC+7, không có múi giờ trong CSDL).</summary>
    internal static DateTimeOffset ToHospitalTime(DateTime local) =>
        new(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), ConsentFormService.VietnamOffset);
}
