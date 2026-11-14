using Consent.Application.Auth;
using Consent.Application.Tests.Fakes;
using Consent.Domain.Identity;

namespace Consent.Application.Tests;

public sealed class SignatureSpecimenServiceTests
{
    private static readonly string SignaturePng = Convert.ToBase64String(TestPng.Signature());

    [Fact]
    public async Task Specimen_IsSavedNormalised_ShownInSession_AndCanBeRemoved()
    {
        var ctx = new AppTestContext();
        var nurse = ctx.AddUser("dieuduong.thu", SystemRoles.Nurse);
        ctx.CurrentUser.UserId = nurse.Id;
        var service = ctx.CreateSignatureSpecimenService();

        var none = await service.GetMineAsync(CancellationToken.None);
        var saved = await service.SaveMineAsync(new(SignaturePng), CancellationToken.None);
        var stored = await service.GetMineAsync(CancellationToken.None);
        var me = await ctx.CreateAuthService().GetCurrentUserAsync(nurse.Id, CancellationToken.None);
        await service.DeleteMineAsync(CancellationToken.None);

        Assert.Null(none);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.Equal(ctx.Clock.UtcNow, saved.Value);
        // Ảnh đã chuẩn hóa (PNG xám cắt sát nét ký), không phải ảnh gửi lên nguyên văn.
        Assert.NotNull(stored);
        Assert.Equal(0x89, stored![0]);
        Assert.True(me.Value.HasSignatureSpecimen);
        Assert.Null(await service.GetMineAsync(CancellationToken.None));
        Assert.Null(nurse.SignatureSpecimenUpdatedAtUtc);
    }

    [Fact]
    public async Task BlankOrInvalidImage_IsRejected_AndNothingIsStored()
    {
        var ctx = new AppTestContext();
        var nurse = ctx.AddUser("dieuduong.thu", SystemRoles.Nurse);
        ctx.CurrentUser.UserId = nurse.Id;
        var service = ctx.CreateSignatureSpecimenService();

        var blank = await service.SaveMineAsync(new(Convert.ToBase64String(TestPng.Blank())), CancellationToken.None);
        var missing = await service.SaveMineAsync(new(null), CancellationToken.None);

        Assert.Equal("consent_forms.signature_blank", blank.Error!.Code);
        Assert.Contains("image", missing.Error!.FieldErrors!.Keys);
        Assert.Null(nurse.SignatureSpecimenPng);
    }
}
