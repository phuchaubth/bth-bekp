using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Consent.Application.Abstractions;
using Consent.Domain.Auditing;
using Consent.Domain.Common;
using Consent.Domain.ConsentForms;
using Consent.Shared;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Ký trên điện thoại riêng của người bệnh qua mã QR (quyết định 32):
/// <list type="number">
/// <item>Nhân viên tạo mã cho một phiếu hoặc cả bộ phiếu đang chờ người bệnh ký: mở phiên ký mới (phiên cũ hết hiệu lực) và
/// hiện mã QR chứa đường dẫn kèm mã ngẫu nhiên 128 bit. Máy chủ chỉ lưu mã băm.</item>
/// <item>Người bệnh quét mã bằng điện thoại của mình, nhập ngày sinh của người bệnh. Đúng thì điện thoại nhận token phiên ký
/// (chỉ ký được đúng các phiếu này) và nội dung phiếu để đọc; sai 5 lần thì mã bị hủy. Mã mở được trên đúng một điện thoại.</item>
/// <item>Người bệnh ký như khi ký trên máy của bệnh viện (cùng API ký, cùng kiểm tra); nhân viên theo dõi trạng thái trên màn hình
/// rồi xác nhận phiếu như thường lệ.</item>
/// </list>
/// Mọi bước đều ghi nhật ký; lần mở mã ghi địa chỉ IP và trình duyệt của điện thoại.
/// </summary>
public sealed class RemoteSigningService(
    ConsentFormService formService,
    IConsentFormRepository forms,
    IRemoteSigningLinkRepository links,
    IUserRepository users,
    ITokenService tokens,
    IUnitOfWork unitOfWork,
    IClock clock,
    ICurrentUser currentUser)
{
    /// <summary>Số byte ngẫu nhiên của mã trong QR (128 bit) và của khóa thiết bị (256 bit).</summary>
    private const int CodeBytes = 16;

    private const int DeviceKeyBytes = 32;

    private static readonly Error NotFound = Error.NotFound("consent_forms.not_found", "Không tìm thấy phiếu.");

    private static readonly Error BundleNotFound = Error.NotFound("consent_forms.bundle_not_found", "Không tìm thấy bộ phiếu.");

    private static readonly Error LinkNotFound = Error.NotFound("remote_signing.not_found", "Không tìm thấy mã ký.");

    /// <summary>
    /// Mã không đúng, hết hạn, đã hủy hay đã có phiên ký khác: cùng một câu trả lời, để người cầm mã không dò được trạng thái.
    /// </summary>
    private static readonly Error InvalidCode = Error.NotFound(
        "remote_signing.invalid", "Mã ký không còn dùng được (đã hết hạn, đã hủy hoặc không đúng). Ông/bà nhờ nhân viên tạo mã mới.");

    /// <summary>Tạo mã ký cho một phiếu đang chờ người bệnh ký.</summary>
    public async Task<Result<RemoteSigningLinkDto>> CreateForFormAsync(Guid formId, CancellationToken cancellationToken)
    {
        var form = await formService.LoadInScopeAsync(formId, cancellationToken).ConfigureAwait(false);
        if (form is null)
        {
            return NotFound;
        }

        if (!form.CanStartSigning)
        {
            return Error.Conflict(
                "consent_forms.invalid_status",
                form.Status == ConsentFormStatus.PendingSignature ? "Người bệnh đã ký phiếu này." : "Chỉ tạo mã ký được cho phiếu đang chờ ký.");
        }

        return await CreateAsync([form], bundleId: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Tạo một mã ký cho mọi phiếu của bộ đang chờ người bệnh ký (người bệnh ký một lần cho cả bộ).</summary>
    public async Task<Result<RemoteSigningLinkDto>> CreateForBundleAsync(Guid bundleId, CancellationToken cancellationToken)
    {
        var bundle = await formService.LoadBundleInScopeAsync(bundleId, cancellationToken).ConfigureAwait(false);
        if (bundle.Count == 0)
        {
            return BundleNotFound;
        }

        var signable = bundle.Where(f => f.CanStartSigning).ToList();
        if (signable.Count == 0)
        {
            return Error.Conflict(
                "consent_forms.invalid_status", "Không có phiếu nào trong bộ đang chờ người bệnh ký; hãy trình ký các phiếu trước.");
        }

        if (ConsentFormService.HasMixedSigners(signable))
        {
            return Error.BusinessRule("consent_forms.bundle_mixed_signers", ConsentFormService.MixedSignersMessage);
        }

        return await CreateAsync(signable, bundleId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Trạng thái mã ký, để màn hình nhân viên biết người bệnh đã mở, đã ký chưa.</summary>
    public async Task<Result<RemoteSigningStatusDto>> GetStatusAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var (link, linkForms) = await LoadInScopeAsync(linkId, cancellationToken).ConfigureAwait(false);
        return link is null ? LinkNotFound : Status(link, linkForms, clock.UtcNow);
    }

    /// <summary>
    /// Nhân viên hủy mã: điện thoại đã mở mã cũng không ký được nữa (phiên ký của mã được đóng).
    /// </summary>
    public async Task<Result<RemoteSigningStatusDto>> CancelAsync(Guid linkId, CancellationToken cancellationToken)
    {
        var (link, linkForms) = await LoadInScopeAsync(linkId, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            return LinkNotFound;
        }

        var now = clock.UtcNow;
        if (!link.IsCancelled)
        {
            link.Cancel(RemoteSigningCancelReason.Staff, now);
            foreach (var form in linkForms)
            {
                form.EndSigning(link.SessionId, now);
                formService.AddAudit(
                    AuditActions.ConsentFormRemoteSigningCancelled, form.Id, JsonSerializer.Serialize(new { link = link.Id, reason = "staff" }));
            }

            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Status(link, linkForms, now);
    }

    /// <summary>
    /// Điện thoại người bệnh mở mã (không cần đăng nhập). Lần đầu: kiểm tra ngày sinh của người bệnh; đúng thì gắn mã với điện
    /// thoại này bằng khóa thiết bị. Tải lại trang: dùng khóa thiết bị, không phải nhập lại ngày sinh. Trả token phiên ký và nội
    /// dung các phiếu (số giấy tờ, điện thoại đã che bớt).
    /// </summary>
    public async Task<Result<RemoteSigningSessionDto>> ClaimAsync(ClaimRemoteSigningRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsWellFormed(request.Code, CodeBytes))
        {
            return InvalidCode;
        }

        var link = await links.FindByCodeHashAsync(Hash(request.Code!), cancellationToken).ConfigureAwait(false);
        var now = clock.UtcNow;
        if (link is null)
        {
            return InvalidCode;
        }

        var resuming = link.IsOpened;
        if (resuming)
        {
            if (!IsWellFormed(request.DeviceKey, DeviceKeyBytes) || !SameHash(Hash(request.DeviceKey!), link.DeviceKeyHash))
            {
                return Error.Conflict(
                    "remote_signing.opened_elsewhere",
                    "Mã ký này đã được mở trên một điện thoại khác. Ông/bà nhờ nhân viên tạo mã mới.");
            }

            if (!link.CanResume(now))
            {
                return InvalidCode;
            }
        }
        else if (!link.CanOpen(now))
        {
            return InvalidCode;
        }

        var linkForms = await LoadFormsAsync(link, cancellationToken).ConfigureAwait(false);
        if (linkForms.Count == link.FormIds.Length && linkForms.All(f => f.PatientSignature is not null))
        {
            return Error.Conflict("remote_signing.already_signed", "Các phiếu đã được ký. Ông/bà có thể đóng trang này.");
        }

        if (linkForms.Count != link.FormIds.Length
            || linkForms.Any(f => f.Status != ConsentFormStatus.PendingSignature || f.PatientSignature is not null || f.SigningSessionId != link.SessionId))
        {
            return InvalidCode;
        }

        string? deviceKey = null;
        if (!resuming)
        {
            var (patient, _) = await formService.ResolvePeopleAsync(linkForms[0], cancellationToken).ConfigureAwait(false);
            if (request.DateOfBirth is not { } dateOfBirth)
            {
                return Error.ValidationField("dateOfBirth", "Vui lòng nhập ngày sinh của người bệnh.");
            }

            if (dateOfBirth != patient.DateOfBirth)
            {
                return await WrongBirthDateAsync(link, linkForms, now, cancellationToken).ConfigureAwait(false);
            }

            deviceKey = NewSecret(DeviceKeyBytes);
            link.Open(Hash(deviceKey), now);
            foreach (var form in linkForms)
            {
                formService.AddAudit(AuditActions.ConsentFormRemoteSigningOpened, form.Id, JsonSerializer.Serialize(new { link = link.Id }));
            }

            // Hai điện thoại cùng mở một lúc: rowversion của mã làm máy lưu sau bị từ chối.
            await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        var staff = await users.GetByIdAsync(link.CreatedByUserId, cancellationToken).ConfigureAwait(false);
        var token = tokens.CreateSigningToken(
            new SigningTokenClaims(link.CreatedByUserId, staff?.UserName ?? string.Empty, [.. linkForms.Select(f => f.Id)], link.SessionId));
        var details = new List<ConsentFormDetailDto>(linkForms.Count);
        foreach (var form in linkForms)
        {
            details.Add(PatientView(await formService.BuildDetailAsync(form, cancellationToken).ConfigureAwait(false)));
        }

        var expiresAt = token.ExpiresAtUtc < link.OpenedAtUtc!.Value + RemoteSigningLink.SignWindow
            ? token.ExpiresAtUtc
            : link.OpenedAtUtc.Value + RemoteSigningLink.SignWindow;
        return new RemoteSigningSessionDto(token.Value, expiresAt, deviceKey, link.BundleId, details);
    }

    /// <summary>Mã băm SHA-256 (hex chữ thường) của mã trong QR hoặc khóa thiết bị.</summary>
    internal static string Hash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(secret)));

    private async Task<Result<RemoteSigningLinkDto>> CreateAsync(
        IReadOnlyList<ConsentForm> signable, Guid? bundleId, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new InvalidOperationException("Chưa xác định người dùng.");
        var now = clock.UtcNow;
        var sessionId = Guid.NewGuid();
        var code = NewSecret(CodeBytes);
        List<Guid> formIds = [.. signable.Select(f => f.Id)];
        var link = RemoteSigningLink.Create(Hash(code), signable[0].PatientId, sessionId, formIds, bundleId, userId, now);
        links.Add(link);
        foreach (var form in signable)
        {
            // Mở phiên ký mới: mã cũ, phiên ký trên máy bệnh viện (nếu có) hết hiệu lực.
            form.StartSigning(sessionId, now);
            formService.AddAudit(
                AuditActions.ConsentFormRemoteSigningCreated,
                form.Id,
                JsonSerializer.Serialize(new { link = link.Id, bundle = bundleId, forms = formIds.Count }));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new RemoteSigningLinkDto(link.Id, code, link.ExpiresAtUtc, formIds);
    }

    private async Task<Result<RemoteSigningSessionDto>> WrongBirthDateAsync(
        RemoteSigningLink link, IReadOnlyList<ConsentForm> linkForms, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var locked = link.RegisterFailedAttempt(now);
        foreach (var form in linkForms)
        {
            formService.AddAudit(
                locked ? AuditActions.ConsentFormRemoteSigningCancelled : AuditActions.ConsentFormRemoteSigningFailed,
                form.Id,
                JsonSerializer.Serialize(new { link = link.Id, attempts = link.FailedAttempts, reason = locked ? "tooManyAttempts" : null }));
            if (locked)
            {
                form.EndSigning(link.SessionId, now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (locked)
        {
            return Error.BusinessRule(
                "remote_signing.locked",
                $"Ngày sinh không đúng {RemoteSigningLink.MaxBirthDateAttempts} lần nên mã đã bị hủy. Ông/bà nhờ nhân viên tạo mã mới.");
        }

        return Error.BusinessRule("remote_signing.wrong_birth_date", "Ngày sinh không đúng với hồ sơ của người bệnh. Ông/bà kiểm tra lại.")
            with
            {
                Metadata = new Dictionary<string, object?>
                {
                    ["remainingAttempts"] = RemoteSigningLink.MaxBirthDateAttempts - link.FailedAttempts,
                },
            };
    }

    /// <summary>Mã ký và các phiếu của mã, chỉ khi người dùng xem được các phiếu đó (phạm vi khoa).</summary>
    private async Task<(RemoteSigningLink? Link, IReadOnlyList<ConsentForm> Forms)> LoadInScopeAsync(
        Guid linkId, CancellationToken cancellationToken)
    {
        var link = await links.GetByIdAsync(linkId, cancellationToken).ConfigureAwait(false);
        if (link is null)
        {
            return (null, []);
        }

        var linkForms = new List<ConsentForm>(link.FormIds.Length);
        foreach (var formId in link.FormIds)
        {
            if (await formService.LoadInScopeAsync(formId, cancellationToken).ConfigureAwait(false) is not { } form)
            {
                return (null, []);
            }

            linkForms.Add(form);
        }

        return (link, linkForms);
    }

    private async Task<IReadOnlyList<ConsentForm>> LoadFormsAsync(RemoteSigningLink link, CancellationToken cancellationToken)
    {
        var result = new List<ConsentForm>(link.FormIds.Length);
        foreach (var formId in link.FormIds)
        {
            if (await forms.GetByIdAsync(formId, cancellationToken).ConfigureAwait(false) is { } form)
            {
                result.Add(form);
            }
        }

        return result;
    }

    private static RemoteSigningStatusDto Status(RemoteSigningLink link, IReadOnlyList<ConsentForm> linkForms, DateTimeOffset now)
    {
        var signedAt = linkForms.Count > 0 && linkForms.All(f => f.PatientSignature is not null)
            ? linkForms.Max(f => f.PatientSignature!.SignedAtUtc)
            : (DateTimeOffset?)null;
        var state = signedAt is not null ? RemoteSigningState.Signed
            : link.CancelReason == RemoteSigningCancelReason.TooManyAttempts ? RemoteSigningState.Locked
            : link.IsCancelled ? RemoteSigningState.Cancelled
            : linkForms.Any(f => f.SigningSessionId != link.SessionId) ? RemoteSigningState.Superseded
            : link.IsOpened ? (link.CanResume(now) ? RemoteSigningState.Opened : RemoteSigningState.Expired)
            : link.CanOpen(now) ? RemoteSigningState.Waiting
            : RemoteSigningState.Expired;
        return new RemoteSigningStatusDto(link.Id, state, link.ExpiresAtUtc, link.OpenedAtUtc, signedAt);
    }

    /// <summary>
    /// Nội dung phiếu gửi xuống điện thoại người bệnh: phần cần đọc và ký; bỏ thông tin nội bộ (người lập, các bước xác nhận,
    /// lưu ý cho nhân viên) và che bớt số giấy tờ, số điện thoại.
    /// </summary>
    private static ConsentFormDetailDto PatientView(ConsentFormDetailDto form) => form with
    {
        Patient = form.Patient with
        {
            CitizenId = SensitiveMask.CitizenId(form.Patient.CitizenId),
            PhoneNumber = SensitiveMask.PhoneNumber(form.Patient.PhoneNumber),
            PatientCode = null,
        },
        Representative = form.Representative is { } representative
            ? representative with
            {
                CitizenId = SensitiveMask.CitizenId(representative.CitizenId),
                PhoneNumber = SensitiveMask.PhoneNumber(representative.PhoneNumber),
            }
            : null,
        HospitalSignerRoleCode = string.Empty,
        CreatedByUserId = Guid.Empty,
        CreatedByName = string.Empty,
        Issues = new Dictionary<string, string[]>(),
        Signatures = [],
        Steps = [],
        SignedPdfSha256 = null,
        ReplacesForm = null,
        ReplacedBy = null,
        Actions = new ConsentFormActions(false, false, false, false, false, false, false, false, false),
        Notices = [],
        Encounter = null,
    };

    private static string NewSecret(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>Đúng độ dài và bảng chữ base64url của mã sinh ra (không dò bảng băm với chuỗi lạ).</summary>
    private static bool IsWellFormed(string? value, int bytes) =>
        value is { } text
        && text.Length == Base64Url.GetEncodedLength(bytes)
        && text.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool SameHash(string hash, string? expected) =>
        expected is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), Encoding.ASCII.GetBytes(expected));
}
