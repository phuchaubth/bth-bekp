using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>Bộ phiếu (quyết định 23): lập cả bộ, người bệnh ký một lần cho cả bộ, nhân viên xác nhận theo lô.</summary>
public sealed class ConsentBundleTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";

    /// <summary>
    /// Tiếp đón Khoa Khám bệnh lập bộ hai mẫu thật: BM02 (chỉ ký xác nhận, tiếp đón xác nhận cuối) và TUVAN-CONGTT (hỏi Đồng
    /// ý/Không đồng ý; bước người tư vấn là tiếp đón, xác nhận cuối là điều dưỡng).
    /// </summary>
    private static async Task<(ConsentScenario S, ConsentTemplate Bm02, ConsentTemplate Tuvan, ConsentFormBundleDto Bundle)> NewBundle()
    {
        var s = ConsentScenario.Create();
        var bm02 = s.AddHospitalTemplate("QT-KNGT-BM02");
        var tuvan = s.AddHospitalTemplate("TUVAN-CONGTT", ConsentScenario.WithConsultantStep);
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var created = await s.Service.CreateBundleAsync(new(s.Patient.Id, [bm02.Id, tuvan.Id], null), CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Message);
        return (s, bm02, tuvan, created.Value);
    }

    private static Guid FormOf(ConsentFormBundleDto bundle, ConsentTemplate template) =>
        bundle.Forms.Single(f => f.TemplateId == template.Id).Id;

    /// <summary>Soạn và trình ký cả hai phiếu (người bệnh tự ký, trừ khi chỉ định người đại diện cho TUVAN).</summary>
    private static async Task SubmitBoth(
        ConsentScenario s, ConsentFormBundleDto bundle, ConsentTemplate bm02, ConsentTemplate tuvan,
        ConsentSignerKind tuvanSigner = ConsentSignerKind.Patient)
    {
        var representativeId = tuvanSigner == ConsentSignerKind.Representative ? s.Patient.Representatives.Single().Id : (Guid?)null;
        Assert.True((await s.Service.UpdateAsync(
            FormOf(bundle, bm02),
            new(new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" }, ConsentSignerKind.Patient, null),
            CancellationToken.None)).IsSuccess);
        Assert.True((await s.Service.UpdateAsync(FormOf(bundle, tuvan), new([], tuvanSigner, representativeId), CancellationToken.None)).IsSuccess);
        foreach (var form in bundle.Forms)
        {
            var submitted = await s.Service.SubmitAsync(form.Id, CancellationToken.None);
            Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        }
    }

    [Fact]
    public async Task CreateBundle_MakesOneDraftPerTemplate_SharingTheBundleAndPrefilledFromThePatient()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();

        Assert.Equal(2, bundle.Forms.Count);
        Assert.All(bundle.Forms, f => Assert.Equal(ConsentFormStatus.Draft, f.Status));
        Assert.Equal(s.Patient.Id, bundle.PatientId);
        var stored = s.Ctx.Store.ConsentForms;
        Assert.All(stored, f => Assert.Equal(bundle.BundleId, f.BundleId));
        Assert.Equal(s.Kkb.Id, stored[0].DepartmentId);
        // BM02 điền sẵn số thẻ BHYT từ hồ sơ.
        var bm02Detail = await s.Service.GetAsync(FormOf(bundle, bm02), CancellationToken.None);
        Assert.Equal("GD4790000000125", bm02Detail.Value.Data["insuranceNumber"]?.GetValue<string>());
        Assert.Equal(bundle.BundleId, bm02Detail.Value.BundleId);
        Assert.True(bundle.Forms.Single(f => f.TemplateId == tuvan.Id).RequiresDecision);
        Assert.False(bundle.Forms.Single(f => f.TemplateId == bm02.Id).RequiresDecision);
        // Còn nháp: chưa mở phiên ký cả bộ được.
        Assert.False(bundle.CanStartSigning);
        Assert.Contains(bundle.Notices, n => n.StartsWith("Còn 2 phiếu nháp", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    public async Task CreateBundle_NeedsAtLeastTwoDifferentTemplates(int distinct)
    {
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        List<Guid> templateIds = distinct == 1 ? [s.Ck01.Id, s.Ck01.Id] : [s.Ck01.Id];

        var result = await s.Service.CreateBundleAsync(new(s.Patient.Id, templateIds, null), CancellationToken.None);

        Assert.True(result.Error!.FieldErrors!.ContainsKey("templateIds"));
        Assert.Empty(s.Ctx.Store.ConsentForms);
    }

    [Fact]
    public async Task CreateBundle_WithAnUnpublishedTemplate_CreatesNothing()
    {
        var s = ConsentScenario.Create();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);

        var result = await s.Service.CreateBundleAsync(new(s.Patient.Id, [s.Ck01.Id, Guid.NewGuid()], null), CancellationToken.None);

        Assert.True(result.Error!.FieldErrors!.ContainsKey("templateIds"));
        Assert.Empty(s.Ctx.Store.ConsentForms);
    }

    [Fact]
    public async Task PatientSignsOnce_ForTheWholeBundle_OneSignatureStatementAndTime_DecisionPerForm()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();
        await SubmitBoth(s, bundle, bm02, tuvan);
        Assert.True((await s.Service.GetBundleAsync(bundle.BundleId, CancellationToken.None)).Value.CanStartSigning);

        var session = await s.Signing.StartBundleSigningAsync(bundle.BundleId, CancellationToken.None);
        Assert.True(session.IsSuccess, session.Error?.Message);
        var claims = s.Ctx.Tokens.LastSigningClaims!;
        Assert.Equal(bundle.Forms.Select(f => f.Id).Order(), claims.FormIds.Order());
        Assert.All(s.Ctx.Store.ConsentForms, f => Assert.Equal(claims.SessionId, f.SigningSessionId));

        var staffLogsInAgain = s.UseSigningToken();
        var tuvanId = FormOf(bundle, tuvan);
        // Token của bộ phiếu không ký lẻ từng phiếu được.
        var single = await s.Signing.SignAsPatientAsync(tuvanId, new(ConsentDecision.Agreed, SignaturePng, Statement), CancellationToken.None);
        var signed = await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, Statement, new Dictionary<Guid, ConsentDecision> { [tuvanId] = ConsentDecision.Refused }),
            CancellationToken.None);
        var again = await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, Statement, new Dictionary<Guid, ConsentDecision> { [tuvanId] = ConsentDecision.Agreed }),
            CancellationToken.None);
        staffLogsInAgain();

        Assert.Equal(ErrorType.Forbidden, single.Error!.Type);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        Assert.Equal(2, signed.Value.Forms.Count);
        Assert.Equal("consent_forms.invalid_status", again.Error!.Code);

        var forms = s.Ctx.Store.ConsentForms;
        var signatures = forms.Select(f => f.PatientSignature!).ToList();
        Assert.Single(signatures.Select(x => x.SignedAtUtc).Distinct());
        Assert.All(signatures, x => Assert.Equal(Statement, x.Statement));
        Assert.Single(signatures.Select(x => Convert.ToBase64String(x.ImagePng!)).Distinct());
        Assert.Equal(ConsentDecision.Refused, forms.Single(f => f.Id == tuvanId).Decision);
        Assert.Null(forms.Single(f => f.Id == FormOf(bundle, bm02)).Decision);
        Assert.All(forms, f => Assert.Null(f.SigningSessionId));
        Assert.Equal(2, s.Ctx.Store.AuditLogs.Count(a =>
            a.Action == AuditActions.ConsentFormPatientSigned && a.Details!.Contains("signedTogether", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task BundleSignature_NeedsADecisionForEachFormThatAsks_AndTheStatement()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();
        await SubmitBoth(s, bundle, bm02, tuvan);
        await s.Signing.StartBundleSigningAsync(bundle.BundleId, CancellationToken.None);
        var staffLogsInAgain = s.UseSigningToken();

        var noDecision = await s.Signing.SignBundleAsPatientAsync(new(SignaturePng, Statement, null), CancellationToken.None);
        var noStatement = await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, "  ", new Dictionary<Guid, ConsentDecision> { [FormOf(bundle, tuvan)] = ConsentDecision.Agreed }),
            CancellationToken.None);
        var blank = await s.Signing.SignBundleAsPatientAsync(
            new(null, Statement, new Dictionary<Guid, ConsentDecision> { [FormOf(bundle, tuvan)] = ConsentDecision.Agreed }),
            CancellationToken.None);
        staffLogsInAgain();

        Assert.Contains("Phiếu tư vấn", noDecision.Error!.FieldErrors!["decisions"][0], StringComparison.Ordinal);
        Assert.Contains("Tôi đã nghe, hiểu và đồng ý", noStatement.Error!.FieldErrors!["statement"][0], StringComparison.Ordinal);
        Assert.True(blank.Error!.FieldErrors!.ContainsKey("image"));
        Assert.All(s.Ctx.Store.ConsentForms, f => Assert.Null(f.PatientSignature));
    }

    [Fact]
    public async Task BundleSigning_WithDifferentSigners_IsRefused()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();
        await SubmitBoth(s, bundle, bm02, tuvan, tuvanSigner: ConsentSignerKind.Representative);

        var view = await s.Service.GetBundleAsync(bundle.BundleId, CancellationToken.None);
        var session = await s.Signing.StartBundleSigningAsync(bundle.BundleId, CancellationToken.None);

        Assert.False(view.Value.CanStartSigning);
        Assert.Contains(view.Value.Notices, n => n.Contains("người ký khác nhau", StringComparison.Ordinal));
        Assert.Equal("consent_forms.bundle_mixed_signers", session.Error!.Code);
        Assert.All(s.Ctx.Store.ConsentForms, f => Assert.Null(f.SigningSessionId));
    }

    [Fact]
    public async Task ConfirmBundle_EachStaffConfirmsTheFormsOnTheirTurn_AndThePdfSaysSignedTogether()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();
        await SubmitBoth(s, bundle, bm02, tuvan);
        await s.Signing.StartBundleSigningAsync(bundle.BundleId, CancellationToken.None);
        var staffLogsInAgain = s.UseSigningToken();
        var tuvanId = FormOf(bundle, tuvan);
        await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, Statement, new Dictionary<Guid, ConsentDecision> { [tuvanId] = ConsentDecision.Agreed }),
            CancellationToken.None);
        staffLogsInAgain();

        // Tiếp đón: xác nhận cuối BM02 và bước "người tư vấn" của TUVAN trong một lần.
        var receptionist = await s.Signing.ConfirmBundleAsync(bundle.BundleId, new(SignaturePng), CancellationToken.None);
        var nothingLeft = await s.Signing.ConfirmBundleAsync(bundle.BundleId, new(SignaturePng), CancellationToken.None);
        // Điều dưỡng: xác nhận cuối TUVAN.
        s.LogInAs(s.Kkb, SystemRoles.Nurse);
        var nurse = await s.Signing.ConfirmBundleAsync(bundle.BundleId, new(SignaturePng), CancellationToken.None);

        Assert.True(receptionist.IsSuccess, receptionist.Error?.Message);
        Assert.Equal(
            [("completed", FormOf(bundle, bm02)), ("step", tuvanId)],
            receptionist.Value.Confirmed.Select(c => (c.Action, c.Id)).OrderBy(c => c.Action, StringComparer.Ordinal).ToList());
        Assert.Empty(receptionist.Value.Failed);
        Assert.Equal("consent_forms.bundle_nothing_to_confirm", nothingLeft.Error!.Code);
        Assert.True(nurse.IsSuccess, nurse.Error?.Message);
        Assert.Equal([("completed", tuvanId)], nurse.Value.Confirmed.Select(c => (c.Action, c.Id)).ToList());
        Assert.All(s.Ctx.Store.ConsentForms, f => Assert.Equal(ConsentFormStatus.Completed, f.Status));

        var formNos = string.Join(", ", s.Ctx.Store.ConsentForms.OrderBy(f => f.FormNo, StringComparer.Ordinal).Select(f => f.FormNo));
        Assert.Equal($"Ký chung một lần cho 2 phiếu: {formNos}", s.Ctx.Pdf.Last!.PatientSignature!.SignedTogether);
    }

    [Fact]
    public async Task Bundle_OutsideTheUsersScope_IsNotFound()
    {
        var (s, _, _, bundle) = await NewBundle();
        s.LogIn(s.Noi);

        var view = await s.Service.GetBundleAsync(bundle.BundleId, CancellationToken.None);
        var session = await s.Signing.StartBundleSigningAsync(bundle.BundleId, CancellationToken.None);
        var confirm = await s.Signing.ConfirmBundleAsync(bundle.BundleId, new(SignaturePng), CancellationToken.None);

        Assert.Equal("consent_forms.bundle_not_found", view.Error!.Code);
        Assert.Equal("consent_forms.bundle_not_found", session.Error!.Code);
        Assert.Equal("consent_forms.bundle_not_found", confirm.Error!.Code);
    }

    [Fact]
    public async Task SingleFormSignedAlone_PrintsNoSignedTogetherNote()
    {
        var (s, bm02, tuvan, bundle) = await NewBundle();
        await SubmitBoth(s, bundle, bm02, tuvan);
        var bm02Id = FormOf(bundle, bm02);
        await s.Signing.StartSigningAsync(bm02Id, CancellationToken.None);
        var staffLogsInAgain = s.UseSigningToken();
        await s.Signing.SignAsPatientAsync(bm02Id, new(null, SignaturePng, Statement), CancellationToken.None);
        staffLogsInAgain();

        var completed = await s.Signing.CompleteAsync(bm02Id, new(SignaturePng), CancellationToken.None);

        Assert.True(completed.IsSuccess, completed.Error?.Message);
        Assert.Null(s.Ctx.Pdf.Last!.PatientSignature!.SignedTogether);
    }
}
