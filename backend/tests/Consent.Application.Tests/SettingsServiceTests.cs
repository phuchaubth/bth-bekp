using Consent.Application.Settings;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Common;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class SettingsServiceTests
{
    [Fact]
    public async Task Get_FallsBackToDeploymentDefaults_UntilAnAdministratorSavesValues()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateSettingsService();

        var before = await service.GetAsync(CancellationToken.None);
        var saved = await service.UpdateHospitalAsync(new("Sở Y tế Thử nghiệm", "Bệnh viện Giả lập", "Thành phố Mẫu"), CancellationToken.None);
        var after = await service.GetAsync(CancellationToken.None);

        Assert.Equal("BỆNH VIỆN [TÊN BỆNH VIỆN]", before.Hospital.Name);
        Assert.Null(before.Hospital.UpdatedAtUtc);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(("Sở Y tế Thử nghiệm", "Bệnh viện Giả lập", "Thành phố Mẫu"), (after.Hospital.ParentAgency, after.Hospital.Name, after.Hospital.Place));
        Assert.Equal(ctx.Clock.UtcNow, after.Hospital.UpdatedAtUtc);
        Assert.Equal(ctx.His.IsEnabled, after.HisEnabled);
        Assert.False(after.SmartCaEnabled);
        Assert.Equal(3, ctx.Store.SystemSettings.Count);
        Assert.Equal(1, ctx.Store.SaveCount);
    }

    [Fact]
    public async Task Update_TrimsValues_OnlyTouchesChangedKeys_AndRejectsEmptyOrTooLong()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateSettingsService();
        await service.UpdateHospitalAsync(new("A", "B", "C"), CancellationToken.None);
        var first = ctx.Store.SystemSettings.Single(s => s.Key == SettingKeys.HospitalName).UpdatedAtUtc;
        ctx.Clock.Advance(TimeSpan.FromMinutes(5));

        var changed = await service.UpdateHospitalAsync(new("  A  ", "Bệnh viện Mới ", "C"), CancellationToken.None);
        var empty = await service.UpdateHospitalAsync(new("A", " ", "C"), CancellationToken.None);
        var tooLong = await service.UpdateHospitalAsync(new("A", new string('x', 501), "C"), CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.Equal("Bệnh viện Mới", changed.Value.Hospital.Name);
        Assert.Equal(first, ctx.Store.SystemSettings.Single(s => s.Key == SettingKeys.HospitalParentAgency).UpdatedAtUtc);
        Assert.Equal(ctx.Clock.UtcNow, ctx.Store.SystemSettings.Single(s => s.Key == SettingKeys.HospitalName).UpdatedAtUtc);
        Assert.Equal(ErrorType.Validation, empty.Error!.Type);
        Assert.Contains("name", empty.Error.FieldErrors!.Keys);
        Assert.Equal(ErrorType.Validation, tooLong.Error!.Type);
    }

    [Fact]
    public void SystemSetting_RejectsUnknownKeys()
    {
        Assert.Throws<DomainException>(() => SystemSetting.Create("Jwt.SigningKey", "x", DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => SystemSetting.Create(SettingKeys.HospitalName, "", DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Pdf_UsesTheHospitalNameSavedByTheAdministrator()
    {
        var s = ConsentScenario.Create();
        await s.Ctx.CreateSettingsService().UpdateHospitalAsync(new("SỞ Y TẾ THỬ NGHIỆM", "BỆNH VIỆN GIẢ LẬP", "Thành phố Mẫu"), CancellationToken.None);
        s.LogInAs(s.Kkb, SystemRoles.Receptionist);
        var form = (await s.Service.CreateAsync(new(s.Patient.Id, s.Ck01.Id, null), CancellationToken.None)).Value;

        var pdf = await s.Service.GetPdfAsync(form.Id, CancellationToken.None);

        Assert.True(pdf.IsSuccess);
        Assert.Equal("BỆNH VIỆN GIẢ LẬP", s.Ctx.Pdf.Last!.Hospital.Name);
        Assert.Equal("Thành phố Mẫu", s.Ctx.Pdf.Last.Hospital.Place);
        Assert.Equal(ConsentFormStatus.Draft, form.Status);
    }
}
