using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Patients;
using Consent.Domain.Auditing;
using Consent.Domain.Common;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.Kiosk;

/// <summary>
/// Kiosk cho người bệnh tự làm phiếu (quyết định 24), dùng bằng tài khoản vai trò Kiosk (chỉ có quyền dùng kiosk):
/// <list type="number">
/// <item>Nhận diện: quét mã QR trên thẻ CCCD; hoặc nhập mã y tế/số CCCD kèm ngày sinh (phải khớp hồ sơ, sai 5 lần thì tạm
/// khóa tra cứu 5 phút); hoặc tự khai. Không hồ sơ và sai ngày sinh trả cùng một thông báo, để không dò được mã nào có
/// thật.</item>
/// <item>Người bệnh trả lời câu hỏi của các mẫu kiosk (quản trị chọn), ký một lần cho cả bộ.</item>
/// <item>Nộp: máy chủ nhận diện lại (không giữ trạng thái giữa các bước), tạo/bổ sung hồ sơ (chỉ điền ô còn trống), lập
/// bộ phiếu, trình ký và ghi chữ ký người bệnh trong một lần lưu. Phiếu chờ nhân viên đối chiếu rồi xác nhận cả bộ.</item>
/// </list>
/// Nhật ký ghi cách nhận diện và kết quả, không ghi mã y tế hay số CCCD.
/// </summary>
public sealed class KioskService(
    IUserRepository users,
    IDepartmentRepository departments,
    IConsentTemplateRepository templates,
    ISystemSettingRepository settings,
    IPatientRepository patients,
    IPatientSource patientSource,
    IConsentFormRepository forms,
    IConsentFormNumberGenerator numbers,
    IAuditLogRepository auditLogs,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser,
    IRequestContext requestContext,
    IOptions<HisOptions> sourceTimeouts,
    IValidator<PatientRequest> patientValidator)
{
    public const string PhoneNumberField = "phoneNumber";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Error NoDepartment = Error.BusinessRule(
        "kiosk.no_department", "Tài khoản kiosk chưa được gán khoa (hoặc khoa đã ngừng sử dụng). Báo quản trị hệ thống.");

    private static readonly Error NotConfigured = Error.BusinessRule(
        "kiosk.not_configured", "Kiosk chưa có phiếu nào để làm. Mời ông/bà đến quầy tiếp đón.");

    private static readonly Error SourceUnavailable = Error.BusinessRule(
        "kiosk.source_unavailable",
        "Chưa lấy được thông tin từ hệ thống bệnh viện. Ông/bà thử lại sau ít phút, hoặc chọn Quét CCCD hay Nhập thông tin.");

    private static readonly Error NeedsStaff = Error.BusinessRule(
        "kiosk.needs_staff", "Hồ sơ của ông/bà cần nhân viên kiểm tra. Mời ông/bà đến quầy tiếp đón.");

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.ToOffset(ConsentFormService.VietnamOffset).DateTime);

    /// <summary>Khoa của máy kiosk và các mẫu người bệnh tự làm (theo thứ tự quản trị chọn).</summary>
    public async Task<Result<KioskConfigDto>> GetConfigAsync(CancellationToken cancellationToken)
    {
        var department = await KioskDepartmentAsync(cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            return NoDepartment;
        }

        var prepared = await LoadTemplatesAsync(department.Id, cancellationToken).ConfigureAwait(false);
        return new KioskConfigDto(
            department.Name,
            prepared.Count > 0,
            [.. prepared.Select(p => new KioskTemplateDto(p.Template.Id, p.Template.Code, p.Template.Name, p.Definition))],
            KioskTemplates.MergeStatement(prepared.Select(p => p.Definition)));
    }

    /// <summary>Nhận diện người bệnh để hiện màn hình "Đây có phải thông tin của ông/bà?" (chưa ghi gì vào hồ sơ).</summary>
    public async Task<Result<KioskIdentityDto>> IdentifyAsync(KioskIdentityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var resolved = await ResolveAsync(request, audit: true, cancellationToken).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return resolved.Error!;
        }

        var identity = resolved.Value;
        var merged = identity.Merged;
        return new KioskIdentityDto(
            identity.Method,
            identity.FoundRecord,
            merged.FullName,
            merged.DateOfBirth,
            merged.Gender,
            SensitiveMask.CitizenId(merged.CitizenId),
            merged.PatientCode,
            identity.Method switch
            {
                KioskIdentityMethod.CitizenIdCard => Clean(request.Card?.PermanentAddress),
                KioskIdentityMethod.SelfReported => Clean(request.SelfReport?.CurrentAddress),
                _ => null,
            },
            merged.PermanentAddress is not null || merged.CurrentAddress is not null,
            merged.PhoneNumber is not null,
            merged.PhoneNumber is null ? [PhoneNumberField] : [],
            PrefilledSources(merged));
    }

    /// <summary>
    /// Nộp và ký cả bộ: kiểm tra hết (nhận diện, câu trả lời, lời xác nhận, chữ ký) rồi mới ghi, tất cả trong một lần lưu,
    /// để không có hồ sơ hay bộ phiếu dở dang.
    /// </summary>
    public async Task<Result<KioskSubmitResultDto>> SubmitAsync(KioskSubmitRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Identity is null)
        {
            return Error.ValidationField("identity", "Thiếu thông tin nhận diện người bệnh.");
        }

        var kioskUser = await KioskUserAsync(cancellationToken).ConfigureAwait(false);
        var department = await KioskDepartmentAsync(cancellationToken).ConfigureAwait(false);
        if (department is null)
        {
            return NoDepartment;
        }

        var prepared = await LoadTemplatesAsync(department.Id, cancellationToken).ConfigureAwait(false);
        if (prepared.Count == 0)
        {
            return NotConfigured;
        }

        var answers = request.Forms ?? [];
        if (answers.Count != prepared.Count || prepared.Exists(p => answers.Count(a => a.TemplateId == p.Template.Id) != 1))
        {
            return Error.ValidationField("forms", "Thiếu câu trả lời của một số phiếu; mời ông/bà làm lại từ đầu.");
        }

        var statement = string.IsNullOrWhiteSpace(request.Statement) ? null : request.Statement.Trim();
        if (statement is { Length: > ConsentSignature.StatementMaxLength })
        {
            return Error.ValidationField("statement", $"Lời xác nhận tối đa {ConsentSignature.StatementMaxLength} ký tự.");
        }

        var resolved = await ResolveAsync(request.Identity, audit: false, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return resolved.Error!;
        }

        var identity = resolved.Value;
        var phone = identity.Merged.PhoneNumber;
        if (phone is null)
        {
            phone = Clean(request.PhoneNumber);
            if (phone is null)
            {
                return Error.ValidationField(PhoneNumberField, "Vui lòng nhập số điện thoại để bệnh viện liên hệ.");
            }

            if (!IsPhone(phone))
            {
                return Error.ValidationField(PhoneNumberField, InvalidPhoneMessage);
            }
        }

        var details = await PreparePatientAsync(identity, identity.Merged with { PhoneNumber = phone }, cancellationToken).ConfigureAwait(false);
        if (details.IsFailure)
        {
            return details.Error!;
        }

        // Bản xem trước của hồ sơ (sau khi bổ sung) để tự điền phiếu; hồ sơ thật chỉ đổi khi mọi thứ đã hợp lệ.
        var preview = Patient.Create(details.Value.ToDetails(), clock.UtcNow);

        // Câu trả lời của từng mẫu: trường tự điền từ hồ sơ luôn lấy theo hồ sơ (không nhận giá trị gửi lên).
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var filled = new List<(PreparedTemplate Prepared, string DataJson, ConsentDecision? Decision)>(prepared.Count);
        foreach (var item in prepared)
        {
            var answer = answers.Single(a => a.TemplateId == item.Template.Id);
            var data = answer.Data?.DeepClone().AsObject() ?? [];
            foreach (var (key, value) in ConsentFormService.Prefill(item.Definition, preview))
            {
                data[key] = value?.DeepClone();
            }

            var evaluation = FormDataEvaluator.Evaluate(item.Definition, data);
            foreach (var (field, messages) in evaluation.Errors)
            {
                errors[$"{item.Template.Code}.{field}"] = messages;
            }

            ConsentDecision? decision = null;
            if (item.Definition.Signatures?.RequiresDecision ?? true)
            {
                if (answer.Decision is not { } chosen || !Enum.IsDefined(chosen))
                {
                    var signatures = item.Definition.Signatures;
                    errors[$"{item.Template.Code}.decision"] =
                        [$"Vui lòng chọn {signatures?.AgreeLabel ?? "Đồng ý"} hoặc {signatures?.RefuseLabel ?? "Từ chối"}."];
                    continue;
                }

                decision = chosen;
            }

            filled.Add((item, evaluation.Data.ToJsonString(), decision));
        }

        if (errors.Count > 0)
        {
            return Error.Validation("kiosk.incomplete", "Còn câu hỏi chưa trả lời; mời ông/bà kiểm tra lại.", errors);
        }

        if (statement is null && KioskTemplates.MergeStatement(prepared.Select(p => p.Definition)) is { Required: true } required)
        {
            var example = required.Suggestions is { Count: > 0 } suggestions ? $", ví dụ: “{suggestions[0]}”" : string.Empty;
            return Error.ValidationField("statement", $"Vui lòng chọn hoặc ghi lời xác nhận{example}.");
        }

        var signaturePng = ConsentSigningService.NormalizeSignature(request.Image);
        if (signaturePng.IsFailure)
        {
            return signaturePng.Error!;
        }

        var now = clock.UtcNow;
        var patient = SavePatient(identity, details.Value, preview, now);

        var bundleId = filled.Count >= 2 ? Guid.CreateVersion7() : (Guid?)null;
        var sessionId = Guid.NewGuid();
        var snapshot = JsonSerializer.Serialize(
            new ConsentFormSnapshot(ConsentFormService.ToPatientInfo(patient), null), TemplateJson.Options);
        var created = new List<ConsentForm>(filled.Count);
        foreach (var (item, dataJson, decision) in filled)
        {
            var formNo = await numbers.NextAsync(now, cancellationToken).ConfigureAwait(false);
            var form = ConsentForm.Create(
                formNo, patient.Id, item.Template.Id, item.Version.Id, department.Id, dataJson, kioskUser.Id, now,
                bundleId: bundleId, kioskIdentity: identity.Method);
            form.Submit(dataJson, snapshot, now);
            form.StartSigning(sessionId, now);
            form.SignByPatient(
                sessionId,
                decision,
                patient.FullName,
                signaturePng.Value,
                requestContext.UserAgent,
                kioskUser.Id,
                now,
                item.Definition.Signatures?.Statement is not null ? statement : null);
            forms.Add(form);
            created.Add(form);
        }

        List<string> formNos = [.. created.Select(f => f.FormNo)];
        foreach (var form in created)
        {
            AddAudit(
                AuditActions.ConsentFormPatientSigned,
                nameof(ConsentForm),
                form.Id.ToString(),
                new { decision = form.Decision?.ToString(), signer = "Patient", bundle = bundleId, kiosk = identity.Method.ToString(), signedTogether = formNos });
        }

        AddAudit(
            AuditActions.KioskSubmitted,
            nameof(Patient),
            patient.Id.ToString(),
            new { method = identity.Method.ToString(), forms = created.Count, bundle = bundleId });
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new KioskSubmitResultDto(
            patient.FullName,
            patient.PatientCode,
            bundleId,
            [.. created.Zip(filled, (form, item) => new KioskSubmittedFormDto(form.Id, form.FormNo, item.Prepared.Template.Name, form.Decision))],
            now);
    }

    private sealed record PreparedTemplate(ConsentTemplate Template, ConsentTemplateVersion Version, TemplateDefinition Definition);

    /// <summary>Người bệnh đã nhận diện: hồ sơ có sẵn (nếu có), thông tin từ hệ thống bệnh viện, và thông tin gộp.</summary>
    private sealed record ResolvedIdentity(
        KioskIdentityMethod Method, Patient? Local, ExternalPatient? Source, PatientRequest Merged, bool FoundRecord);

    private async Task<User> KioskUserAsync(CancellationToken cancellationToken) =>
        await users.GetByIdAsync(currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng."), cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException("Không tìm thấy tài khoản kiosk.");

    private async Task<Department?> KioskDepartmentAsync(CancellationToken cancellationToken)
    {
        var user = await KioskUserAsync(cancellationToken).ConfigureAwait(false);
        var department = user.DepartmentId is { } id ? await departments.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
        return department is { IsActive: true } ? department : null;
    }

    /// <summary>Mẫu kiosk dùng được ở khoa này, theo thứ tự cấu hình; mẫu đã ngừng hay không hợp lệ thì bỏ qua.</summary>
    private async Task<List<PreparedTemplate>> LoadTemplatesAsync(Guid departmentId, CancellationToken cancellationToken)
    {
        var codes = await KioskTemplates.ReadCodesAsync(settings, cancellationToken).ConfigureAwait(false);
        if (codes.Count == 0)
        {
            return [];
        }

        var all = await templates.ListAsync(includeInactive: false, cancellationToken).ConfigureAwait(false);
        var result = new List<PreparedTemplate>(codes.Count);
        foreach (var code in codes)
        {
            if (all.FirstOrDefault(t => t.Code == code) is { } template
                && template.PublishedVersion is { } version
                && (template.DepartmentId is null || template.DepartmentId == departmentId)
                && KioskTemplates.Problems(template).Count == 0)
            {
                result.Add(new PreparedTemplate(template, version, ConsentTemplateService.ReadDefinition(version)));
            }
        }

        return result;
    }

    /// <summary>
    /// Nhận diện (dùng chung cho xem trước và nộp): kiểm tra khóa tra cứu, tìm hồ sơ (phần mềm này, rồi hệ thống bệnh
    /// viện), đối chiếu ngày sinh. Lần sai được tính vào khóa tra cứu, kể cả khi nộp. Người gọi lưu thay đổi.
    /// </summary>
    private async Task<Result<ResolvedIdentity>> ResolveAsync(KioskIdentityRequest request, bool audit, CancellationToken cancellationToken)
    {
        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var kioskUser = await KioskUserAsync(cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        var dateOfBirth = request.DateOfBirth!.Value;
        var citizenId = Clean(request.CitizenId);
        var patientCode = Clean(request.PatientCode);
        var lookupByCode = request.Method == KioskIdentityMethod.LookupCode;

        // Đang khóa thì chặn mọi cách có tra hồ sơ; tự khai không kèm số CCCD (không tra ai) vẫn làm được.
        if ((citizenId is not null || patientCode is not null) && kioskUser.IsKioskLookupLocked(now))
        {
            return Locked(kioskUser.KioskLookupLockedUntilUtc!.Value);
        }

        Patient? local = null;
        PatientSourceResult source = PatientSourceResult.NotFound;
        if (citizenId is not null || patientCode is not null)
        {
            local = patientCode is not null
                ? await patients.FindByPatientCodeAsync(patientCode, cancellationToken).ConfigureAwait(false)
                : await patients.FindByCitizenIdAsync(citizenId!, cancellationToken).ConfigureAwait(false);
            source = await FindInSourceAsync(patientCode, patientCode is null ? citizenId : null, cancellationToken).ConfigureAwait(false);

            // Hồ sơ có sẵn nhưng tìm theo khóa khác (ví dụ nhập mã y tế, hồ sơ có sẵn lưu theo số CCCD trên hệ thống bệnh viện).
            if (local is null && source.Patient is { } found)
            {
                local = CleanCitizenId(found.CitizenId) is { } sourceCitizenId
                    ? await patients.FindByCitizenIdAsync(sourceCitizenId, cancellationToken).ConfigureAwait(false)
                    : null;
                if (local is null && CleanPatientCode(found.PatientCode) is { } sourceCode)
                {
                    local = await patients.FindByPatientCodeAsync(sourceCode, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        var external = source.Status == PatientSourceStatus.Found ? source.Patient : null;
        var foundRecord = local is not null || external is not null;
        if (lookupByCode && !foundRecord)
        {
            // Hệ thống bệnh viện không trả lời được: không phải lỗi của người bệnh, không tính lần sai.
            return source.Status == PatientSourceStatus.Unavailable ? SourceUnavailable : RegisterFailure(kioskUser, request.Method, now);
        }

        // Ngày sinh đối chiếu: theo hồ sơ có sẵn, không có thì theo hệ thống bệnh viện. Nhập mã mà hồ sơ không có ngày sinh
        // thì không đối chiếu được, coi như không khớp.
        var expected = local?.DateOfBirth ?? external?.DateOfBirth;
        if (foundRecord && (expected is null ? lookupByCode : expected != dateOfBirth))
        {
            return RegisterFailure(kioskUser, request.Method, now);
        }

        // Chưa có hồ sơ ở phần mềm này mà hệ thống bệnh viện thiếu họ tên/giới tính: không lập được hồ sơ từ mã y tế.
        if (lookupByCode && local is null && (Text(external?.FullName, Patient.FullNameMaxLength) is null || external?.Gender is null))
        {
            return NeedsStaff;
        }

        if ((citizenId is not null || patientCode is not null)
            && (kioskUser.KioskLookupFailures > 0 || kioskUser.KioskLookupLockedUntilUtc is not null))
        {
            kioskUser.ResetKioskLookupFailures();
        }

        if (audit)
        {
            AddAudit(
                AuditActions.KioskIdentified,
                nameof(Patient),
                local?.Id.ToString(),
                new { method = request.Method.ToString(), found = foundRecord, source = source.Status.ToString() });
        }

        return new ResolvedIdentity(request.Method, local, external, Merge(local, external, request, dateOfBirth), foundRecord);
    }

    private Error? Validate(KioskIdentityRequest request)
    {
        if (!Enum.IsDefined(request.Method))
        {
            return Error.ValidationField("method", "Cách nhận diện không hợp lệ.");
        }

        if (request.DateOfBirth is not { } dateOfBirth || dateOfBirth < PatientRules.MinimumDate || dateOfBirth > Today)
        {
            return Error.ValidationField("dateOfBirth", "Ngày sinh không hợp lệ.");
        }

        var citizenId = Clean(request.CitizenId);
        if (citizenId is not null && !CitizenIdNumber.HasValidFormat(citizenId))
        {
            return Error.ValidationField("citizenId", "Số CCCD phải gồm 12 chữ số.");
        }

        switch (request.Method)
        {
            case KioskIdentityMethod.CitizenIdCard:
                if (citizenId is null || request.Card is not { } card || Clean(card.FullName) is null
                    || card.Gender is not { } cardGender || !Enum.IsDefined(cardGender))
                {
                    return Error.ValidationField("card", "Không đọc được đủ thông tin trên thẻ. Mời ông/bà quét lại.");
                }

                return CitizenIdNumber.CheckConsistency(citizenId, dateOfBirth, cardGender) is null
                    ? null
                    : Error.ValidationField("card", "Thông tin đọc từ thẻ không khớp nhau. Mời ông/bà quét lại hoặc đến quầy tiếp đón.");

            case KioskIdentityMethod.LookupCode:
                var patientCode = Clean(request.PatientCode);
                if ((patientCode is null) == (citizenId is null))
                {
                    return Error.ValidationField("patientCode", "Nhập mã y tế hoặc số CCCD (một trong hai).");
                }

                return patientCode is null || CleanPatientCode(patientCode) is not null
                    ? null
                    : Error.ValidationField("patientCode", "Mã y tế không hợp lệ.");

            default:
                if (request.SelfReport is not { } self || Clean(self.FullName) is null
                    || self.Gender is not { } selfGender || !Enum.IsDefined(selfGender))
                {
                    return Error.ValidationField("selfReport", "Vui lòng nhập họ tên và chọn giới tính.");
                }

                if (Clean(self.FullName)!.Length > Patient.FullNameMaxLength)
                {
                    return Error.ValidationField("fullName", $"Họ tên tối đa {Patient.FullNameMaxLength} ký tự.");
                }

                if (Clean(self.PhoneNumber) is not { } selfPhone)
                {
                    return Error.ValidationField(PhoneNumberField, "Vui lòng nhập số điện thoại.");
                }

                if (!IsPhone(selfPhone))
                {
                    return Error.ValidationField(PhoneNumberField, InvalidPhoneMessage);
                }

                if (Clean(self.CurrentAddress) is not { } selfAddress)
                {
                    return Error.ValidationField("currentAddress", "Vui lòng nhập chỗ ở hiện nay.");
                }

                if (selfAddress.Length > Patient.AddressMaxLength)
                {
                    return Error.ValidationField("currentAddress", $"Địa chỉ tối đa {Patient.AddressMaxLength} ký tự.");
                }

                return citizenId is not null && CitizenIdNumber.CheckConsistency(citizenId, dateOfBirth, selfGender) is { } mismatch
                    ? Error.ValidationField("citizenId", mismatch)
                    : null;
        }
    }

    /// <summary>Ghi một lần tra cứu sai; vừa đủ số lần thì tạm khóa tra cứu. Thông báo như nhau dù không có hồ sơ hay sai ngày sinh.</summary>
    private Error RegisterFailure(User kioskUser, KioskIdentityMethod method, DateTimeOffset now)
    {
        var locked = kioskUser.RegisterKioskLookupFailure(now, KioskLookupPolicy.Default);
        AddAudit(AuditActions.KioskLookupFailed, nameof(Patient), null, new { method = method.ToString() });
        if (locked)
        {
            AddAudit(AuditActions.KioskLookupLocked, nameof(User), kioskUser.Id.ToString(), new { until = kioskUser.KioskLookupLockedUntilUtc });
            return Locked(kioskUser.KioskLookupLockedUntilUtc!.Value);
        }

        return Error.BusinessRule(
                "kiosk.lookup_failed",
                "Không tìm thấy hồ sơ khớp với mã và ngày sinh đã nhập. Ông/bà kiểm tra lại, hoặc chọn cách khác.")
            with
            {
                Metadata = new Dictionary<string, object?>
                {
                    ["remainingAttempts"] = KioskLookupPolicy.MaxFailedAttempts - kioskUser.KioskLookupFailures,
                },
            };
    }

    private static Error Locked(DateTimeOffset until) =>
        Error.BusinessRule(
                "kiosk.lookup_locked",
                $"Máy tạm khóa tra cứu {KioskLookupPolicy.LockoutDuration.TotalMinutes:0} phút do nhập sai nhiều lần. Mời ông/bà đến quầy tiếp đón hoặc thử lại sau.")
            with
            {
                Metadata = new Dictionary<string, object?> { ["lockedUntilUtc"] = until },
            };

    private async Task<PatientSourceResult> FindInSourceAsync(string? patientCode, string? citizenId, CancellationToken cancellationToken)
    {
        if (!patientSource.IsEnabled)
        {
            return PatientSourceResult.Disabled;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, sourceTimeouts.Value.TimeoutSeconds)));
        try
        {
            return await patientSource.FindAsync(patientCode, citizenId, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PatientSourceResult.Unavailable;
        }
#pragma warning disable CA1031 // Nguồn lỗi bất kỳ cũng không được chặn người bệnh (vẫn quét thẻ hoặc tự khai được).
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return PatientSourceResult.Unavailable;
        }
    }

    /// <summary>
    /// Thông tin gộp. Có hồ sơ sẵn: giữ nguyên mọi giá trị đã có (nhân viên đã kiểm tra), chỉ điền ô trống bằng hệ thống
    /// bệnh viện rồi tới thẻ/tự khai; không đổi số CCCD hay mã y tế của hồ sơ có sẵn. Chưa có hồ sơ: theo hệ thống bệnh
    /// viện (nếu có), thiếu đâu lấy từ thẻ/tự khai. Dữ liệu ngoài không đúng định dạng thì bỏ qua.
    /// </summary>
    private PatientRequest Merge(Patient? local, ExternalPatient? source, KioskIdentityRequest request, DateOnly dateOfBirth)
    {
        var card = request.Method == KioskIdentityMethod.CitizenIdCard ? request.Card : null;
        var self = request.Method == KioskIdentityMethod.SelfReported ? request.SelfReport : null;
        var phone = Pick(local?.PhoneNumber, CleanPhone(source?.PhoneNumber), Clean(self?.PhoneNumber));
        var permanentAddress = Pick(local?.PermanentAddress, Text(source?.PermanentAddress, Patient.AddressMaxLength), Text(card?.PermanentAddress, Patient.AddressMaxLength));
        var currentAddress = Pick(local?.CurrentAddress, Text(source?.CurrentAddress, Patient.AddressMaxLength), Text(self?.CurrentAddress, Patient.AddressMaxLength));
        var ethnicity = Pick(local?.Ethnicity, Text(source?.Ethnicity, Patient.EthnicityMaxLength));
        var occupation = Pick(local?.Occupation, Text(source?.Occupation, Patient.OccupationMaxLength));
        var nationality = Pick(local?.Nationality, Text(source?.Nationality, Patient.NationalityMaxLength));
        var insurance = Pick(local?.HealthInsuranceNumber, CleanInsurance(source?.HealthInsuranceNumber));
        var oldId = Pick(local?.OldIdNumber, CleanOldId(source?.OldIdNumber), CleanOldId(card?.OldIdNumber));

        if (local is not null)
        {
            // Mã y tế: chỉ bổ sung khi hồ sơ chưa có và hệ thống bệnh viện trả về đúng người (cùng số CCCD) — việc kiểm tra
            // trùng mã với hồ sơ khác làm khi lưu.
            var patientCode = local.PatientCode
                ?? (local.CitizenId is not null && CleanCitizenId(source?.CitizenId) == local.CitizenId ? CleanPatientCode(source?.PatientCode) : null);
            var issueDate = local.CitizenIdIssueDate ?? ValidIssueDate(source?.CitizenIdIssueDate ?? card?.IssueDate, local.DateOfBirth);
            return new PatientRequest(
                local.FullName, local.DateOfBirth, local.Gender, local.CitizenId, oldId, issueDate, permanentAddress, currentAddress,
                phone, ethnicity, occupation, nationality, insurance, patientCode);
        }

        var fullName = Pick(Text(source?.FullName, Patient.FullNameMaxLength), Text(card?.FullName, Patient.FullNameMaxLength), Text(self?.FullName, Patient.FullNameMaxLength))
            ?? throw new InvalidOperationException("Thiếu họ tên người bệnh.");
        var birth = source?.DateOfBirth ?? dateOfBirth;
        var gender = source?.Gender ?? card?.Gender ?? self?.Gender ?? throw new InvalidOperationException("Thiếu giới tính người bệnh.");
        return new PatientRequest(
            fullName,
            birth,
            gender,
            CleanCitizenId(source?.CitizenId) ?? Clean(request.CitizenId),
            oldId,
            ValidIssueDate(source?.CitizenIdIssueDate ?? card?.IssueDate, birth),
            permanentAddress,
            currentAddress,
            phone,
            ethnicity,
            occupation,
            nationality,
            insurance,
            CleanPatientCode(source?.PatientCode));
    }

    /// <summary>
    /// Thông tin hồ sơ sẽ lưu: có sẵn thì chỉ bổ sung ô trống; chưa có thì hồ sơ mới. Không đổi gì ở đây — người gọi lưu
    /// (<see cref="SavePatient"/>) sau khi mọi thứ khác đã hợp lệ.
    /// </summary>
    private async Task<Result<PatientRequest>> PreparePatientAsync(
        ResolvedIdentity identity, PatientRequest details, CancellationToken cancellationToken)
    {
        // Mã y tế từ hệ thống bệnh viện đã thuộc hồ sơ khác: không bổ sung vào hồ sơ có sẵn (nhân viên xử lý trùng sau);
        // hồ sơ mới mà trùng mã hay trùng số CCCD thì không tự gộp, mời đến quầy.
        if (details.PatientCode is { } code && identity.Local?.PatientCode != code
            && await patients.PatientCodeExistsAsync(code, identity.Local?.Id, cancellationToken).ConfigureAwait(false))
        {
            if (identity.Local is null)
            {
                return NeedsStaff;
            }

            details = details with { PatientCode = identity.Local.PatientCode };
        }

        if (identity.Local is null && details.CitizenId is { } citizenId
            && await patients.FindByCitizenIdAsync(citizenId, cancellationToken).ConfigureAwait(false) is not null)
        {
            return NeedsStaff;
        }

        // Thông tin người bệnh tự nhập đã được kiểm tra trước; còn sai thì do dữ liệu hồ sơ/hệ thống bệnh viện, cần nhân viên.
        return await patientValidator.ValidateToErrorAsync(details, cancellationToken).ConfigureAwait(false) is null
            ? details
            : NeedsStaff;
    }

    /// <summary>
    /// Lưu hồ sơ: hồ sơ có sẵn được bổ sung ô trống (không đổi nếu không có gì mới), hồ sơ mới được thêm. Nhận mã y tế từ
    /// hệ thống bệnh viện thì đánh dấu đã đối chiếu, như màn hình tiếp nhận.
    /// </summary>
    private Patient SavePatient(ResolvedIdentity identity, PatientRequest details, Patient preview, DateTimeOffset now)
    {
        var patient = identity.Local ?? preview;
        if (identity.Local is null)
        {
            patients.Add(patient);
        }
        else if (Differs(patient, details))
        {
            patient.Update(details.ToDetails(), now);
        }

        if (identity.Source?.PatientCode is not null && patient.PatientCode is { } patientCode && patient.HisVerifiedAtUtc is null
            && string.Equals(CleanPatientCode(identity.Source.PatientCode), patientCode, StringComparison.OrdinalIgnoreCase))
        {
            patient.MarkHisVerified(patientCode, now);
        }

        return patient;
    }

    private static bool Differs(Patient patient, PatientRequest details) =>
        patient.PatientCode != details.PatientCode
        || patient.OldIdNumber != details.OldIdNumber
        || patient.CitizenIdIssueDate != details.CitizenIdIssueDate
        || patient.PermanentAddress != details.PermanentAddress
        || patient.CurrentAddress != details.CurrentAddress
        || patient.PhoneNumber != details.PhoneNumber
        || patient.Ethnicity != details.Ethnicity
        || patient.Occupation != details.Occupation
        || patient.Nationality != details.Nationality
        || patient.HealthInsuranceNumber != details.HealthInsuranceNumber;

    /// <summary>Nguồn tự điền đã có giá trị (giống <see cref="ConsentFormService.Prefill"/>): câu hỏi tương ứng không cần hỏi.</summary>
    private static List<string> PrefilledSources(PatientRequest merged)
    {
        var sources = new List<string>();
        Add(PrefillSources.HealthInsuranceNumber, merged.HealthInsuranceNumber);
        Add(PrefillSources.PhoneNumber, merged.PhoneNumber);
        Add(PrefillSources.PatientCode, merged.PatientCode);
        Add(PrefillSources.PermanentAddress, merged.PermanentAddress);
        Add(PrefillSources.CurrentAddress, merged.CurrentAddress ?? merged.PermanentAddress);
        Add(PrefillSources.Ethnicity, merged.Ethnicity);
        Add(PrefillSources.Occupation, merged.Occupation);
        return sources;

        void Add(string source, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                sources.Add(source);
            }
        }
    }

    private void AddAudit(string action, string entityType, string? entityId, object details)
    {
        var actor = new AuditActor(
            currentUser.UserId, currentUser.UserName, requestContext.IpAddress, requestContext.UserAgent, requestContext.CorrelationId);
        auditLogs.Add(AuditLog.Create(clock.UtcNow, action, actor, entityType, entityId, JsonSerializer.Serialize(details)));
    }

    private DateOnly? ValidIssueDate(DateOnly? issued, DateOnly dateOfBirth) =>
        issued is { } value && value >= dateOfBirth && value <= Today ? value : null;

    private const string InvalidPhoneMessage = "Số điện thoại không hợp lệ (ví dụ 0912345678).";

    private static bool IsPhone(string value) => Regex.IsMatch(value, PatientRules.PhonePattern, RegexOptions.None, RegexTimeout);

    private static string? Pick(params string?[] values) => values.FirstOrDefault(v => v is not null);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Text(string? value, int maxLength) => Clean(value) is { } text && text.Length <= maxLength ? text : null;

    private static string? CleanCitizenId(string? value) => Clean(value) is { } text && CitizenIdNumber.HasValidFormat(text) ? text : null;

    private static string? CleanPatientCode(string? value) =>
        Clean(value) is { Length: <= Patient.PatientCodeMaxLength } text && Regex.IsMatch(text, "^[A-Za-z0-9_-]+$", RegexOptions.None, RegexTimeout)
            ? text
            : null;

    private static string? CleanOldId(string? value) =>
        Clean(value) is { } text && Regex.IsMatch(text, "^([0-9]{9}|[0-9]{12})$", RegexOptions.None, RegexTimeout) ? text : null;

    private static string? CleanInsurance(string? value) =>
        Clean(value)?.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant() is { } text && HealthInsuranceNumber.IsValid(text)
            ? text
            : null;

    /// <summary>Số điện thoại từ hệ thống bệnh viện: bỏ khoảng trắng, dấu chấm, gạch; vẫn sai định dạng thì bỏ qua.</summary>
    private static string? CleanPhone(string? value)
    {
        if (Clean(value) is not { } text)
        {
            return null;
        }

        var compact = new string([.. text.Where(c => char.IsAsciiDigit(c) || c == '+')]);
        return IsPhone(compact) ? compact : null;
    }
}
