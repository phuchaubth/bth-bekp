using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Nhiều bước xác nhận theo vai trò (<c>signatures.steps</c>): phiếu nhận định BM10 cần nhân viên hướng dẫn, điều dưỡng,
/// rồi bác sĩ xác nhận cuối. Dữ liệu giả lập.
/// </summary>
public sealed class ConfirmationStepTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    /// <summary>Phiếu BM10 đầy đủ, người lớn, không cấp cứu (giả lập).</summary>
    private static JsonObject Bm10Data() => new()
    {
        ["consultingRoom"] = "Phòng 05",
        ["selfFallHistory"] = "no",
        ["selfMobilityAid"] = "no",
        ["infectionSymptoms"] = "no",
        ["staffMobilityAid"] = "no",
        ["staffUnsteady"] = "no",
        ["pulse"] = "80",
        ["temperature"] = "37",
        ["bloodPressure"] = "120/80",
        ["respiratoryRate"] = "18",
        ["height"] = "160",
        ["weight"] = "55",
        ["bmi"] = "21,5",
        ["emergencySigns"] = "yes",
        ["emergencySignsDetail"] = "Khó thở",
        ["nurseEduFall"] = "notNeeded",
        ["nurseEduWound"] = "notNeeded",
        ["dietCounseling"] = "notRequired",
        ["physicianEduFall"] = "notRequired",
        ["physicianEduWound"] = "notRequired",
        ["physicianEduPain"] = "notRequired",
        ["physicianEduNewPatient"] = "notRequired",
        ["physicianEduOther"] = "notRequired",
    };

    /// <summary>Bác sĩ Khoa Khám bệnh lập phiếu BM10, trình ký, người bệnh ký trên máy tính bảng.</summary>
    private static async Task<(ConsentScenario S, Guid FormId)> PatientSignedBm10()
    {
        var s = ConsentScenario.Create();
        var template = s.AddHospitalTemplate("QT-KNGT-BM10");
        s.LogInAs(s.Kkb, SystemRoles.Doctor);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, template.Id, null), CancellationToken.None)).Value;
        var updated = await s.Service.UpdateAsync(form.Id, new(Bm10Data(), ConsentSignerKind.Patient, null), CancellationToken.None);
        Assert.True(updated.IsSuccess, updated.Error?.Message);
        var submitted = await s.Service.SubmitAsync(form.Id, CancellationToken.None);
        Assert.True(submitted.IsSuccess, string.Join("; ", submitted.Error?.FieldErrors?.Keys ?? []));
        Assert.True((await s.Signing.StartSigningAsync(form.Id, CancellationToken.None)).IsSuccess);
        var back = s.UseSigningToken();
        var signed = await s.Signing.SignAsPatientAsync(form.Id, new(null, SignaturePng), CancellationToken.None);
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        back();
        return (s, form.Id);
    }

    private static Task<Result<ConsentFormDetailDto>> Confirm(ConsentScenario s, Guid formId, string step) =>
        s.Signing.ConfirmStepAsync(formId, new ConfirmStepRequest(step, Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

    [Fact]
    public async Task Steps_AreConfirmedInOrder_ByTheirRoles_ThenTheDoctorCompletes()
    {
        var (s, formId) = await PatientSignedBm10();

        // Bác sĩ chưa hoàn tất được khi còn bước; điều dưỡng chưa đến lượt.
        var doctorTooEarly = await s.Signing.CompleteAsync(formId, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);
        s.LogInAs(s.Kkb, SystemRoles.Nurse);
        var nurseTooEarly = await Confirm(s, formId, "nurse");
        var nurseView = await s.Service.GetAsync(formId, CancellationToken.None);

        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var receptionView = await s.Service.GetAsync(formId, CancellationToken.None);
        var screening = await Confirm(s, formId, "screeningStaff");
        var screeningAgain = await Confirm(s, formId, "screeningStaff");

        var nurse = s.LogInAs(s.Kkb, SystemRoles.Nurse);
        var nursed = await Confirm(s, formId, "nurse");

        s.LogInAs(s.Kkb, SystemRoles.Doctor);
        var doctorView = await s.Service.GetAsync(formId, CancellationToken.None);
        var completed = await s.Signing.CompleteAsync(formId, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        Assert.Contains("Nhân viên bộ phận hướng dẫn", doctorTooEarly.Error!.Message, StringComparison.Ordinal);
        Assert.Equal("consent_forms.invalid_status", nurseTooEarly.Error!.Code);
        Assert.False(nurseView.Value.Actions.CanConfirmStep);
        Assert.True(receptionView.Value.Actions.CanConfirmStep);
        Assert.Equal(["screeningStaff", "nurse"], receptionView.Value.Steps.Select(st => st.Key));
        Assert.True(receptionView.Value.Steps[0].IsNext);
        Assert.Equal("Tiếp đón", receptionView.Value.Steps[0].RoleName);
        Assert.True(screening.IsSuccess, screening.Error?.Message);
        Assert.Equal("consent_forms.invalid_status", screeningAgain.Error!.Code);
        Assert.True(nursed.IsSuccess, nursed.Error?.Message);
        Assert.Equal(nurse.FullName, nursed.Value.Steps[1].Confirmation!.SignerName);
        Assert.True(doctorView.Value.Actions.CanConfirm);
        Assert.False(doctorView.Value.Actions.CanConfirmStep);

        Assert.True(completed.IsSuccess, completed.Error?.Message);
        Assert.Equal(ConsentFormStatus.Completed, completed.Value.Status);
        Assert.Equal(
            [ConsentSignerRole.Patient, ConsentSignerRole.Staff, ConsentSignerRole.Staff, ConsentSignerRole.Hospital],
            completed.Value.Signatures.Select(sig => sig.Role));
        Assert.Equal(2, s.Ctx.Store.AuditLogs.Count(a => a.Action == AuditActions.ConsentFormStepConfirmed));

        // PDF đã ký được dựng với đủ hai bước xác nhận.
        var pdf = s.Ctx.Pdf.Last!;
        Assert.True(pdf.IsFinal);
        Assert.Equal(2, pdf.StepConfirmations!.Count);
        Assert.All(pdf.StepConfirmations, c => Assert.NotNull(c));
    }

    [Fact]
    public async Task StepConfirmation_RequiresAHandwrittenSignature_PrintedInTheStepBox()
    {
        var (s, formId) = await PatientSignedBm10();
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);

        var missing = await s.Signing.ConfirmStepAsync(formId, new ConfirmStepRequest("screeningStaff"), CancellationToken.None);
        var blank = await s.Signing.ConfirmStepAsync(
            formId, new ConfirmStepRequest("screeningStaff", Convert.ToBase64String(TestPng.Blank())), CancellationToken.None);
        var confirmed = await Confirm(s, formId, "screeningStaff");
        s.LogInAs(s.Kkb, SystemRoles.Nurse);
        await Confirm(s, formId, "nurse");
        s.LogInAs(s.Kkb, SystemRoles.Doctor);
        await s.Signing.CompleteAsync(formId, new(SignaturePng), CancellationToken.None);

        Assert.Contains("image", missing.Error!.FieldErrors!.Keys);
        Assert.Equal("consent_forms.signature_blank", blank.Error!.Code);
        Assert.True(confirmed.IsSuccess, confirmed.Error?.Message);
        Assert.All(s.Ctx.Pdf.Last!.StepConfirmations!, c => Assert.NotNull(c!.ImagePng));
    }

    [Fact]
    public async Task WrongRole_IsForbidden_AndUnknownStepIsRejected()
    {
        var (s, formId) = await PatientSignedBm10();
        s.LogInAs(s.Kkb, SystemRoles.Nurse);

        var wrongRole = await Confirm(s, formId, "screeningStaff");
        var unknown = await Confirm(s, formId, "pharmacist");
        var missing = await Confirm(s, formId, " ");

        Assert.Equal(ErrorType.Forbidden, wrongRole.Error!.Type);
        Assert.Contains("Tiếp đón", wrongRole.Error.Message, StringComparison.Ordinal);
        Assert.Contains("stepKey", unknown.Error!.FieldErrors!.Keys);
        Assert.Contains("stepKey", missing.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task OneAccount_CannotConfirmTwoSteps_NorStepAndFinal()
    {
        var (s, formId) = await PatientSignedBm10();
        // Một tài khoản có cả ba vai trò (ví dụ cấu hình nhầm): vẫn chỉ xác nhận được một phần.
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        s.Ctx.CurrentUser.Roles.UnionWith([SystemRoles.Nurse, SystemRoles.Doctor]);

        var first = await Confirm(s, formId, "screeningStaff");
        var second = await Confirm(s, formId, "nurse");
        var view = await s.Service.GetAsync(formId, CancellationToken.None);

        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.Equal("consent_forms.same_confirmer", second.Error!.Code);
        Assert.False(view.Value.Actions.CanConfirmStep);

        s.LogInAs(s.Kkb, SystemRoles.Nurse);
        Assert.True((await Confirm(s, formId, "nurse")).IsSuccess);
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var receptionistUserId = s.Ctx.Store.ConsentForms.Single(f => f.Id == formId).StepSignatures.First().UserId!.Value;
        s.Ctx.CurrentUser.UserId = receptionistUserId;
        s.Ctx.CurrentUser.Roles.Add(SystemRoles.Doctor);
        var final = await s.Signing.CompleteAsync(formId, new(Convert.ToBase64String(TestPng.Signature())), CancellationToken.None);

        Assert.Equal("consent_forms.same_confirmer", final.Error!.Code);
    }

    [Fact]
    public async Task Steps_WaitForThePatientSignature_AndReturnToDraftClearsThem()
    {
        var s = ConsentScenario.Create();
        var template = s.AddHospitalTemplate("QT-KNGT-BM10");
        s.LogInAs(s.Kkb, SystemRoles.Doctor);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, template.Id, null), CancellationToken.None)).Value;
        await s.Service.UpdateAsync(form.Id, new(Bm10Data(), ConsentSignerKind.Patient, null), CancellationToken.None);
        await s.Service.SubmitAsync(form.Id, CancellationToken.None);
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);

        var beforePatient = await Confirm(s, form.Id, "screeningStaff");
        var view = await s.Service.GetAsync(form.Id, CancellationToken.None);

        Assert.Equal("consent_forms.invalid_status", beforePatient.Error!.Code);
        Assert.All(view.Value.Steps, st => Assert.False(st.IsNext));
    }

    [Fact]
    public async Task TemplateSteps_MustReferenceExistingRoles()
    {
        var s = ConsentScenario.Create();
        s.LogIn(s.Kkb, Domain.Authorization.Permissions.TemplatesManage);
        var service = s.Ctx.CreateConsentTemplateService();
        var template = s.Tn01;
        var draft = (await service.CreateDraftAsync(template.Id, CancellationToken.None)).Value;
        var definition = JsonNode.Parse(Consent.Application.ConsentTemplates.TemplateJson.Serialize(draft.Definition))!.AsObject();
        definition["signatures"] = new JsonObject
        {
            ["steps"] = new JsonArray(new JsonObject { ["key"] = "pharmacist", ["roleCode"] = "PHARMACIST", ["label"] = "Dược sĩ" }),
        };

        var result = await service.UpdateDraftAsync(
            template.Id,
            draft.VersionNo,
            new(System.Text.Json.JsonSerializer.SerializeToElement(definition)),
            CancellationToken.None);

        Assert.Equal("consent_templates.invalid_definition", result.Error!.Code);
        Assert.Contains("PHARMACIST", result.Error.FieldErrors!["definition"][0], StringComparison.Ordinal);
    }
}
