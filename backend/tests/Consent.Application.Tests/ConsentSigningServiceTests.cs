using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ConsentSigningServiceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    /// <summary>Tiếp đón (vai trò ký CK-01) lập và trình ký một phiếu CK-01 ở Khoa Khám bệnh.</summary>
    private static async Task<(ConsentScenario S, ConsentFormDetailDto Form)> PendingCk01(ConsentSignerKind signer = ConsentSignerKind.Patient)
    {
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        var representativeId = signer == ConsentSignerKind.Representative ? s.Patient.Representatives.Single().Id : (Guid?)null;
        await s.Service.UpdateAsync(form.Id, new(ConsentScenario.CompleteCk01(), signer, representativeId), CancellationToken.None);
        var submitted = await s.Service.SubmitAsync(form.Id, CancellationToken.None);
        Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        return (s, submitted.Value);
    }

    /// <summary>Mở phiên ký rồi người bệnh ký trên máy tính bảng (dùng token phiên ký).</summary>
    private static async Task<Result<PatientSignatureResultDto>> PatientSigns(
        ConsentScenario s,
        Guid formId,
        ConsentDecision? decision = ConsentDecision.Agreed,
        string? image = null,
        string? statement = "Tôi đã nghe, hiểu và đồng ý")
    {
        Assert.True((await s.Signing.StartSigningAsync(formId, CancellationToken.None)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        var result = await s.Signing.SignAsPatientAsync(formId, new(decision, image ?? SignaturePng, statement), CancellationToken.None);
        staffLogsInAgain();
        return result;
    }

    [Fact]
    public async Task StartSigning_IssuesATokenForThisFormAndSession_OnlyForPendingForms()
    {
        var (s, pending) = await PendingCk01();
        var draft = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        var session = await s.Signing.StartSigningAsync(pending.Id, CancellationToken.None);
        var onDraft = await s.Signing.StartSigningAsync(draft.Id, CancellationToken.None);

        Assert.True(session.IsSuccess);
        var claims = s.Ctx.Tokens.LastSigningClaims!;
        Assert.Equal(pending.Id, claims.FormId);
        Assert.Equal(s.Ctx.CurrentUser.UserId, claims.StaffUserId);
        Assert.Equal(claims.SessionId, s.Ctx.Store.ConsentForms.Single(f => f.Id == pending.Id).SigningSessionId);
        Assert.Equal(s.Ctx.Clock.UtcNow.AddMinutes(30), session.Value.ExpiresAtUtc);
        Assert.Equal("consent_forms.invalid_status", onDraft.Error!.Code);
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormSigningStarted);
    }

    [Fact]
    public async Task PatientSignature_NeedsTheSigningTokenOfThisFormAndSession_AndWorksOnce()
    {
        var (s, form) = await PendingCk01();
        await s.Signing.StartSigningAsync(form.Id, CancellationToken.None);
        var firstSession = s.Ctx.Tokens.LastSigningClaims!;

        // Token đăng nhập thường (không phải token phiên ký): bị từ chối.
        var staffToken = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, SignaturePng), CancellationToken.None);

        // Mở phiên mới: phiên cũ hết hiệu lực.
        await s.Signing.StartSigningAsync(form.Id, CancellationToken.None);
        s.Ctx.CurrentUser.SigningFormId = firstSession.FormId;
        s.Ctx.CurrentUser.SigningSessionId = firstSession.SessionId;
        var oldSession = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, SignaturePng), CancellationToken.None);

        s.UseSigningToken();
        var otherForm = await s.Signing.SignAsPatientAsync(Guid.NewGuid(), new(ConsentDecision.Agreed, SignaturePng), CancellationToken.None);
        var signed = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Refused, SignaturePng), CancellationToken.None);
        var again = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, SignaturePng), CancellationToken.None);

        Assert.Equal(ErrorType.Forbidden, staffToken.Error!.Type);
        Assert.Equal("consent_forms.invalid_status", oldSession.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, otherForm.Error!.Type);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        Assert.Equal(ConsentDecision.Refused, signed.Value.Decision);
        Assert.Equal("consent_forms.invalid_status", again.Error!.Code);

        var stored = s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id);
        var signature = stored.PatientSignature!;
        Assert.Equal("Phạm Thị Dung (giả lập)", signature.SignerName);
        Assert.Equal("unit-test", signature.Device);
        // Ảnh lưu lại là ảnh đã chuẩn hóa ở máy chủ (PNG xám, cắt sát nét ký), không phải ảnh gốc.
        Assert.Equal(0, TestPng.ReadHeader(signature.ImagePng!).ColorType);
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormPatientSigned && a.Details!.Contains("Refused"));
    }

    [Fact]
    public async Task PatientSignature_RejectsBlankInvalidAndMissingInput()
    {
        var (s, form) = await PendingCk01();

        var blank = await PatientSigns(s, form.Id, image: Convert.ToBase64String(TestPng.Blank()));
        s.UseSigningToken();
        var notPng = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, Convert.ToBase64String("không phải ảnh"u8.ToArray())), CancellationToken.None);
        var notBase64 = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, "@@@"), CancellationToken.None);
        var noDecision = await s.Signing.SignAsPatientAsync(form.Id, new(null, SignaturePng), CancellationToken.None);
        var tooLarge = await s.Signing.SignAsPatientAsync(
            form.Id, new(ConsentDecision.Agreed, new string('A', PatientSignatureRequestValidator.MaxImageLength + 4)), CancellationToken.None);

        Assert.Equal(ErrorType.BusinessRule, blank.Error!.Type);
        Assert.Equal("consent_forms.signature_blank", blank.Error.Code);
        Assert.Contains("image", notPng.Error!.FieldErrors!.Keys);
        Assert.Contains("image", notBase64.Error!.FieldErrors!.Keys);
        Assert.Contains("decision", noDecision.Error!.FieldErrors!.Keys);
        Assert.Contains("image", tooLarge.Error!.FieldErrors!.Keys);
        Assert.Null(s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id).PatientSignature);
    }

    /// <summary>Tiếp đón lập và trình ký một phiếu theo mẫu thật <paramref name="code"/>.</summary>
    private static async Task<(ConsentScenario S, ConsentFormDetailDto Form)> PendingHospitalForm(string code, JsonObject data)
    {
        var s = ConsentScenario.Create();
        var template = s.AddHospitalTemplate(code);
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, template.Id, null), CancellationToken.None)).Value;
        var updated = await s.Service.UpdateAsync(form.Id, new(data, ConsentSignerKind.Patient, null), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);
        var submitted = await s.Service.SubmitAsync(form.Id, CancellationToken.None);
        Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        return (s, submitted.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(ConsentDecision.Refused)]
    public async Task ConfirmOnlyTemplate_SignsWithoutDecision_AndIgnoresAnySentDecision(ConsentDecision? sent)
    {
        var (s, form) = await PendingHospitalForm(
            "QT-KNGT-BM02", new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" });

        var signed = await PatientSigns(s, form.Id, sent);
        var completed = await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        Assert.True(signed.IsSuccess, signed.Error?.Message);
        Assert.Null(signed.Value.Decision);
        Assert.True(completed.IsSuccess, completed.Error?.Message);
        Assert.Null(completed.Value.Decision);
        Assert.Null(s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id).Decision);
    }

    [Fact]
    public async Task Statement_IsRequiredByHospitalTemplates_SuggestingTheFirstPhrase()
    {
        var (s, form) = await PendingHospitalForm("TUVAN-CONGTT", []);

        var missing = await PatientSigns(s, form.Id, statement: "   ");
        var signed = await PatientSigns(s, form.Id, statement: "  Tôi đã nghe, hiểu và đồng ý  ");
        var detail = await s.Service.GetAsync(form.Id, CancellationToken.None);
        await s.Service.GetPdfAsync(form.Id, CancellationToken.None);

        Assert.Equal(["Vui lòng ghi lời xác nhận, ví dụ: “Tôi đã nghe, hiểu và đồng ý”."], missing.Error!.FieldErrors!["statement"]);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        var patientSignature = detail.Value.Signatures.Single(x => x.Method == ConsentSignatureMethod.Handwritten);
        Assert.Equal("Tôi đã nghe, hiểu và đồng ý", patientSignature.Statement);
        Assert.Equal("Tôi đã nghe, hiểu và đồng ý", s.Ctx.Pdf.Last!.PatientSignature!.Statement);
    }

    [Fact]
    public async Task Statement_IsIgnored_WhenTheTemplateHasNoStatementBox()
    {
        var s = ConsentScenario.Create();
        s.LogIn(s.Kkb);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        await s.Service.UpdateAsync(form.Id, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), CancellationToken.None);
        await s.Service.SubmitAsync(form.Id, CancellationToken.None);

        var signed = await PatientSigns(s, form.Id, statement: "Tôi đã nghe, hiểu và đồng ý");

        Assert.True(signed.IsSuccess, signed.Error?.Message);
        Assert.Null(s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id).PatientSignature!.Statement);
    }

    [Fact]
    public async Task TemplateWithCustomDecisionLabels_StillRequiresADecision_NamingTheLabels()
    {
        var (s, form) = await PendingHospitalForm("TUVAN-CONGTT", []);

        var missing = await PatientSigns(s, form.Id, decision: null);
        s.UseSigningToken();
        var agreed = await s.Signing.SignAsPatientAsync(
            form.Id, new(ConsentDecision.Agreed, SignaturePng, "Tôi đã nghe, hiểu và đồng ý"), CancellationToken.None);

        Assert.Equal(
            "Vui lòng chọn Đồng ý (Agree) hoặc Không đồng ý (Disagree).", missing.Error!.FieldErrors!["decision"][0]);
        Assert.True(agreed.IsSuccess, agreed.Error?.Message);
        Assert.Equal(ConsentDecision.Agreed, agreed.Value.Decision);
    }

    [Fact]
    public async Task Complete_RequiresTheStaffHandwrittenSignature_StoresItAndPrintsItOnThePdf()
    {
        var (s, form) = await PendingCk01();
        await PatientSigns(s, form.Id);

        var missing = await s.Signing.CompleteAsync(form.Id, new(null), CancellationToken.None);
        var blank = await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Blank())), CancellationToken.None);
        var notPng = await s.Signing.CompleteAsync(form.Id, new("@@@"), CancellationToken.None);
        var completed = await s.Signing.CompleteAsync(form.Id, new(SignaturePng), CancellationToken.None);

        Assert.Contains("image", missing.Error!.FieldErrors!.Keys);
        Assert.Equal("consent_forms.signature_blank", blank.Error!.Code);
        Assert.Contains("image", notPng.Error!.FieldErrors!.Keys);
        Assert.True(completed.IsSuccess, completed.Error?.Message);
        var hospital = s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id).HospitalSignature!;
        // Ảnh lưu lại là ảnh đã chuẩn hóa ở máy chủ (PNG xám), giống chữ ký của người bệnh.
        Assert.Equal(0, TestPng.ReadHeader(hospital.ImagePng!).ColorType);
        Assert.Equal(hospital.ImagePng, s.Ctx.Pdf.Last!.HospitalConfirmation!.ImagePng);
    }

    [Fact]
    public async Task Complete_NeedsPatientSignatureFirst_AndTheRoleConfiguredOnTheTemplate()
    {
        var (s, form) = await PendingCk01();

        var beforePatient = await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);
        await PatientSigns(s, form.Id);
        s.LogIn(s.Kkb); // điều dưỡng cùng khoa: không phải vai trò xác nhận của CK-01
        var wrongRole = await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        Assert.Equal("consent_forms.invalid_status", beforePatient.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, wrongRole.Error!.Type);
        Assert.Contains("Tiếp đón", wrongRole.Error.Message, StringComparison.Ordinal);
        Assert.Equal(ConsentFormStatus.PendingSignature, s.Ctx.Store.ConsentForms.Single(f => f.Id == form.Id).Status);
    }

    [Fact]
    public async Task Complete_StoresTheFinalPdfWithItsHash_AndLocksTheForm()
    {
        var (s, form) = await PendingCk01(ConsentSignerKind.Representative);
        await PatientSigns(s, form.Id);

        var completed = await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        Assert.True(completed.IsSuccess, completed.Error?.Message);
        var detail = completed.Value;
        Assert.Equal(ConsentFormStatus.Completed, detail.Status);
        Assert.Equal(ConsentDecision.Agreed, detail.Decision);
        Assert.Collection(
            detail.Signatures,
            patient =>
            {
                Assert.Equal(ConsentSignerRole.Representative, patient.Role);
                Assert.Equal(ConsentSignatureMethod.Handwritten, patient.Method);
                Assert.Equal("Phạm Văn Chồng (giả lập)", patient.SignerName);
            },
            hospital =>
            {
                Assert.Equal(ConsentSignerRole.Hospital, hospital.Role);
                Assert.Equal(ConsentSignatureMethod.AccountConfirmation, hospital.Method);
                Assert.Equal("Chức danh giả lập", hospital.SignerTitle);
            });

        // PDF bản cuối có chữ ký tay, thông tin người đại diện và xác nhận phía bệnh viện.
        var pdf = s.Ctx.Pdf.Last!;
        Assert.True(pdf.IsFinal);
        Assert.NotNull(pdf.PatientSignature);
        Assert.Equal("036085000777", pdf.Representative!.CitizenId);
        Assert.Equal(TimeSpan.FromHours(7), pdf.HospitalConfirmation!.ConfirmedAt.Offset);

        // File lưu kèm SHA-256; tải PDF trả đúng file đã lưu.
        var (key, stored) = s.Ctx.Documents.Files.Single();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(stored)), detail.SignedPdfSha256);
        var download = await s.Service.GetPdfAsync(form.Id, CancellationToken.None);
        Assert.Equal(stored, download.Value.Content);

        // Phiếu đã hoàn tất không sửa, không trả về nháp, không hủy được.
        var edit = await s.Service.UpdateAsync(form.Id, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), CancellationToken.None);
        var back = await s.Service.ReturnToDraftAsync(form.Id, CancellationToken.None);
        var cancel = await s.Service.CancelAsync(form.Id, new("Muốn hủy phiếu đã ký"), CancellationToken.None);
        Assert.All([edit.Error, back.Error, cancel.Error], e => Assert.Equal("consent_forms.invalid_status", e!.Code));
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormCompleted && a.Details!.Contains(detail.SignedPdfSha256!));
        Assert.NotEmpty(key);
    }

    [Fact]
    public async Task SignedPdf_ThatWasModifiedOnDisk_IsNotServed()
    {
        var (s, form) = await PendingCk01();
        await PatientSigns(s, form.Id);
        await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        s.Ctx.Documents.Tamper(s.Ctx.Documents.Files.Keys.Single());
        var download = await s.Service.GetPdfAsync(form.Id, CancellationToken.None);

        Assert.Equal("consent_forms.signed_pdf_unavailable", download.Error!.Code);
    }

    [Fact]
    public async Task Revoke_NeedsAReason_ThenAReplacementCopiesTheContentOnce()
    {
        var (s, form) = await PendingCk01();
        await PatientSigns(s, form.Id);
        await s.Signing.CompleteAsync(form.Id, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);
        s.Ctx.CurrentUser.Permissions.Add(Permissions.ConsentsRevoke);

        var noReason = await s.Signing.RevokeAsync(form.Id, new(" "), CancellationToken.None);
        var revoked = await s.Signing.RevokeAsync(form.Id, new("Ghi sai hình thức khám"), CancellationToken.None);
        var replacement = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null, form.Id), CancellationToken.None);
        var second = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null, form.Id), CancellationToken.None);
        var original = await s.Service.GetAsync(form.Id, CancellationToken.None);

        Assert.Contains("reason", noReason.Error!.FieldErrors!.Keys);
        Assert.Equal(ConsentFormStatus.Revoked, revoked.Value.Status);
        Assert.Equal("Ghi sai hình thức khám", revoked.Value.RevokeReason);
        Assert.True(revoked.Value.Actions.CanReplace);
        Assert.True(replacement.IsSuccess, replacement.Error?.Message);
        Assert.Equal(ConsentFormStatus.Draft, replacement.Value.Status);
        Assert.Equal(new ConsentFormRef(form.Id, form.FormNo), replacement.Value.ReplacesForm);
        Assert.Equal("bhyt", replacement.Value.Data["examType"]!.GetValue<string>());
        Assert.Equal("consent_forms.already_replaced", second.Error!.Code);
        Assert.Equal(replacement.Value.Id, original.Value.ReplacedBy!.Id);
        Assert.False(original.Value.Actions.CanReplace);
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormRevoked);
    }

    [Fact]
    public async Task Replacement_OnlyForRevokedFormsOfTheSamePatientAndTemplate()
    {
        var (s, form) = await PendingCk01();

        var notRevoked = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null, form.Id), CancellationToken.None);
        var otherTemplate = await s.Service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null, form.Id), CancellationToken.None);

        Assert.Contains("replacesFormId", notRevoked.Error!.FieldErrors!.Keys);
        Assert.Contains("replacesFormId", otherTemplate.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Revoke_IsOnlyForCompletedForms()
    {
        var (s, form) = await PendingCk01();

        var result = await s.Signing.RevokeAsync(form.Id, new("Thu hồi phiếu chưa ký"), CancellationToken.None);

        Assert.Equal("consent_forms.invalid_status", result.Error!.Code);
    }

    [Fact]
    public async Task ReturnToDraft_AfterPatientSigned_DropsTheSignature_AndAllowsSigningAgain()
    {
        var (s, form) = await PendingCk01();
        await PatientSigns(s, form.Id);

        var back = await s.Service.ReturnToDraftAsync(form.Id, CancellationToken.None);
        await s.Service.SubmitAsync(form.Id, CancellationToken.None);
        var signedAgain = await PatientSigns(s, form.Id, ConsentDecision.Agreed);

        Assert.Empty(back.Value.Signatures);
        Assert.Null(back.Value.Decision);
        Assert.True(signedAgain.IsSuccess, signedAgain.Error?.Message);
    }

    [Fact]
    public async Task Actions_FollowStatusRoleAndPermissions()
    {
        var (s, form) = await PendingCk01();

        var pending = (await s.Service.GetAsync(form.Id, CancellationToken.None)).Value.Actions;
        await PatientSigns(s, form.Id);
        var signedForReceptionist = (await s.Service.GetAsync(form.Id, CancellationToken.None)).Value.Actions;
        s.LogIn(s.Kkb);
        var signedForNurse = (await s.Service.GetAsync(form.Id, CancellationToken.None)).Value.Actions;

        Assert.True(pending.CanStartSigning);
        Assert.False(pending.CanConfirm);
        Assert.False(signedForReceptionist.CanStartSigning);
        Assert.True(signedForReceptionist.CanConfirm);
        Assert.False(signedForNurse.CanConfirm);
    }
}
