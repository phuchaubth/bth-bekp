using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;

namespace Consent.Application.Tests;

/// <summary>Kế thừa phiếu lần trước khi nhân viên lập phiếu, lập bộ phiếu (quyết định 27).</summary>
public sealed class FormInheritanceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";

    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>Tiếp đón Khoa Khám bệnh lập, cho ký và xác nhận một phiếu CK-01 của người bệnh; trả về số phiếu.</summary>
    private static async Task<string> CompletedCk01(ConsentScenario s, JsonObject? data = null)
    {
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;
        Assert.True((await s.Service.UpdateAsync(form.Id, new(data ?? ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), Ct)).IsSuccess);
        Assert.True((await s.Service.SubmitAsync(form.Id, Ct)).IsSuccess);
        Assert.True((await s.Signing.StartSigningAsync(form.Id, Ct)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        var signed = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, SignaturePng, Statement), Ct);
        staffLogsInAgain();
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        var completed = await s.Signing.CompleteAsync(form.Id, new(SignaturePng), Ct);
        Assert.True(completed.IsSuccess, completed.Error?.Message);
        return form.FormNo;
    }

    private static JsonObject Facility(string name)
    {
        var data = ConsentScenario.CompleteCk01();
        data["primaryCareFacility"] = name;
        return data;
    }

    [Fact]
    public async Task NewForm_IsFilledFromTheLatestCompletedFormOfTheSameTemplate_WithANoticeAndAnAuditEntry()
    {
        var s = ConsentScenario.Create();
        s.Ctx.SetInheritanceDays(30);
        await CompletedCk01(s, Facility("Trạm Y tế Phường Cũ"));
        s.Ctx.Clock.Advance(TimeSpan.FromDays(2));
        var latest = await CompletedCk01(s, Facility("Trạm Y tế Phường Mẫu"));
        s.Ctx.Clock.Advance(TimeSpan.FromDays(3));

        var created = await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct);

        Assert.True(created.IsSuccess, created.Error?.Message);
        var form = created.Value;
        Assert.Equal(ConsentFormStatus.Draft, form.Status);
        Assert.Equal("Trạm Y tế Phường Mẫu", form.Data["primaryCareFacility"]!.GetValue<string>());
        Assert.Equal("bhyt", form.Data["examType"]!.GetValue<string>());
        Assert.Equal("no", form.Data["referralPaper"]!.GetValue<string>());
        // Ý kiến và chữ ký không kế thừa: phiếu mới chưa có.
        Assert.Null(form.Decision);
        Assert.Empty(form.Signatures);
        var notice = Assert.Single(form.Notices, n => n.Contains(latest, StringComparison.Ordinal));
        Assert.Contains("kiểm tra lại trước khi trình ký", notice, StringComparison.Ordinal);
        // Phiếu thứ hai lúc lập cũng đã kế thừa phiếu thứ nhất; phiếu mới ghi nhật ký nguồn là phiếu gần nhất.
        var audit = Assert.Single(
            s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormInherited && a.EntityId == form.Id.ToString());
        Assert.Contains(latest, audit.Details, StringComparison.Ordinal);

        // Mở lại phiếu (tải lại trang): nội dung đã lưu, lưu ý vẫn hiện trên phiếu nháp đến khi trình ký.
        var reopened = (await s.Service.GetAsync(form.Id, Ct)).Value;
        Assert.Equal("Trạm Y tế Phường Mẫu", reopened.Data["primaryCareFacility"]!.GetValue<string>());
        Assert.Equal(notice, Assert.Single(reopened.Notices, n => n.Contains(latest, StringComparison.Ordinal)));
        var submitted = await s.Service.SubmitAsync(form.Id, Ct);
        Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        Assert.DoesNotContain(submitted.Value.Notices, n => n.Contains(latest, StringComparison.Ordinal));
    }

    [Fact]
    public async Task NothingIsInherited_WhenTurnedOff_OrTheFormIsOlderThanTheDays()
    {
        var s = ConsentScenario.Create();
        await CompletedCk01(s, Facility("Trạm Y tế Phường Mẫu"));

        var off = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;
        s.Ctx.SetInheritanceDays(7);
        s.Ctx.Clock.Advance(TimeSpan.FromDays(8));
        var tooOld = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;

        Assert.False(off.Data.ContainsKey("primaryCareFacility"));
        Assert.False(tooOld.Data.ContainsKey("primaryCareFacility"));
        Assert.DoesNotContain(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormInherited);
    }

    [Fact]
    public async Task OnlyCompletedFormsOfTheSameDepartment_AreInherited()
    {
        var s = ConsentScenario.Create();
        s.Ctx.SetInheritanceDays(30);
        await CompletedCk01(s, Facility("Trạm Y tế Phường Mẫu"));

        // Phiếu mới hơn nhưng còn nháp (chưa hoàn tất) không được dùng.
        var draft = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;
        Assert.True((await s.Service.UpdateAsync(draft.Id, new(Facility("Phường Nháp"), ConsentSignerKind.Patient, null), Ct)).IsSuccess);
        var fromKkb = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), Ct)).Value;

        // Khoa khác không thấy nội dung phiếu của Khoa Khám bệnh.
        s.LogIn(s.Noi);
        var fromNoi = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, s.Noi.Id), Ct)).Value;

        Assert.Equal("Trạm Y tế Phường Mẫu", fromKkb.Data["primaryCareFacility"]!.GetValue<string>());
        Assert.False(fromNoi.Data.ContainsKey("primaryCareFacility"));
    }

    [Fact]
    public async Task Bundle_FillsEachFormThatHasAPreviousOne_AndListsWhichOnes()
    {
        var s = ConsentScenario.Create();
        s.Ctx.SetInheritanceDays(30);
        var previous = await CompletedCk01(s, Facility("Trạm Y tế Phường Mẫu"));

        var bundle = await s.Service.CreateBundleAsync(new(s.Patient.Id, [s.Ck01.Id, s.Tn01.Id], null), Ct);

        Assert.True(bundle.IsSuccess, bundle.Error?.Message);
        var ck01 = bundle.Value.Forms.Single(f => f.TemplateId == s.Ck01.Id);
        var stored = JsonNode.Parse(s.Ctx.Store.ConsentForms.Single(f => f.Id == ck01.Id).DataJson)!;
        Assert.Equal("Trạm Y tế Phường Mẫu", stored["primaryCareFacility"]!.GetValue<string>());
        var notice = Assert.Single(bundle.Value.Notices, n => n.Contains(previous, StringComparison.Ordinal));
        Assert.StartsWith(ck01.FormNo, notice, StringComparison.Ordinal);
        Assert.Single(s.Ctx.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormInherited);

        // Mở lại bộ phiếu và từng phiếu: lưu ý vẫn còn trên thanh bộ phiếu và trên trang phiếu CK-01 nháp.
        var reloaded = await s.Service.GetBundleAsync(bundle.Value.BundleId, Ct);
        Assert.Equal(notice, Assert.Single(reloaded.Value.Notices, n => n.Contains(previous, StringComparison.Ordinal)));
        var ck01Page = (await s.Service.GetAsync(ck01.Id, Ct)).Value;
        Assert.Single(ck01Page.Notices, n => n.Contains(previous, StringComparison.Ordinal));
        var tn01Page = (await s.Service.GetAsync(bundle.Value.Forms.Single(f => f.TemplateId == s.Tn01.Id).Id, Ct)).Value;
        Assert.DoesNotContain(tn01Page.Notices, n => n.Contains(previous, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Settings_AcceptZeroToAYear_AndSwitchOffWithZero()
    {
        var s = ConsentScenario.Create();
        var service = s.Ctx.CreateFormInheritanceService();

        var initial = await service.GetAsync(Ct);
        var negative = await service.UpdateAsync(new(-1), Ct);
        var tooMany = await service.UpdateAsync(new(366), Ct);
        var missing = await service.UpdateAsync(new(null), Ct);
        var saved = await service.UpdateAsync(new(30), Ct);
        var days = await service.GetDaysAsync(Ct);
        var off = await service.UpdateAsync(new(0), Ct);

        Assert.Equal(new FormInheritanceSettingsDto(0, null), initial);
        Assert.All([negative, tooMany, missing], r => Assert.Contains("days", r.Error!.FieldErrors!.Keys));
        Assert.Equal(30, saved.Value.Days);
        Assert.Equal(s.Ctx.Clock.UtcNow, saved.Value.UpdatedAtUtc);
        Assert.Equal(30, days);
        Assert.Equal(0, off.Value.Days);
        Assert.Equal(0, await service.GetDaysAsync(Ct));
    }
}
