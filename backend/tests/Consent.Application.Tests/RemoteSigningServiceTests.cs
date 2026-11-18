using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Ký trên điện thoại riêng của người bệnh qua mã QR (quyết định 32). Dữ liệu giả lập.</summary>
public sealed class RemoteSigningServiceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>Ngày sinh của người bệnh giả lập trong <see cref="ConsentScenario"/>.</summary>
    private static readonly DateOnly BirthDate = new(1988, 4, 5);

    /// <summary>Tiếp đón Khoa Khám bệnh lập và trình ký một phiếu CK-01 (người bệnh tự ký).</summary>
    private static async Task<(ConsentScenario S, Guid FormId)> PendingCk01()
    {
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;
        Assert.True((await s.Service.UpdateAsync(form.Id, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), Ct)).IsSuccess);
        Assert.True((await s.Service.SubmitAsync(form.Id, Ct)).IsSuccess);
        return (s, form.Id);
    }

    private static async Task<RemoteSigningLinkDto> NewCode(ConsentScenario s, Guid formId)
    {
        var created = await s.Ctx.CreateRemoteSigningService().CreateForFormAsync(formId, Ct);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return created.Value;
    }

    /// <summary>Điện thoại người bệnh mở mã: không có người dùng đăng nhập.</summary>
    private static async Task<Result<RemoteSigningSessionDto>> Claim(
        ConsentScenario s, string? code, DateOnly? birthDate, string? deviceKey = null)
    {
        var user = s.Ctx.CurrentUser;
        var (userId, userName, departmentId) = (user.UserId, user.UserName, user.DepartmentId);
        var (roles, permissions) = (user.Roles.ToList(), user.Permissions.ToList());
        user.UserId = null;
        user.UserName = null;
        user.DepartmentId = null;
        user.Roles.Clear();
        user.Permissions.Clear();
        try
        {
            return await s.Ctx.CreateRemoteSigningService().ClaimAsync(new(code, birthDate, deviceKey), Ct);
        }
        finally
        {
            user.UserId = userId;
            user.UserName = userName;
            user.DepartmentId = departmentId;
            user.Roles.UnionWith(roles);
            user.Permissions.UnionWith(permissions);
        }
    }

    /// <summary>Điện thoại ký phiếu bằng token phiên ký vừa nhận khi mở mã.</summary>
    private static async Task<Result<PatientSignatureResultDto>> SignOnPhone(ConsentScenario s, Guid formId)
    {
        var back = s.UseSigningToken();
        try
        {
            return await s.Signing.SignAsPatientAsync(formId, new(ConsentDecision.Agreed, SignaturePng), Ct);
        }
        finally
        {
            back();
        }
    }

    private static Task<Result<RemoteSigningStatusDto>> Status(ConsentScenario s, Guid linkId) =>
        s.Ctx.CreateRemoteSigningService().GetStatusAsync(linkId, Ct);

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(text)));

    private static int Audits(ConsentScenario s, string action) => s.Ctx.Store.AuditLogs.Count(a => a.Action == action);

    [Fact]
    public async Task CreateForForm_GivesAOneTimeCode_StoresOnlyItsHash_AndReplacesTheOpenSigningSession()
    {
        var (s, formId) = await PendingCk01();
        Assert.True((await s.Signing.StartSigningAsync(formId, Ct)).IsSuccess);
        var form = s.Ctx.Store.ConsentForms.Single(f => f.Id == formId);
        var deviceSession = form.SigningSessionId;

        var code = await NewCode(s, formId);

        var link = Assert.Single(s.Ctx.Store.RemoteSigningLinks);
        Assert.Equal(22, code.Code.Length);
        Assert.Equal(Sha256(code.Code), link.CodeHash);
        Assert.DoesNotContain(code.Code, link.CodeHash, StringComparison.Ordinal);
        Assert.Equal([formId], code.FormIds);
        Assert.Equal(s.Ctx.Clock.UtcNow + RemoteSigningLink.OpenWindow, code.ExpiresAtUtc);
        // Phiên ký mới thay phiên ký trên máy của bệnh viện vừa mở.
        Assert.Equal(link.SessionId, form.SigningSessionId);
        Assert.NotEqual(deviceSession, form.SigningSessionId);
        Assert.Equal(1, Audits(s, AuditActions.ConsentFormRemoteSigningCreated));
        Assert.DoesNotContain(s.Ctx.Store.AuditLogs, a => a.Details?.Contains(code.Code, StringComparison.Ordinal) == true);
        Assert.Equal(RemoteSigningState.Waiting, (await Status(s, code.Id)).Value.State);
    }

    [Fact]
    public async Task CreateForForm_OnlyForAFormWaitingForThePatient_InTheUsersDepartment()
    {
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var draft = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;
        var (other, pendingId) = await PendingCk01();

        var forDraft = await s.Ctx.CreateRemoteSigningService().CreateForFormAsync(draft.Id, Ct);
        other.LogIn(other.Noi);
        var otherDepartment = await other.Ctx.CreateRemoteSigningService().CreateForFormAsync(pendingId, Ct);

        Assert.Equal("consent_forms.invalid_status", forDraft.Error!.Code);
        Assert.Equal(ErrorType.NotFound, otherDepartment.Error!.Type);
        Assert.Empty(s.Ctx.Store.RemoteSigningLinks);
        Assert.Empty(other.Ctx.Store.RemoteSigningLinks);
    }

    [Fact]
    public async Task Claim_WithTheRightBirthDate_GivesATokenForThoseFormsOnly_AndThePatientsView()
    {
        var (s, formId) = await PendingCk01();
        var staffId = s.Ctx.CurrentUser.UserId;
        var code = await NewCode(s, formId);

        var opened = await Claim(s, code.Code, BirthDate);

        Assert.True(opened.IsSuccess, opened.Error?.Message);
        var link = Assert.Single(s.Ctx.Store.RemoteSigningLinks);
        var claims = s.Ctx.Tokens.LastSigningClaims!;
        Assert.Equal([formId], claims.FormIds);
        Assert.Equal(link.SessionId, claims.SessionId);
        Assert.Equal(staffId, claims.StaffUserId);
        Assert.Equal(43, opened.Value.DeviceKey!.Length);
        Assert.Equal(Sha256(opened.Value.DeviceKey), link.DeviceKeyHash);
        Assert.Equal(s.Ctx.Clock.UtcNow, link.OpenedAtUtc);
        Assert.Null(opened.Value.BundleId);
        // Nội dung để người bệnh đọc và ký; số giấy tờ, điện thoại che bớt; không có thông tin nội bộ.
        var form = Assert.Single(opened.Value.Forms);
        Assert.Equal(formId, form.Id);
        Assert.Equal("Phạm Thị Dung (giả lập)", form.Patient.FullName);
        Assert.Equal(BirthDate, form.Patient.DateOfBirth);
        Assert.NotEqual("036188000125", form.Patient.CitizenId);
        Assert.Contains('*', form.Patient.CitizenId!);
        Assert.NotEqual("0900000125", form.Patient.PhoneNumber);
        Assert.Null(form.Patient.PatientCode);
        Assert.Equal(string.Empty, form.CreatedByName);
        Assert.Empty(form.Notices);
        Assert.False(form.Actions.CanStartSigning || form.Actions.CanConfirm || form.Actions.CanEdit);
        Assert.Equal("Trạm Y tế Phường Mẫu", form.Data["primaryCareFacility"]!.GetValue<string>());
        Assert.Equal(1, Audits(s, AuditActions.ConsentFormRemoteSigningOpened));
        Assert.Equal(RemoteSigningState.Opened, (await Status(s, code.Id)).Value.State);

        // Ký bằng token phiên ký như trên máy của bệnh viện; xong thì mã không mở lại được.
        var signed = await SignOnPhone(s, formId);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        var status = (await Status(s, code.Id)).Value;
        Assert.Equal(RemoteSigningState.Signed, status.State);
        Assert.Equal(signed.Value.SignedAtUtc, status.SignedAtUtc);
        var reopened = await Claim(s, code.Code, null, opened.Value.DeviceKey);
        Assert.Equal("remote_signing.already_signed", reopened.Error!.Code);
    }

    [Fact]
    public async Task Claim_NeedsTheBirthDate_AndCancelsTheCodeAfterFiveWrongTries()
    {
        var (s, formId) = await PendingCk01();
        var code = await NewCode(s, formId);

        var missing = await Claim(s, code.Code, null);
        var remaining = new List<object?>();
        for (var attempt = 1; attempt < RemoteSigningLink.MaxBirthDateAttempts; attempt++)
        {
            var wrong = await Claim(s, code.Code, BirthDate.AddDays(attempt));
            Assert.Equal("remote_signing.wrong_birth_date", wrong.Error!.Code);
            remaining.Add(wrong.Error.Metadata!["remainingAttempts"]);
        }

        var locked = await Claim(s, code.Code, BirthDate.AddYears(1));
        var right = await Claim(s, code.Code, BirthDate);

        Assert.Contains("dateOfBirth", missing.Error!.FieldErrors!.Keys);
        Assert.Equal([4, 3, 2, 1], remaining);
        Assert.Equal("remote_signing.locked", locked.Error!.Code);
        Assert.Equal("remote_signing.invalid", right.Error!.Code);
        Assert.Null(s.Ctx.Store.ConsentForms.Single(f => f.Id == formId).SigningSessionId);
        Assert.Equal(RemoteSigningState.Locked, (await Status(s, code.Id)).Value.State);
        Assert.Equal(4, Audits(s, AuditActions.ConsentFormRemoteSigningFailed));
        Assert.Equal(1, Audits(s, AuditActions.ConsentFormRemoteSigningCancelled));
        Assert.Null(s.Ctx.Tokens.LastSigningClaims);
    }

    [Fact]
    public async Task Claim_OpensOnOnePhoneOnly_ThatPhoneCanReloadWithoutTheBirthDate()
    {
        var (s, formId) = await PendingCk01();
        var code = await NewCode(s, formId);
        var phoneA = (await Claim(s, code.Code, BirthDate)).Value.DeviceKey!;

        var phoneB = await Claim(s, code.Code, BirthDate);
        var phoneBGuessing = await Claim(s, code.Code, null, Convert.ToBase64String(new byte[32]).TrimEnd('=').Replace('+', '-').Replace('/', '_'));
        var reload = await Claim(s, code.Code, null, phoneA);

        Assert.Equal("remote_signing.opened_elsewhere", phoneB.Error!.Code);
        Assert.Equal("remote_signing.opened_elsewhere", phoneBGuessing.Error!.Code);
        Assert.True(reload.IsSuccess, reload.Error?.Message);
        Assert.Null(reload.Value.DeviceKey);
        Assert.Single(reload.Value.Forms);
        Assert.Equal(1, Audits(s, AuditActions.ConsentFormRemoteSigningOpened));
    }

    [Fact]
    public async Task Codes_ExpireIfNotOpenedInTime_OrAfterTheSigningWindow()
    {
        var (s, formId) = await PendingCk01();
        var late = await NewCode(s, formId);
        s.Ctx.Clock.Advance(RemoteSigningLink.OpenWindow + TimeSpan.FromMinutes(1));
        var tooLate = await Claim(s, late.Code, BirthDate);
        var lateStatus = (await Status(s, late.Id)).Value.State;

        var code = await NewCode(s, formId);
        var deviceKey = (await Claim(s, code.Code, BirthDate)).Value.DeviceKey;
        s.Ctx.Clock.Advance(RemoteSigningLink.SignWindow + TimeSpan.FromMinutes(1));
        var reloadTooLate = await Claim(s, code.Code, null, deviceKey);

        Assert.Equal("remote_signing.invalid", tooLate.Error!.Code);
        Assert.Equal(RemoteSigningState.Expired, lateStatus);
        Assert.Equal("remote_signing.invalid", reloadTooLate.Error!.Code);
        Assert.Equal(RemoteSigningState.Expired, (await Status(s, code.Id)).Value.State);
    }

    [Fact]
    public async Task ANewSigningSession_SupersedesTheCode_AndItsToken()
    {
        var (s, formId) = await PendingCk01();
        var code = await NewCode(s, formId);
        var deviceKey = (await Claim(s, code.Code, BirthDate)).Value.DeviceKey;
        var remoteClaims = s.Ctx.Tokens.LastSigningClaims!;

        // Người bệnh đổi ý, ký trên máy của bệnh viện: nhân viên mở phiên ký mới.
        Assert.True((await s.Signing.StartSigningAsync(formId, Ct)).IsSuccess);
        var status = (await Status(s, code.Id)).Value.State;
        var reload = await Claim(s, code.Code, null, deviceKey);
        s.Ctx.CurrentUser.SigningFormIds = remoteClaims.FormIds;
        s.Ctx.CurrentUser.SigningSessionId = remoteClaims.SessionId;
        var signWithOldToken = await s.Signing.SignAsPatientAsync(formId, new(ConsentDecision.Agreed, SignaturePng), Ct);

        Assert.Equal(RemoteSigningState.Superseded, status);
        Assert.Equal("remote_signing.invalid", reload.Error!.Code);
        Assert.Equal("consent_forms.invalid_status", signWithOldToken.Error!.Code);
    }

    [Fact]
    public async Task Cancel_EndsTheSession_SoAPhoneThatOpenedTheCodeCannotSign()
    {
        var (s, formId) = await PendingCk01();
        var code = await NewCode(s, formId);
        var deviceKey = (await Claim(s, code.Code, BirthDate)).Value.DeviceKey;

        var cancelled = await s.Ctx.CreateRemoteSigningService().CancelAsync(code.Id, Ct);
        var signed = await SignOnPhone(s, formId);
        var reload = await Claim(s, code.Code, null, deviceKey);

        Assert.Equal(RemoteSigningState.Cancelled, cancelled.Value.State);
        Assert.Equal("consent_forms.invalid_status", signed.Error!.Code);
        Assert.Equal("remote_signing.invalid", reload.Error!.Code);
        Assert.Null(s.Ctx.Store.ConsentForms.Single(f => f.Id == formId).SigningSessionId);
        Assert.Equal(1, Audits(s, AuditActions.ConsentFormRemoteSigningCancelled));
    }

    [Fact]
    public async Task StatusAndCancel_OnlyForStaffWhoCanSeeTheForms()
    {
        var (s, formId) = await PendingCk01();
        var code = await NewCode(s, formId);
        s.LogIn(s.Noi);

        var status = await Status(s, code.Id);
        var cancel = await s.Ctx.CreateRemoteSigningService().CancelAsync(code.Id, Ct);

        Assert.Equal(ErrorType.NotFound, status.Error!.Type);
        Assert.Equal(ErrorType.NotFound, cancel.Error!.Type);
        Assert.False(s.Ctx.Store.RemoteSigningLinks.Single().IsCancelled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ngan-qua")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA")]
    public async Task Claim_RejectsUnknownOrMalformedCodes_WithTheSameAnswer(string? code)
    {
        var (s, formId) = await PendingCk01();
        await NewCode(s, formId);

        var result = await Claim(s, code, BirthDate);

        Assert.Equal("remote_signing.invalid", result.Error!.Code);
        Assert.Equal(0, s.Ctx.Store.RemoteSigningLinks.Single().FailedAttempts);
    }

    [Fact]
    public async Task Bundle_OneCodeForEveryFormWaitingForThePatient_SignedOnceOnThePhone()
    {
        var s = ConsentScenario.Create();
        var bm02 = s.AddHospitalTemplate("QT-KNGT-BM02");
        var tuvan = s.AddHospitalTemplate("TUVAN-CONGTT");
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var bundle = (await s.Service.CreateBundleAsync(new(s.Patient.Id, [bm02.Id, tuvan.Id], null), Ct)).Value;
        var bm02Id = bundle.Forms.Single(f => f.TemplateId == bm02.Id).Id;
        var tuvanId = bundle.Forms.Single(f => f.TemplateId == tuvan.Id).Id;
        Assert.True((await s.Service.UpdateAsync(
            bm02Id,
            new(new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" }, ConsentSignerKind.Patient, null),
            Ct)).IsSuccess);
        Assert.True((await s.Service.UpdateAsync(tuvanId, new([], ConsentSignerKind.Patient, null), Ct)).IsSuccess);
        Assert.True((await s.Service.SubmitAsync(bm02Id, Ct)).IsSuccess);
        Assert.True((await s.Service.SubmitAsync(tuvanId, Ct)).IsSuccess);

        var created = await s.Ctx.CreateRemoteSigningService().CreateForBundleAsync(bundle.BundleId, Ct);
        var opened = await Claim(s, created.Value.Code, BirthDate);
        var back = s.UseSigningToken();
        var signed = await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, "Tôi đã nghe, hiểu và đồng ý", new Dictionary<Guid, ConsentDecision> { [tuvanId] = ConsentDecision.Agreed }),
            Ct);
        back();

        Assert.True(created.IsSuccess, created.Error?.Message);
        Assert.Equal(new[] { bm02Id, tuvanId }.Order(), created.Value.FormIds.Order());
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        Assert.Equal(bundle.BundleId, opened.Value.BundleId);
        Assert.Equal(2, opened.Value.Forms.Count);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        Assert.Equal(RemoteSigningState.Signed, (await Status(s, created.Value.Id)).Value.State);
        Assert.Equal(2, Audits(s, AuditActions.ConsentFormRemoteSigningOpened));
    }
}
