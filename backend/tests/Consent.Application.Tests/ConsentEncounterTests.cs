using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Auditing;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;
using Consent.Domain.Patients;

namespace Consent.Application.Tests;

/// <summary>Gắn phiếu với lượt tiếp nhận trên HIS khi phía bệnh viện ký hoàn tất (quyết định 25).</summary>
public sealed class ConsentEncounterTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";

    /// <summary>Hôm nay theo giờ bệnh viện (đồng hồ giả lập: 08:00 UTC ngày 11/11/2026, tức 15:00 giờ Việt Nam).</summary>
    private static readonly DateOnly Today = new(2026, 11, 11);

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static DateTime At(DateOnly day, int hour, int minute) => day.ToDateTime(new TimeOnly(hour, minute));

    private static DateTimeOffset HospitalTime(DateOnly day, int hour, int minute) => new(At(day, hour, minute), TimeSpan.FromHours(7));

    /// <summary>Bật gắn lượt tiếp nhận; người bệnh của kịch bản (mã y tế BN000125) có hai lượt hôm nay và một lượt hôm qua.</summary>
    private static ConsentScenario Scenario()
    {
        var s = ConsentScenario.Create();
        s.Ctx.EncounterSource.IsEnabled = true;
        s.Ctx.EncounterSource.Add("BN000125", "TN-0001", At(Today, 7, 30), "Phòng khám 1", "Nguyễn Tiếp Đón (giả lập)");
        s.Ctx.EncounterSource.Add("BN000125", "TN-0002", At(Today, 10, 45), "Phòng khám 3", "Trần Tiếp Đón (giả lập)");
        s.Ctx.EncounterSource.Add("BN000125", "TN-0000", At(Today.AddDays(-1), 9, 0), "Khoa Cấp cứu", null);
        return s;
    }

    /// <summary>Tiếp đón lập và trình ký một phiếu CK-01; người bệnh đã ký, chờ tiếp đón xác nhận cuối.</summary>
    private static async Task<Guid> PatientSignedCk01(ConsentScenario s, Patient? patient = null)
    {
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new((patient ?? s.Patient).Id, s.Ck01.Id, null), Ct)).Value;
        await s.Service.UpdateAsync(form.Id, new(ConsentScenario.CompleteCk01(), ConsentSignerKind.Patient, null), Ct);
        Assert.True((await s.Service.SubmitAsync(form.Id, Ct)).IsSuccess);
        Assert.True((await s.Signing.StartSigningAsync(form.Id, Ct)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        var signed = await s.Signing.SignAsPatientAsync(form.Id, new(ConsentDecision.Agreed, SignaturePng, Statement), Ct);
        staffLogsInAgain();
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        return form.Id;
    }

    /// <summary>
    /// Bộ BM02 + TUVAN-CONGTT của Khoa Khám bệnh, người bệnh đã ký một lần cho cả bộ. Tiếp đón xác nhận cuối BM02 và bước
    /// "người tư vấn" của TUVAN; điều dưỡng xác nhận cuối TUVAN.
    /// </summary>
    private static async Task<(Guid BundleId, Guid Bm02, Guid Tuvan)> PatientSignedBundle(ConsentScenario s)
    {
        var bm02Template = s.AddHospitalTemplate("QT-KNGT-BM02");
        var tuvanTemplate = s.AddHospitalTemplate("TUVAN-CONGTT");
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var bundle = (await s.Service.CreateBundleAsync(new(s.Patient.Id, [bm02Template.Id, tuvanTemplate.Id], null), Ct)).Value;
        var bm02 = bundle.Forms.Single(f => f.TemplateId == bm02Template.Id).Id;
        var tuvan = bundle.Forms.Single(f => f.TemplateId == tuvanTemplate.Id).Id;
        Assert.True((await s.Service.UpdateAsync(
            bm02,
            new(new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" }, ConsentSignerKind.Patient, null),
            Ct)).IsSuccess);
        Assert.True((await s.Service.UpdateAsync(tuvan, new([], ConsentSignerKind.Patient, null), Ct)).IsSuccess);
        foreach (var form in bundle.Forms)
        {
            Assert.True((await s.Service.SubmitAsync(form.Id, Ct)).IsSuccess);
        }

        Assert.True((await s.Signing.StartBundleSigningAsync(bundle.BundleId, Ct)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        var signed = await s.Signing.SignBundleAsPatientAsync(
            new(SignaturePng, Statement, new Dictionary<Guid, ConsentDecision> { [tuvan] = ConsentDecision.Agreed }), Ct);
        staffLogsInAgain();
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        return (bundle.BundleId, bm02, tuvan);
    }

    [Fact]
    public async Task Complete_WhenEnabled_RequiresAnEncounter_AndChangesNothingWithoutOne()
    {
        var s = Scenario();
        var formId = await PatientSignedCk01(s);

        var missing = await s.Signing.CompleteAsync(formId, new(SignaturePng), Ct);
        var noDate = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-0002"), Ct);
        var blank = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "  ", EncounterDate: Today), Ct);

        Assert.All([missing, noDate, blank], r => Assert.True(r.Error!.FieldErrors!.ContainsKey("encounterNo")));
        Assert.Empty(s.Ctx.EncounterSource.Calls);
        var form = s.Ctx.Store.ConsentForms.Single();
        Assert.Equal(ConsentFormStatus.PendingSignature, form.Status);
        Assert.Null(form.HospitalSignature);
        Assert.Empty(s.Ctx.Documents.Files);
    }

    [Fact]
    public async Task Complete_AttachesTheChosenEncounter_AfterCheckingHis_AndPrintsItOnThePdf()
    {
        var s = Scenario();
        var formId = await PatientSignedCk01(s);

        var completed = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: " tn-0002 ", EncounterDate: Today), Ct);

        Assert.True(completed.IsSuccess, completed.Error?.Message);
        var receivedAt = HospitalTime(Today, 10, 45);
        Assert.Equal(new ConsentFormEncounterDto("TN-0002", receivedAt, "Phòng khám 3", "Trần Tiếp Đón (giả lập)"), completed.Value.Encounter);
        var form = s.Ctx.Store.ConsentForms.Single();
        Assert.Equal(ConsentFormStatus.Completed, form.Status);
        Assert.Equal(
            ("TN-0002", (DateTimeOffset?)receivedAt, "Phòng khám 3", "Trần Tiếp Đón (giả lập)"),
            (form.EncounterNo, form.EncounterReceivedAt, form.EncounterPlace, form.EncounterReceivedBy));
        Assert.Equal(TimeSpan.FromHours(7), form.EncounterReceivedAt!.Value.Offset);
        // Chân trang PDF: số tiếp nhận và người tiếp nhận để đối soát.
        var printed = s.Ctx.Pdf.Last!.Encounter!;
        Assert.Equal(("TN-0002", "Phòng khám 3", "Trần Tiếp Đón (giả lập)"), (printed.Number, printed.Place, printed.ReceivedBy));
        // Máy chủ tra lại HIS theo mã y tế của người bệnh, đúng ngày đã chọn.
        Assert.Equal(("BN000125", (string?)null, Today, Today), Assert.Single(s.Ctx.EncounterSource.Calls));
        Assert.Contains(s.Ctx.Store.AuditLogs, a =>
            a.Action == AuditActions.ConsentFormCompleted && a.Details!.Contains("\"encounter\":\"TN-0002\"", StringComparison.Ordinal));
        // Phiếu đã hoàn tất thì vẫn hiện lượt tiếp nhận khi mở lại.
        Assert.Equal("TN-0002", (await s.Service.GetAsync(formId, Ct)).Value.Encounter!.EncounterNo);
    }

    [Fact]
    public async Task Complete_WithAnEncounterOfAnotherDayOrAnotherPatient_IsRefused()
    {
        var s = Scenario();
        s.Ctx.EncounterSource.Add("BN-KHAC", "TN-9999", At(Today, 8, 0), "Phòng khám 2", null);
        var formId = await PatientSignedCk01(s);

        // TN-0000 có thật nhưng là lượt hôm qua; TN-9999 là lượt hôm nay của người bệnh khác.
        var wrongDay = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-0000", EncounterDate: Today), Ct);
        var otherPatient = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-9999", EncounterDate: Today), Ct);

        Assert.Contains("không thuộc người bệnh này", wrongDay.Error!.FieldErrors!["encounterNo"][0], StringComparison.Ordinal);
        Assert.True(otherPatient.Error!.FieldErrors!.ContainsKey("encounterNo"));
        var form = s.Ctx.Store.ConsentForms.Single();
        Assert.Equal(ConsentFormStatus.PendingSignature, form.Status);
        Assert.Null(form.EncounterNo);
        Assert.Empty(s.Ctx.Documents.Files);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Complete_WhenHisCannotBeReached_IsRefused_AndTheFormStaysPending(bool throws)
    {
        var s = Scenario();
        var formId = await PatientSignedCk01(s);
        s.Ctx.EncounterSource.Unavailable = !throws;
        s.Ctx.EncounterSource.Throws = throws;

        var result = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-0002", EncounterDate: Today), Ct);

        Assert.Equal("consent_forms.encounters_unavailable", result.Error!.Code);
        Assert.Equal(ConsentFormStatus.PendingSignature, s.Ctx.Store.ConsentForms.Single().Status);
    }

    [Fact]
    public async Task Complete_ByStaffWhoMayNotConfirm_IsRefusedBeforeAskingHis()
    {
        var s = Scenario();
        var formId = await PatientSignedCk01(s);
        // CK-01 do tiếp đón xác nhận cuối; điều dưỡng không xác nhận được.
        s.LogInAs(s.Kkb, SystemRoles.Nurse);

        var result = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-0002", EncounterDate: Today), Ct);

        Assert.True(result.IsFailure);
        Assert.Empty(s.Ctx.EncounterSource.Calls);
    }

    [Fact]
    public async Task Complete_WhenTheHospitalHasNotEnabledIt_WorksAsBefore()
    {
        var s = ConsentScenario.Create();
        var formId = await PatientSignedCk01(s);

        var completed = await s.Signing.CompleteAsync(formId, new(SignaturePng, EncounterNo: "TN-0002", EncounterDate: Today), Ct);

        Assert.True(completed.IsSuccess, completed.Error?.Message);
        Assert.Null(completed.Value.Encounter);
        Assert.Null(s.Ctx.Store.ConsentForms.Single().EncounterNo);
        Assert.Null(s.Ctx.Pdf.Last!.Encounter);
        Assert.Empty(s.Ctx.EncounterSource.Calls);
        Assert.Equal(
            "consent_forms.encounters_disabled",
            (await s.Ctx.CreateConsentEncounterService().ListForFormAsync(formId, null, null, Ct)).Error!.Code);
    }

    [Fact]
    public async Task List_DefaultsToToday_NewestFirst_WithTheFormsAlreadyAttached()
    {
        var s = Scenario();
        // Số tiếp nhận dài bất thường trên HIS không gắn được vào phiếu: không đưa lên danh sách.
        s.Ctx.EncounterSource.Add("BN000125", new string('9', ConsentEncounter.NumberMaxLength + 1), At(Today, 11, 0));
        var first = await PatientSignedCk01(s);
        Assert.True((await s.Signing.CompleteAsync(first, new(SignaturePng, EncounterNo: "TN-0001", EncounterDate: Today), Ct)).IsSuccess);
        var second = await PatientSignedCk01(s);

        var list = await s.Ctx.CreateConsentEncounterService().ListForFormAsync(second, null, null, Ct);

        Assert.True(list.IsSuccess, list.Error?.Message);
        Assert.Equal((Today, Today), (list.Value.From, list.Value.To));
        Assert.Equal(["TN-0002", "TN-0001"], list.Value.Items.Select(i => i.EncounterNo).ToList());
        var latest = list.Value.Items[0];
        Assert.Equal(HospitalTime(Today, 10, 45), latest.ReceivedAt);
        Assert.Equal(TimeSpan.FromHours(7), latest.ReceivedAt.Offset);
        Assert.Equal(("Phòng khám 3", "Trần Tiếp Đón (giả lập)"), (latest.Place, latest.ReceivedBy));
        Assert.Empty(latest.Forms);
        var firstForm = s.Ctx.Store.ConsentForms.Single(f => f.Id == first);
        Assert.Equal(
            new EncounterFormDto(first, firstForm.FormNo, s.Ck01.Name, ConsentFormStatus.Completed),
            Assert.Single(list.Value.Items[1].Forms));
    }

    [Fact]
    public async Task List_FromDateToDate_IncludesEachDay_AndRejectsBadRanges()
    {
        var s = Scenario();
        var formId = await PatientSignedCk01(s);
        var encounters = s.Ctx.CreateConsentEncounterService();

        var twoDays = await encounters.ListForFormAsync(formId, Today.AddDays(-1), Today, Ct);
        var yesterday = await encounters.ListForFormAsync(formId, Today.AddDays(-1), null, Ct);
        var longest = await encounters.ListForFormAsync(formId, Today.AddDays(-(ConsentEncounterService.MaxDays - 1)), Today, Ct);
        var reversed = await encounters.ListForFormAsync(formId, Today, Today.AddDays(-1), Ct);
        var tooLong = await encounters.ListForFormAsync(formId, Today.AddDays(-ConsentEncounterService.MaxDays), Today, Ct);

        Assert.Equal(["TN-0002", "TN-0001", "TN-0000"], twoDays.Value.Items.Select(i => i.EncounterNo).ToList());
        Assert.Equal((Today.AddDays(-1), Today), (twoDays.Value.From, twoDays.Value.To));
        Assert.Equal(["TN-0000"], yesterday.Value.Items.Select(i => i.EncounterNo).ToList());
        Assert.True(longest.IsSuccess);
        Assert.True(reversed.Error!.FieldErrors!.ContainsKey("from"));
        Assert.True(tooLong.Error!.FieldErrors!.ContainsKey("from"));
    }

    [Fact]
    public async Task List_UsesTheCitizenIdWhenThePatientHasNoCode_AndAsksToCompleteARecordWithNeither()
    {
        var s = Scenario();
        var noCode = Patient.Create(
            new PatientDetails(
                "Hoàng Văn Chưa Mã (giả lập)", new DateOnly(1990, 1, 2), Gender.Male, "036090000777", null, null,
                "Số 3 Đường Thử Nghiệm", null, null, "Kinh", null, "Việt Nam", null, null),
            s.Ctx.Clock.UtcNow);
        var neither = Patient.Create(
            new PatientDetails(
                "Lý Thị Chưa Giấy Tờ (giả lập)", new DateOnly(1992, 3, 4), Gender.Female, null, null, null,
                "Số 4 Đường Thử Nghiệm", null, null, "Kinh", null, "Việt Nam", null, null),
            s.Ctx.Clock.UtcNow);
        s.Ctx.Store.Patients.AddRange([noCode, neither]);
        s.Ctx.EncounterSource.Add("036090000777", "TN-0100", At(Today, 9, 15), "Phòng khám 2", null);
        var byCitizenId = await PatientSignedCk01(s, noCode);
        var withNeither = await PatientSignedCk01(s, neither);
        var encounters = s.Ctx.CreateConsentEncounterService();

        var listed = await encounters.ListForFormAsync(byCitizenId, null, null, Ct);
        var noKey = await encounters.ListForFormAsync(withNeither, null, null, Ct);
        var completeNoKey = await s.Signing.CompleteAsync(withNeither, new(SignaturePng, EncounterNo: "TN-0100", EncounterDate: Today), Ct);

        Assert.Equal(["TN-0100"], listed.Value.Items.Select(i => i.EncounterNo).ToList());
        Assert.Equal(((string?)null, "036090000777", Today, Today), Assert.Single(s.Ctx.EncounterSource.Calls));
        Assert.Equal("consent_forms.encounters_no_patient_key", noKey.Error!.Code);
        Assert.Equal("consent_forms.encounters_no_patient_key", completeNoKey.Error!.Code);
    }

    [Fact]
    public async Task ConfirmBundle_WithoutAValidEncounter_WhenAFormWouldBeCompleted_ChangesNothing()
    {
        var s = Scenario();
        var (bundleId, _, _) = await PatientSignedBundle(s);

        var missing = await s.Signing.ConfirmBundleAsync(bundleId, new(SignaturePng), Ct);
        var wrongDay = await s.Signing.ConfirmBundleAsync(bundleId, new(SignaturePng, EncounterNo: "TN-0000", EncounterDate: Today), Ct);

        Assert.True(missing.Error!.FieldErrors!.ContainsKey("encounterNo"));
        Assert.True(wrongDay.Error!.FieldErrors!.ContainsKey("encounterNo"));
        // Không phiếu nào bị đổi, kể cả bước "người tư vấn" của TUVAN (bước trung gian không cần lượt tiếp nhận).
        Assert.All(s.Ctx.Store.ConsentForms, f =>
        {
            Assert.Equal(ConsentFormStatus.PendingSignature, f.Status);
            Assert.Single(f.Signatures);
            Assert.Null(f.EncounterNo);
        });
    }

    [Fact]
    public async Task ConfirmBundle_AttachesTheEncounterToTheFormsItCompletes_CheckingHisOncePerConfirmation()
    {
        var s = Scenario();
        var (bundleId, bm02, tuvan) = await PatientSignedBundle(s);

        // Tiếp đón: hoàn tất BM02 và xác nhận bước "người tư vấn" của TUVAN trong một lần.
        var receptionist = await s.Signing.ConfirmBundleAsync(bundleId, new(SignaturePng, EncounterNo: "TN-0002", EncounterDate: Today), Ct);

        Assert.True(receptionist.IsSuccess, receptionist.Error?.Message);
        Assert.Empty(receptionist.Value.Failed);
        Assert.Single(s.Ctx.EncounterSource.Calls);
        var forms = s.Ctx.Store.ConsentForms;
        Assert.Equal("TN-0002", forms.Single(f => f.Id == bm02).EncounterNo);
        Assert.Null(forms.Single(f => f.Id == tuvan).EncounterNo);

        // Điều dưỡng: hoàn tất TUVAN vào cùng lượt tiếp nhận.
        s.LogInAs(s.Kkb, SystemRoles.Nurse);
        var nurse = await s.Signing.ConfirmBundleAsync(bundleId, new(SignaturePng, EncounterNo: "TN-0002", EncounterDate: Today), Ct);

        Assert.True(nurse.IsSuccess, nurse.Error?.Message);
        Assert.All(forms, f => Assert.Equal(("TN-0002", ConsentFormStatus.Completed), (f.EncounterNo, f.Status)));
        Assert.Equal("TN-0002", s.Ctx.Pdf.Last!.Encounter!.Number);

        // Danh sách lượt tiếp nhận của bộ: lượt TN-0002 kèm cả hai phiếu, theo số phiếu.
        var list = await s.Ctx.CreateConsentEncounterService().ListForBundleAsync(bundleId, null, null, Ct);
        var chosen = list.Value.Items[0];
        Assert.Equal("TN-0002", chosen.EncounterNo);
        Assert.Equal(
            forms.OrderBy(f => f.FormNo, StringComparer.Ordinal).Select(f => f.Id).ToList(),
            chosen.Forms.Select(f => f.Id).ToList());
        Assert.Empty(list.Value.Items[1].Forms);
    }

    [Fact]
    public async Task ConfirmBundle_WithOnlyIntermediateSteps_NeedsNoEncounter()
    {
        var s = Scenario();
        var (bundleId, bm02, tuvan) = await PatientSignedBundle(s);
        Assert.True((await s.Signing.CompleteAsync(bm02, new(SignaturePng, EncounterNo: "TN-0001", EncounterDate: Today), Ct)).IsSuccess);

        var stepOnly = await s.Signing.ConfirmBundleAsync(bundleId, new(SignaturePng), Ct);

        Assert.True(stepOnly.IsSuccess, stepOnly.Error?.Message);
        Assert.Equal([("step", tuvan)], stepOnly.Value.Confirmed.Select(c => (c.Action, c.Id)).ToList());
        Assert.Single(s.Ctx.EncounterSource.Calls);
    }

    [Fact]
    public async Task List_OutsideTheUsersScope_IsNotFound()
    {
        var s = Scenario();
        var (bundleId, bm02, _) = await PatientSignedBundle(s);
        s.LogIn(s.Noi);
        var encounters = s.Ctx.CreateConsentEncounterService();

        Assert.Equal("consent_forms.not_found", (await encounters.ListForFormAsync(bm02, null, null, Ct)).Error!.Code);
        Assert.Equal("consent_forms.bundle_not_found", (await encounters.ListForBundleAsync(bundleId, null, null, Ct)).Error!.Code);
        Assert.Empty(s.Ctx.EncounterSource.Calls);
    }
}
