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
    private sealed class Scenario
    {
        public required AppTestContext Ctx { get; init; }

        public required Department Noi { get; init; }

        public required Department Kkb { get; init; }

        public required Patient Patient { get; init; }

        public required ConsentTemplate Ck01 { get; init; }

        public required ConsentTemplate Tn01 { get; init; }

        public ConsentFormService Service => Ctx.CreateConsentFormService();

        /// <summary>Đăng nhập như một nhân viên của khoa <paramref name="department"/>.</summary>
        public User LogIn(Department department, params string[] extraPermissions)
        {
            var user = Ctx.AddUser($"nv{Ctx.Store.Users.Count}", SystemRoles.Nurse, departmentId: department.Id);
            Ctx.CurrentUser.UserId = user.Id;
            Ctx.CurrentUser.UserName = user.UserName;
            Ctx.CurrentUser.DepartmentId = department.Id;
            Ctx.CurrentUser.Permissions.Clear();
            Ctx.CurrentUser.Permissions.UnionWith([Permissions.ConsentsRead, Permissions.ConsentsCreate, .. extraPermissions]);
            return user;
        }
    }

    private static Scenario Setup()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội tổng hợp");
        var kkb = ctx.AddDepartment("KKB", "Khoa Khám bệnh");
        var patient = Patient.Create(
            new PatientDetails(
                "Phạm Thị Dung (giả lập)", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", null, null,
                "Số 9 Đường Thử Nghiệm", null, "0900000125", "Kinh", null, "Việt Nam", "GD4790000000125", "BN000125"),
            ctx.Clock.UtcNow);
        patient.AddRepresentative(
            new RepresentativeDetails("Phạm Văn Chồng (giả lập)", RepresentativeRelationship.Spouse, "036085000777", null, Gender.Male, null, null),
            ctx.Clock.UtcNow);
        ctx.Store.Patients.Add(patient);

        ConsentTemplate Seed(string code)
        {
            var source = DefaultConsentTemplates.All.Single(t => t.Code == code);
            var template = ConsentTemplate.Create(code, source.Name, source.HospitalSignerRoleCode, null, source.ContentJson, ctx.Clock.UtcNow);
            template.Publish(1, ctx.Clock.UtcNow);
            ctx.Store.ConsentTemplates.Add(template);
            return template;
        }

        return new Scenario { Ctx = ctx, Noi = noi, Kkb = kkb, Patient = patient, Ck01 = Seed("CK-01"), Tn01 = Seed("TN-01") };
    }

    private static JsonObject CompleteCk01() => new()
    {
        ["examType"] = "bhyt",
        ["insuranceNumber"] = "GD4790000000125",
        ["insuranceValidFrom"] = "2026-01-01",
        ["insuranceValidTo"] = "2026-12-31",
        ["primaryCareFacility"] = "Trạm Y tế Phường Mẫu",
        ["referralPaper"] = "no",
        ["acknowledged"] = true,
    };

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
        Assert.Equal(new ConsentFormActions(false, false, true, true), reloaded.Value.Actions);
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
}
