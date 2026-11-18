using System.Text.Json.Nodes;
using Consent.Application.Abstractions;
using Consent.Application.Kiosk;
using Consent.Application.Tests.Fakes;
using Consent.Application.Users;
using Consent.Domain.Auditing;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentForms;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Identity;
using Consent.Domain.Patients;
using Consent.Shared;

namespace Consent.Application.Tests;

/// <summary>
/// Kiosk người bệnh tự làm phiếu (quyết định 24): nhận diện an toàn (ngày sinh phải khớp, sai 5 lần khóa 5 phút), nộp và ký
/// một lần cho cả bộ, tài khoản kiosk quyền tối thiểu, nhân viên được nhắc đối chiếu.
/// </summary>
public sealed class KioskServiceTests
{
    private const string Statement = "Tôi đã nghe, hiểu và đồng ý";

    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    /// <summary>Thẻ CCCD giả lập của một người chưa có hồ sơ (số CCCD khớp năm sinh 1985, giới tính nam).</summary>
    private static readonly KioskIdentityRequest NewCard = new(
        KioskIdentityMethod.CitizenIdCard,
        new DateOnly(1985, 3, 12),
        CitizenId: "079085000123",
        Card: new KioskCardData("Nguyễn Văn An (giả lập)", Gender.Male, "Số 1 Đường Thử Nghiệm, Phường Mẫu", new DateOnly(2021, 6, 15), null));

    /// <summary>Người bệnh có sẵn trong kịch bản (mã BN000125, sinh 05/04/1988).</summary>
    private static KioskIdentityRequest ByCode(string code = "BN000125", DateOnly? dateOfBirth = null) =>
        new(KioskIdentityMethod.LookupCode, dateOfBirth ?? new DateOnly(1988, 4, 5), PatientCode: code);

    private static readonly KioskIdentityRequest SelfReported = new(
        KioskIdentityMethod.SelfReported,
        new DateOnly(1990, 1, 2),
        SelfReport: new KioskSelfReport("Trần Thị Mẫu (giả lập)", Gender.Female, "0900000999", "Số 5 Đường Thử Nghiệm"));

    private sealed class Kiosk
    {
        public required ConsentScenario S { get; init; }

        public required User Account { get; init; }

        public required ConsentTemplate Bm02 { get; init; }

        public required ConsentTemplate Ttnb { get; init; }

        public required ConsentTemplate Tuvan { get; init; }

        public KioskService Service => S.Ctx.CreateKioskService();

        public InMemoryStore Store => S.Ctx.Store;

        /// <summary>Trả lời đủ ba phiếu (BM02 khám dịch vụ, TTNB, TUVAN đồng ý).</summary>
        public List<KioskFormAnswers> Answers(ConsentDecision? tuvanDecision = ConsentDecision.Agreed, bool withReligion = true)
        {
            var ttnb = new JsonObject
            {
                ["ethnicity"] = "Kinh",
                ["educationLevel"] = "12/12",
                ["occupation"] = "Nhân viên văn phòng",

                // Câu hỏi có nguồn tự điền: máy chủ luôn lấy theo hồ sơ nếu hồ sơ đã có (giá trị gửi lên bị bỏ qua).
                ["permanentAddress"] = "Địa chỉ người bệnh trả lời",
                ["currentAddress"] = "Chỗ ở người bệnh trả lời",
                ["familyName"] = "Nguyễn Thị Bình (giả lập)",
                ["familyRelationship"] = "Vợ",
                ["familyPhone"] = "0900000111",
                ["familyIsEmergencyContact"] = "yes",
            };
            if (withReligion)
            {
                ttnb["religion"] = "buddhism";
            }

            return
            [
                new(Bm02.Id, new JsonObject { ["careScheme"] = "nonInsurance" }, null),
                new(Ttnb.Id, ttnb, null),
                new(Tuvan.Id, [], tuvanDecision),
            ];
        }

        public KioskSubmitRequest Submit(KioskIdentityRequest identity, string? phone = "0900000555") =>
            new(identity, phone, Answers(), Statement, SignaturePng);
    }

    private static async Task<Kiosk> NewKiosk(bool configure = true)
    {
        var s = ConsentScenario.Create();
        var bm02 = s.AddHospitalTemplate("QT-KNGT-BM02");
        var ttnb = s.AddHospitalTemplate("TTNB-NGOAITRU");
        var tuvan = s.AddHospitalTemplate("TUVAN-CONGTT");
        if (configure)
        {
            var saved = await s.Ctx.CreateKioskSettingsService().UpdateAsync(new([bm02.Id, ttnb.Id, tuvan.Id]), CancellationToken.None);
            Assert.True(saved.IsSuccess, saved.Error?.Message);
        }

        var account = s.Ctx.AddUser("kiosk.kkb", SystemRoles.Kiosk, departmentId: s.Kkb.Id);
        LogInKiosk(s, account);
        return new Kiosk { S = s, Account = account, Bm02 = bm02, Ttnb = ttnb, Tuvan = tuvan };
    }

    private static void LogInKiosk(ConsentScenario s, User account)
    {
        var user = s.Ctx.CurrentUser;
        user.UserId = account.Id;
        user.UserName = account.UserName;
        user.DepartmentId = account.DepartmentId;
        user.SigningFormIds = [];
        user.SigningSessionId = null;
        user.Roles.Clear();
        user.Roles.Add(SystemRoles.Kiosk);
        user.Permissions.Clear();
        user.Permissions.Add(Permissions.KioskUse);
    }

    /// <summary>Tiếp đón lập, cho ký và xác nhận một phiếu BM02 (khám dịch vụ) của người bệnh có sẵn; trả về số phiếu.</summary>
    private static async Task<string> CompletedBm02(Kiosk k)
    {
        var s = k.S;
        var ct = CancellationToken.None;
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, k.Bm02.Id, null), ct)).Value;
        Assert.True((await s.Service.UpdateAsync(
            form.Id, new(new JsonObject { ["careScheme"] = "nonInsurance" }, ConsentSignerKind.Patient, null), ct)).IsSuccess);
        Assert.True((await s.Service.SubmitAsync(form.Id, ct)).IsSuccess);
        Assert.True((await s.Signing.StartSigningAsync(form.Id, ct)).IsSuccess);
        var staffLogsInAgain = s.UseSigningToken();
        var signed = await s.Signing.SignAsPatientAsync(form.Id, new(null, SignaturePng, Statement), ct);
        staffLogsInAgain();
        Assert.True(signed.IsSuccess, signed.Error?.Message);
        var completed = await s.Signing.CompleteAsync(form.Id, new(SignaturePng), ct);
        Assert.True(completed.IsSuccess, completed.Error?.Message);
        LogInKiosk(s, k.Account);
        return form.FormNo;
    }

    [Fact]
    public async Task PreviousForm_IsOfferedWithoutItsAnswers_AndKeptWhenThePatientChangesNothing()
    {
        var k = await NewKiosk();
        k.S.Ctx.SetInheritanceDays(30);
        var previous = await CompletedBm02(k);
        k.S.Ctx.Clock.Advance(TimeSpan.FromDays(1));

        var identity = (await k.Service.IdentifyAsync(ByCode(), CancellationToken.None)).Value;
        var newcomer = (await k.Service.IdentifyAsync(NewCard, CancellationToken.None)).Value;

        // Chỉ báo mẫu nào có phiếu lần trước và câu nào đã trả lời, không trả câu trả lời.
        var offered = Assert.Single(identity.Inherited!);
        Assert.Equal(k.Bm02.Id, offered.TemplateId);
        Assert.Equal(["careScheme"], offered.Keys);
        Assert.Empty(newcomer.Inherited!);

        // "Không thay đổi": kiosk không hỏi lại câu BM02; không giữ như lần trước thì phải trả lời.
        var answers = k.Answers();
        answers[0] = new(k.Bm02.Id, [], null);
        var notKept = await k.Service.SubmitAsync(new(ByCode(), null, answers, Statement, SignaturePng), CancellationToken.None);
        var kept = await k.Service.SubmitAsync(new(ByCode(), null, answers, Statement, SignaturePng, KeepPrevious: true), CancellationToken.None);

        Assert.Equal("kiosk.incomplete", notKept.Error!.Code);
        Assert.Contains("QT-KNGT-BM02.careScheme", notKept.Error.FieldErrors!.Keys);
        Assert.True(kept.IsSuccess, kept.Error?.Message);
        var bm02 = k.Store.ConsentForms.Single(f => f.Id == kept.Value.Forms[0].Id);
        Assert.Equal("nonInsurance", JsonNode.Parse(bm02.DataJson)!["careScheme"]!.GetValue<string>());
        // Ý kiến Đồng ý/Không đồng ý vẫn do người bệnh chọn lại (TUVAN), không lấy theo lần trước.
        Assert.Equal(ConsentDecision.Agreed, kept.Value.Forms[2].Decision);
        var audit = Assert.Single(k.Store.AuditLogs, a => a.Action == AuditActions.ConsentFormInherited && a.EntityId == bm02.Id.ToString());
        Assert.Contains(previous, audit.Details, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NothingIsOffered_WhenInheritanceIsOff()
    {
        var k = await NewKiosk();
        await CompletedBm02(k);

        var identity = (await k.Service.IdentifyAsync(ByCode(), CancellationToken.None)).Value;
        var answers = k.Answers();
        answers[0] = new(k.Bm02.Id, [], null);
        var kept = await k.Service.SubmitAsync(new(ByCode(), null, answers, Statement, SignaturePng, KeepPrevious: true), CancellationToken.None);

        Assert.Empty(identity.Inherited!);
        Assert.Contains("QT-KNGT-BM02.careScheme", kept.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Config_ListsTheKioskTemplatesInOrder_WithOneStatementForTheWholeBundle()
    {
        var k = await NewKiosk();

        var config = (await k.Service.GetConfigAsync(CancellationToken.None)).Value;

        Assert.True(config.Ready);
        Assert.Equal("Khoa Khám bệnh", config.DepartmentName);
        Assert.Equal(["QT-KNGT-BM02", "TTNB-NGOAITRU", "TUVAN-CONGTT"], config.Templates.Select(t => t.Code));
        Assert.NotNull(config.Statement);
        Assert.True(config.Statement.Required);
        Assert.Contains(Statement, config.Statement.Suggestions!);
        Assert.Equal(config.Statement.Suggestions!.Count, config.Statement.Suggestions.Distinct().Count());
    }

    [Fact]
    public async Task Config_NotReady_WhenNoTemplateIsChosen_AndRefused_WhenTheAccountHasNoDepartment()
    {
        var k = await NewKiosk(configure: false);
        var config = (await k.Service.GetConfigAsync(CancellationToken.None)).Value;
        Assert.False(config.Ready);
        Assert.Empty(config.Templates);

        var submitted = await k.Service.SubmitAsync(k.Submit(NewCard), CancellationToken.None);
        Assert.Equal("kiosk.not_configured", submitted.Error?.Code);

        var noDepartment = k.S.Ctx.AddUser("kiosk.khac", SystemRoles.Kiosk);
        LogInKiosk(k.S, noDepartment);
        Assert.Equal("kiosk.no_department", (await k.Service.GetConfigAsync(CancellationToken.None)).Error?.Code);
    }

    [Fact]
    public async Task Settings_KeepTheChosenOrder_AndRefuseTemplatesThePatientCannotFillAlone()
    {
        var k = await NewKiosk(configure: false);
        var settings = k.S.Ctx.CreateKioskSettingsService();

        var saved = await settings.UpdateAsync(new([k.Tuvan.Id, k.Bm02.Id]), CancellationToken.None);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(["TUVAN-CONGTT", "QT-KNGT-BM02"], saved.Value.Templates.Select(t => t.Code));
        Assert.All(saved.Value.Templates, t => Assert.Empty(t.Problems));

        // TN-01 có trường ngày giờ/bảng nhiều dòng (phần chuyên môn): không dùng được trên kiosk.
        var refused = await settings.UpdateAsync(new([k.Bm02.Id, k.S.Tn01.Id]), CancellationToken.None);
        Assert.Equal(ErrorType.Validation, refused.Error?.Type);
        Assert.Contains("TN-01", refused.Error!.FieldErrors!["templateIds"][0], StringComparison.Ordinal);

        var draft = ConsentTemplate.Create("NHAP-01", "Mẫu nháp", SystemRoles.Nurse, null, k.Bm02.PublishedVersion!.ContentJson, k.S.Ctx.Clock.UtcNow);
        k.Store.ConsentTemplates.Add(draft);
        Assert.False((await settings.UpdateAsync(new([draft.Id]), CancellationToken.None)).IsSuccess);
        Assert.False((await settings.UpdateAsync(new([k.Bm02.Id, k.Bm02.Id]), CancellationToken.None)).IsSuccess);

        var cleared = await settings.UpdateAsync(new([]), CancellationToken.None);
        Assert.Empty(cleared.Value.Templates);
        Assert.DoesNotContain(k.Store.SystemSettings, s => s.Key == Domain.Settings.SettingKeys.KioskTemplateCodes);
    }

    [Fact]
    public async Task IdentifyByCard_NewPerson_ShowsTheCardDetails_AsksForThePhone_AndSavesNothingYet()
    {
        var k = await NewKiosk();
        var patientsBefore = k.Store.Patients.Count;

        var identity = (await k.Service.IdentifyAsync(NewCard, CancellationToken.None)).Value;

        Assert.False(identity.FoundRecord);
        Assert.Equal("Nguyễn Văn An (giả lập)", identity.FullName);
        Assert.Equal("0790******23", identity.CitizenIdMasked);
        Assert.Equal("Số 1 Đường Thử Nghiệm, Phường Mẫu", identity.PermanentAddress);
        Assert.False(identity.HasPhoneNumber);
        Assert.Equal([KioskService.PhoneNumberField], identity.MissingFields);
        Assert.Contains("patient.permanentAddress", identity.PrefilledSources);
        Assert.Equal(patientsBefore, k.Store.Patients.Count);
        Assert.Contains(k.Store.AuditLogs, a => a.Action == AuditActions.KioskIdentified);
        Assert.DoesNotContain(k.Store.AuditLogs, a => a.Details?.Contains("079085000123", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task IdentifyByCode_MatchingBirthDate_ShowsTheRecord_ButNeverTheAddressOrPhone()
    {
        var k = await NewKiosk();

        var identity = (await k.Service.IdentifyAsync(ByCode(), CancellationToken.None)).Value;

        Assert.True(identity.FoundRecord);
        Assert.Equal("Phạm Thị Dung (giả lập)", identity.FullName);
        Assert.Equal("BN000125", identity.PatientCode);
        Assert.Equal("0361******25", identity.CitizenIdMasked);
        Assert.Null(identity.PermanentAddress);
        Assert.True(identity.HasAddressOnFile);
        Assert.True(identity.HasPhoneNumber);
        Assert.Empty(identity.MissingFields);
        Assert.Contains("patient.healthInsuranceNumber", identity.PrefilledSources);
    }

    [Fact]
    public async Task IdentifyByCode_WrongBirthDateOrUnknownCode_GiveTheSameAnswer_AndLockForFiveMinutesAfterFiveTries()
    {
        var k = await NewKiosk();
        var wrongBirthDate = ByCode(dateOfBirth: new DateOnly(1988, 4, 6));

        for (var attempt = 1; attempt < KioskLookupPolicy.MaxFailedAttempts; attempt++)
        {
            var request = attempt % 2 == 0 ? ByCode("BN999999") : wrongBirthDate;
            var failed = await k.Service.IdentifyAsync(request, CancellationToken.None);
            Assert.Equal("kiosk.lookup_failed", failed.Error?.Code);
            Assert.Equal(KioskLookupPolicy.MaxFailedAttempts - attempt, failed.Error!.Metadata!["remainingAttempts"]);
        }

        var locked = await k.Service.IdentifyAsync(wrongBirthDate, CancellationToken.None);
        Assert.Equal("kiosk.lookup_locked", locked.Error?.Code);
        Assert.Contains(k.Store.AuditLogs, a => a.Action == AuditActions.KioskLookupLocked);

        // Đang khóa: kể cả nhập đúng hay quét thẻ cũng phải chờ.
        Assert.Equal("kiosk.lookup_locked", (await k.Service.IdentifyAsync(ByCode(), CancellationToken.None)).Error?.Code);
        Assert.Equal("kiosk.lookup_locked", (await k.Service.IdentifyAsync(NewCard, CancellationToken.None)).Error?.Code);

        k.S.Ctx.Clock.Advance(KioskLookupPolicy.LockoutDuration);
        Assert.True((await k.Service.IdentifyAsync(ByCode(), CancellationToken.None)).IsSuccess);
        Assert.Equal(0, k.Account.KioskLookupFailures);
        Assert.Null(k.Account.KioskLookupLockedUntilUtc);
    }

    [Fact]
    public async Task SelfReport_WithoutACitizenId_LooksUpNobody_SoItStillWorksWhileLookupsAreLocked()
    {
        var k = await NewKiosk();
        for (var i = 0; i < KioskLookupPolicy.MaxFailedAttempts; i++)
        {
            await k.Service.IdentifyAsync(ByCode("BN999999"), CancellationToken.None);
        }

        var identity = await k.Service.IdentifyAsync(SelfReported, CancellationToken.None);

        Assert.True(identity.IsSuccess, identity.Error?.Message);
        Assert.False(identity.Value.FoundRecord);
        Assert.True(identity.Value.HasPhoneNumber);
        Assert.Contains("patient.currentAddress", identity.Value.PrefilledSources);
        Assert.True(k.Account.IsKioskLookupLocked(k.S.Ctx.Clock.UtcNow));
    }

    [Fact]
    public async Task IdentifyByCode_HospitalSystemDown_IsNotCountedAsAWrongTry()
    {
        var k = await NewKiosk();
        k.S.Ctx.PatientSource.Throws = true;

        var result = await k.Service.IdentifyAsync(ByCode("BN000777"), CancellationToken.None);

        Assert.Equal("kiosk.source_unavailable", result.Error?.Code);
        Assert.Equal(0, k.Account.KioskLookupFailures);
    }

    [Fact]
    public async Task IdentifyByCode_FindsAPatientOnlyInTheHospitalSystem_WhenTheBirthDateMatches()
    {
        var k = await NewKiosk();
        k.S.Ctx.PatientSource.Result = new PatientSourceResult(
            PatientSourceStatus.Found,
            new ExternalPatient("BN000777", "Lê Văn Bảy (giả lập)", new DateOnly(1970, 7, 7), Gender.Male, "001070000777", "0909.000.777",
                "Số 7 Đường Thử Nghiệm", null, "Kinh", "Hưu trí", "Việt Nam", null));

        Assert.Equal(
            "kiosk.lookup_failed",
            (await k.Service.IdentifyAsync(ByCode("BN000777", new DateOnly(1970, 7, 8)), CancellationToken.None)).Error?.Code);
        var identity = (await k.Service.IdentifyAsync(ByCode("BN000777", new DateOnly(1970, 7, 7)), CancellationToken.None)).Value;

        Assert.True(identity.FoundRecord);
        Assert.Equal("Lê Văn Bảy (giả lập)", identity.FullName);
        Assert.True(identity.HasPhoneNumber);
        Assert.Contains("patient.occupation", identity.PrefilledSources);
        Assert.Equal([("BN000777", (string?)null), ("BN000777", null)], k.S.Ctx.PatientSource.Calls);
    }

    [Fact]
    public async Task SubmitByCard_CreatesThePatient_AndOneBundleSignedOnceByThePatient_WaitingForStaff()
    {
        var k = await NewKiosk();

        var submitted = await k.Service.SubmitAsync(k.Submit(NewCard), CancellationToken.None);

        Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        var patient = k.Store.Patients.Single(p => p.CitizenId == "079085000123");
        Assert.Equal("Nguyễn Văn An (giả lập)", patient.FullName);
        // Quyết định 41: hồ sơ do tài khoản kiosk tạo.
        Assert.Equal(k.S.Ctx.CurrentUser.UserId, patient.CreatedByUserId);
        Assert.Equal("0900000555", patient.PhoneNumber);
        Assert.Equal("Số 1 Đường Thử Nghiệm, Phường Mẫu", patient.PermanentAddress);
        Assert.Equal(new DateOnly(2021, 6, 15), patient.CitizenIdIssueDate);

        var forms = k.Store.ConsentForms.Where(f => f.PatientId == patient.Id).ToList();
        Assert.Equal(3, forms.Count);
        Assert.Single(forms.Select(f => f.BundleId).Distinct());
        Assert.NotNull(submitted.Value.BundleId);
        Assert.All(forms, f =>
        {
            Assert.Equal(ConsentFormStatus.PendingSignature, f.Status);
            Assert.Equal(KioskIdentityMethod.CitizenIdCard, f.KioskIdentity);
            Assert.Equal(k.S.Kkb.Id, f.DepartmentId);
            Assert.Equal(k.Account.Id, f.CreatedByUserId);
            Assert.NotNull(f.PatientSignature);
            Assert.Equal(Statement, f.PatientSignature.Statement);
            Assert.True(f.CanComplete);
        });
        Assert.Single(forms.Select(f => f.PatientSignature!.SignedAtUtc).Distinct());
        Assert.Equal(ConsentDecision.Agreed, forms.Single(f => f.TemplateId == k.Tuvan.Id).Decision);
        Assert.Null(forms.Single(f => f.TemplateId == k.Ttnb.Id).Decision);

        // Địa chỉ trên thẻ tự điền vào phiếu thông tin người bệnh.
        var ttnbData = JsonNode.Parse(forms.Single(f => f.TemplateId == k.Ttnb.Id).DataJson)!.AsObject();
        Assert.Equal("Số 1 Đường Thử Nghiệm, Phường Mẫu", ttnbData["permanentAddress"]!.GetValue<string>());
        Assert.Equal(["QT-KNGT-BM02", "TTNB-NGOAITRU", "TUVAN-CONGTT"],
            submitted.Value.Forms.Select(f => k.Store.ConsentTemplates.Single(t => t.Name == f.TemplateName).Code));
        Assert.Contains(k.Store.AuditLogs, a => a.Action == AuditActions.KioskSubmitted);
        Assert.Equal(3, k.Store.AuditLogs.Count(a => a.Action == AuditActions.ConsentFormPatientSigned));
    }

    [Fact]
    public async Task Submit_WithUnansweredQuestions_ListsThemPerForm_AndSavesNothing()
    {
        var k = await NewKiosk();
        var patientsBefore = k.Store.Patients.Count;
        var request = new KioskSubmitRequest(NewCard, "0900000555", k.Answers(tuvanDecision: null, withReligion: false), Statement, SignaturePng);

        var result = await k.Service.SubmitAsync(request, CancellationToken.None);

        Assert.Equal("kiosk.incomplete", result.Error?.Code);
        Assert.Contains("TTNB-NGOAITRU.religion", result.Error!.FieldErrors!.Keys);
        Assert.Contains("TUVAN-CONGTT.decision", result.Error.FieldErrors.Keys);
        Assert.Equal(patientsBefore, k.Store.Patients.Count);
        Assert.Empty(k.Store.ConsentForms);
    }

    [Fact]
    public async Task Submit_RequiresThePhoneWhenTheRecordHasNone_TheStatement_AndARealSignature()
    {
        var k = await NewKiosk();

        Assert.Equal(
            KioskService.PhoneNumberField,
            (await k.Service.SubmitAsync(k.Submit(NewCard, phone: null), CancellationToken.None)).Error?.FieldErrors?.Keys.Single());
        Assert.Equal(
            KioskService.PhoneNumberField,
            (await k.Service.SubmitAsync(k.Submit(NewCard, phone: "12345"), CancellationToken.None)).Error?.FieldErrors?.Keys.Single());
        Assert.Equal(
            "statement",
            (await k.Service.SubmitAsync(k.Submit(NewCard) with { Statement = " " }, CancellationToken.None)).Error?.FieldErrors?.Keys.Single());
        Assert.False((await k.Service.SubmitAsync(k.Submit(NewCard) with { Image = null }, CancellationToken.None)).IsSuccess);
        Assert.Empty(k.Store.ConsentForms);
    }

    [Fact]
    public async Task SubmitForAnExistingPatient_OnlyFillsEmptyFields_FromTheHospitalSystem()
    {
        var k = await NewKiosk();
        var patient = k.S.Patient;
        k.S.Ctx.PatientSource.Result = new PatientSourceResult(
            PatientSourceStatus.Found,
            new ExternalPatient("BN000125", "Tên Khác Trên HIS", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", "0911111111",
                "Địa chỉ khác trên HIS", "Chỗ ở trên HIS", "Kinh", "Giáo viên", "Việt Nam", null));

        var submitted = await k.Service.SubmitAsync(k.Submit(ByCode(), phone: null), CancellationToken.None);

        Assert.True(submitted.IsSuccess, submitted.Error?.Message);
        Assert.Equal("Phạm Thị Dung (giả lập)", patient.FullName);
        Assert.Equal("0900000125", patient.PhoneNumber);
        Assert.Equal("Số 9 Đường Thử Nghiệm", patient.PermanentAddress);
        Assert.Equal("Chỗ ở trên HIS", patient.CurrentAddress);
        Assert.Equal("Giáo viên", patient.Occupation);
        Assert.All(k.Store.ConsentForms, f => Assert.Equal(KioskIdentityMethod.LookupCode, f.KioskIdentity));
    }

    [Fact]
    public async Task Submit_WithATamperedBirthDate_CountsAWrongTry_AndCreatesNothing()
    {
        var k = await NewKiosk();

        var result = await k.Service.SubmitAsync(k.Submit(ByCode(dateOfBirth: new DateOnly(2000, 1, 1))), CancellationToken.None);

        Assert.Equal("kiosk.lookup_failed", result.Error?.Code);
        Assert.Equal(1, k.Account.KioskLookupFailures);
        Assert.Empty(k.Store.ConsentForms);
    }

    [Fact]
    public async Task StaffSeeWhichFormsCameFromTheKiosk_AndAreToldToCheckSelfReportedPatientsCarefully()
    {
        var k = await NewKiosk();
        var submitted = (await k.Service.SubmitAsync(k.Submit(SelfReported), CancellationToken.None)).Value;

        k.S.LogInAs(k.S.Kkb, SystemRoles.Receptionist);
        var bundle = (await k.S.Service.GetBundleAsync(submitted.BundleId!.Value, CancellationToken.None)).Value;
        Assert.Equal(KioskIdentityMethod.SelfReported, bundle.KioskIdentity);
        Assert.Contains(bundle.Notices, n => n.Contains("tự khai thông tin tại kiosk", StringComparison.Ordinal));
        Assert.True(bundle.CanConfirm);

        var detail = (await k.S.Service.GetAsync(submitted.Forms[0].Id, CancellationToken.None)).Value;
        Assert.Equal(KioskIdentityMethod.SelfReported, detail.KioskIdentity);
        Assert.Contains(detail.Notices, n => n.Contains("kiosk", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KioskAccount_MustHaveOnlyTheKioskRole_AndADepartment()
    {
        var k = await NewKiosk();
        k.S.LogInAs(k.S.Noi, SystemRoles.Administrator);
        var users = k.S.Ctx.CreateUserAdminService();
        var kioskRole = k.S.Ctx.RoleByCode(SystemRoles.Kiosk).Id;
        var nurseRole = k.S.Ctx.RoleByCode(SystemRoles.Nurse).Id;

        var withOtherRole = await users.CreateAsync(
            new CreateUserRequest("kiosk.2", "Kiosk 2", null, null, null, k.S.Kkb.Id, null, [kioskRole, nurseRole], AppTestContext.DefaultPassword),
            CancellationToken.None);
        Assert.Equal("roleIds", withOtherRole.Error?.FieldErrors?.Keys.Single());

        var withoutDepartment = await users.CreateAsync(
            new CreateUserRequest("kiosk.3", "Kiosk 3", null, null, null, null, null, [kioskRole], AppTestContext.DefaultPassword),
            CancellationToken.None);
        Assert.Equal("departmentId", withoutDepartment.Error?.FieldErrors?.Keys.Single());

        var ok = await users.CreateAsync(
            new CreateUserRequest("kiosk.4", "Kiosk 4", null, null, null, k.S.Kkb.Id, null, [kioskRole], AppTestContext.DefaultPassword),
            CancellationToken.None);
        Assert.True(ok.IsSuccess, ok.Error?.Message);
    }

    [Fact]
    public async Task KioskRole_PermissionsCannotBeChanged_FromThePermissionMatrix()
    {
        var k = await NewKiosk();
        var roles = k.S.Ctx.CreateRoleService();
        var kioskRole = k.S.Ctx.RoleByCode(SystemRoles.Kiosk);

        var result = await roles.UpdatePermissionsAsync(kioskRole.Id, new([Permissions.KioskUse, Permissions.PatientsRead]), CancellationToken.None);

        Assert.Equal("roles.kiosk_immutable", result.Error?.Code);
        Assert.Equal([Permissions.KioskUse], kioskRole.PermissionCodes);
        Assert.True((await roles.ListAsync(CancellationToken.None)).Single(r => r.Code == SystemRoles.Kiosk).IsLocked);
    }
}
