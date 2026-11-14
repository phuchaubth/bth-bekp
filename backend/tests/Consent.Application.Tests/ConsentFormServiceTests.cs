using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Departments;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class ConsentFormServiceTests
{
    private static ConsentScenario Setup() => ConsentScenario.Create();

    private static JsonObject CompleteCk01() => ConsentScenario.CompleteCk01();

    [Fact]
    public async Task Create_UsesPublishedVersion_NumbersTheForm_AndPrefillsFromPatient()
    {
        var s = Setup();
        s.LogIn(s.Kkb);

        var result = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var form = result.Value;
        Assert.Equal(ConsentFormStatus.Draft, form.Status);
        Assert.Matches("^PDT-2026-\\d{6}$", form.FormNo);
        Assert.Equal(s.Ck01.PublishedVersion!.Id, form.TemplateVersionId);
        Assert.Equal(s.Kkb.Id, form.DepartmentId);
        Assert.Equal("GD4790000000125", form.Data["insuranceNumber"]!.GetValue<string>());
        Assert.True(form.Actions.CanEdit);
    }

    [Fact]
    public async Task Draft_ForPatientWithLegacyInsuranceNumber_CarriesNotice_UntilSubmitted()
    {
        var s = Setup();
        s.LogIn(s.Kkb);

        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        await s.Service.UpdateAsync(form.Id, new(CompleteCk01(), ConsentSignerKind.Patient, null), CancellationToken.None);
        var submitted = await s.Service.SubmitAsync(form.Id, CancellationToken.None);

        Assert.Equal([HealthInsuranceNumber.LegacyNotice], form.Notices);
        Assert.Empty(submitted.Value.Notices);
    }

    [Fact]
    public async Task Draft_ForPatientWithNewInsuranceNumber_HasNoNotice()
    {
        var s = Setup();
        s.Patient.Update(
            new PatientDetails(
                "Phạm Thị Dung (giả lập)", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", null, null,
                "Số 9 Đường Thử Nghiệm", null, "0900000125", "Kinh", null, "Việt Nam", "GD479036188000125", "BN000125"),
            s.Ctx.Clock.UtcNow);
        s.LogIn(s.Kkb);

        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        Assert.Equal("GD479036188000125", form.Data["insuranceNumber"]!.GetValue<string>());
        Assert.Empty(form.Notices);
    }

    [Fact]
    public async Task Create_OutpatientInformation_PrefillsEthnicityOccupationAndAddresses()
    {
        var s = Setup();
        var template = s.AddHospitalTemplate("TTNB-NGOAITRU");
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);

        var form = (await s.Service.CreateAsync(new(s.Patient.Id, template.Id, null), CancellationToken.None)).Value;

        Assert.Equal("Kinh", form.Data["ethnicity"]!.GetValue<string>());
        Assert.Equal("Số 9 Đường Thử Nghiệm", form.Data["permanentAddress"]!.GetValue<string>());
        // Chưa có chỗ ở hiện nay: dùng địa chỉ thường trú.
        Assert.Equal("Số 9 Đường Thử Nghiệm", form.Data["currentAddress"]!.GetValue<string>());
        // Hồ sơ chưa có nghề nghiệp: để trống cho người khai điền.
        Assert.Null(form.Data["occupation"]);
    }

    [Fact]
    public async Task Form_KeepsItsTemplateVersion_WhenANewVersionIsPublished()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var version1 = s.Ck01.PublishedVersion!;
        var before = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None);

        // Phiên bản 2 đổi tiêu đề rồi được phát hành (phiên bản 1 chuyển sang ngừng dùng).
        var definition = JsonNode.Parse(version1.ContentJson)!.AsObject();
        definition["title"] = "Giấy cam kết (phiên bản 2)";
        var draft = s.Ck01.CreateDraftFromLatest(s.Ctx.Clock.UtcNow);
        s.Ck01.UpdateDraft(draft.VersionNo, definition.ToJsonString(), s.Ctx.Clock.UtcNow);
        s.Ck01.Publish(draft.VersionNo, s.Ctx.Clock.UtcNow);

        var reopened = await s.Service.GetAsync(before.Value.Id, CancellationToken.None);
        var after = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None);

        Assert.Equal(TemplateVersionStatus.Retired, version1.Status);
        Assert.Equal(version1.Id, reopened.Value.TemplateVersionId);
        Assert.Equal(1, reopened.Value.TemplateVersionNo);
        Assert.NotEqual("Giấy cam kết (phiên bản 2)", reopened.Value.Definition.Title);
        Assert.Equal(2, after.Value.TemplateVersionNo);
        Assert.Equal("Giấy cam kết (phiên bản 2)", after.Value.Definition.Title);
    }

    [Fact]
    public async Task Create_ForAnotherDepartment_IsForbidden_UnlessHospitalWideScope()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var forbidden = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, s.Noi.Id), CancellationToken.None);

        s.LogIn(s.Kkb, Permissions.ScopeAllDepartments);
        var allowed = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, s.Noi.Id), CancellationToken.None);

        Assert.Equal(ErrorType.Forbidden, forbidden.Error!.Type);
        Assert.Equal(s.Noi.Id, allowed.Value.DepartmentId);
    }

    [Fact]
    public async Task Forms_OfOtherDepartments_AreInvisible()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        s.LogIn(s.Noi);
        var fromOtherDepartment = await s.Service.GetAsync(form.Id, CancellationToken.None);
        var listOther = await s.Service.SearchAsync(new ConsentFormQuery(), CancellationToken.None);

        s.LogIn(s.Noi, Permissions.ScopeAllDepartments);
        var hospitalWide = await s.Service.GetAsync(form.Id, CancellationToken.None);
        var listAll = await s.Service.SearchAsync(new ConsentFormQuery(), CancellationToken.None);

        Assert.Equal(ErrorType.NotFound, fromOtherDepartment.Error!.Type);
        Assert.Equal(0, listOther.TotalCount);
        Assert.True(hospitalWide.IsSuccess);
        Assert.Equal(1, listAll.TotalCount);
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormViewed && a.EntityId == form.Id.ToString());
    }

    [Fact]
    public async Task Submit_Incomplete_ReturnsFieldErrors_AndStaysDraft()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        var result = await s.Service.SubmitAsync(form.Id, CancellationToken.None);

        Assert.Equal("consent_forms.incomplete", result.Error!.Code);
        Assert.Contains("examType", result.Error!.FieldErrors!.Keys);
        Assert.Contains("acknowledged", result.Error!.FieldErrors!.Keys);
        Assert.Equal(ConsentFormStatus.Draft, s.Ctx.Store.ConsentForms.Single().Status);
    }

    [Fact]
    public async Task Submit_Complete_FreezesPatientInfo_ThenOnlyReturnOrCancelAllowed()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        await service.UpdateAsync(form.Id, new(CompleteCk01(), ConsentSignerKind.Patient, null), CancellationToken.None);

        var submitted = await service.SubmitAsync(form.Id, CancellationToken.None);
        // Hồ sơ đổi sau khi trình ký không làm thay đổi nội dung đem ký.
        s.Patient.Update(
            new PatientDetails(
                "Tên đã đổi (giả lập)", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", null, null,
                "Địa chỉ mới", null, "0900000125", "Kinh", null, "Việt Nam", "GD4790000000125", "BN000125"),
            s.Ctx.Clock.UtcNow);
        var reloaded = await service.GetAsync(form.Id, CancellationToken.None);
        var edit = await service.UpdateAsync(form.Id, new(CompleteCk01(), ConsentSignerKind.Patient, null), CancellationToken.None);

        Assert.Equal(ConsentFormStatus.PendingSignature, submitted.Value.Status);
        Assert.Equal("Phạm Thị Dung (giả lập)", reloaded.Value.Patient.FullName);
        Assert.Equal(
            new ConsentFormActions(false, false, true, true, CanStartSigning: true, CanConfirmStep: false, CanConfirm: false, CanRevoke: false, CanReplace: false),
            reloaded.Value.Actions);
        Assert.Equal("consent_forms.invalid_status", edit.Error!.Code);

        var back = await service.ReturnToDraftAsync(form.Id, CancellationToken.None);
        Assert.Equal(ConsentFormStatus.Draft, back.Value.Status);
        Assert.Equal("Tên đã đổi (giả lập)", back.Value.Patient.FullName);
    }

    [Fact]
    public async Task Representative_MustBelongToPatient_AndIsShownAsSigner()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        var stranger = await service.UpdateAsync(
            form.Id, new(CompleteCk01(), ConsentSignerKind.Representative, Guid.NewGuid()), CancellationToken.None);
        var spouse = await service.UpdateAsync(
            form.Id, new(CompleteCk01(), ConsentSignerKind.Representative, s.Patient.Representatives.Single().Id), CancellationToken.None);

        Assert.Contains("representativeId", stranger.Error!.FieldErrors!.Keys);
        Assert.Equal("Phạm Văn Chồng (giả lập)", spouse.Value.Representative!.FullName);
        Assert.Equal(RepresentativeRelationship.Spouse, spouse.Value.Representative.Relationship);
    }

    [Fact]
    public async Task Draft_ShowsMorseScore_AndIssues()
    {
        var s = Setup();
        s.LogIn(s.Noi);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Tn01.Id, null), CancellationToken.None)).Value;
        var data = new JsonObject
        {
            ["assessmentReason"] = "initial",
            ["morseFallHistory"] = "yes",
            ["morseSecondaryDiagnosis"] = "yes",
            ["morseAmbulatoryAid"] = "crutches",
            ["morseIntravenous"] = "no",
            ["morseGait"] = "normal",
            ["morseMentalStatus"] = "oriented",
            ["measuresStandard"] = new JsonArray("callBell", "lowBed"),
        };

        var saved = await service.UpdateAsync(form.Id, new(data, ConsentSignerKind.Patient, null), CancellationToken.None);

        Assert.Equal(55, saved.Value.Scores["morseTotal"].Total);
        Assert.Equal("Nguy cơ cao", saved.Value.Scores["morseTotal"].LevelLabel);
        Assert.Equal(["measuresHigh", "measuresMedium", "reassessAt"], saved.Value.Issues.Keys.Order());
    }

    [Fact]
    public async Task Cancel_RequiresReason()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        var noReason = await service.CancelAsync(form.Id, new(" "), CancellationToken.None);
        var cancelled = await service.CancelAsync(form.Id, new("Lập nhầm mẫu phiếu"), CancellationToken.None);
        var again = await service.CancelAsync(form.Id, new("Lập nhầm mẫu phiếu"), CancellationToken.None);

        Assert.Contains("reason", noReason.Error!.FieldErrors!.Keys);
        Assert.Equal(ConsentFormStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal("Lập nhầm mẫu phiếu", cancelled.Value.CancelReason);
        Assert.Equal("consent_forms.invalid_status", again.Error!.Code);
    }

    [Fact]
    public async Task Create_WithUnpublishedTemplate_IsRejected()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        s.Ck01.Retire(1, s.Ctx.Clock.UtcNow);

        var result = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None);

        Assert.Contains("templateId", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Submit_KeepsOnlyVisibleContent()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        var data = new JsonObject { ["examType"] = "service", ["insuranceNumber"] = "GD4790000000125", ["acknowledged"] = true };

        var draft = await service.UpdateAsync(form.Id, new(data, ConsentSignerKind.Patient, null), CancellationToken.None);
        var submitted = await service.SubmitAsync(form.Id, CancellationToken.None);

        Assert.NotNull(draft.Value.Data["insuranceNumber"]);
        Assert.Null(submitted.Value.Data["insuranceNumber"]);
        Assert.Equal("service", submitted.Value.Data["examType"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pdf_IsPreviewUntilCompleted_PrintsOnlyVisibleFields_AndIsAudited()
    {
        var s = Setup();
        s.LogIn(s.Kkb);
        var service = s.Service;
        var form = (await service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;
        var data = new JsonObject { ["examType"] = "service", ["insuranceNumber"] = "GD4790000000125", ["acknowledged"] = true };
        await service.UpdateAsync(form.Id, new(data, ConsentSignerKind.Patient, null), CancellationToken.None);

        var pdf = await service.GetPdfAsync(form.Id, CancellationToken.None);

        Assert.Equal($"{form.FormNo}.pdf", pdf.Value.FileName);
        var printed = s.Ctx.Pdf.Last!;
        Assert.False(printed.IsFinal);
        Assert.Null(printed.Data["insuranceNumber"]);
        Assert.Equal("CK-01", printed.TemplateCode);
        Assert.Equal("Khoa Khám bệnh", printed.DepartmentName);
        Assert.Contains(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormPdfViewed);
    }
}
