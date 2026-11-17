using Consent.Domain.Common;
using Consent.Domain.ConsentForms;

namespace Consent.Domain.Tests;

/// <summary>Mã ký trên điện thoại riêng của người bệnh (quyết định 32): hạn mở, một điện thoại, sai ngày sinh thì hủy.</summary>
public sealed class RemoteSigningLinkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 2, 0, 0, TimeSpan.Zero);

    private static readonly string CodeHash = new('a', RemoteSigningLink.HashLength);

    private static readonly string DeviceHash = new('b', RemoteSigningLink.HashLength);

    private static RemoteSigningLink NewLink(params Guid[] formIds) =>
        RemoteSigningLink.Create(
            CodeHash, Guid.NewGuid(), Guid.NewGuid(), formIds.Length == 0 ? [Guid.NewGuid()] : formIds, null, Guid.NewGuid(), Now);

    [Fact]
    public void NewLink_CanBeOpenedOnlyWithinTheOpenWindow()
    {
        var link = NewLink();

        Assert.Equal(Now + RemoteSigningLink.OpenWindow, link.ExpiresAtUtc);
        Assert.True(link.CanOpen(Now));
        Assert.True(link.CanOpen(link.ExpiresAtUtc.AddSeconds(-1)));
        Assert.False(link.CanOpen(link.ExpiresAtUtc));
        Assert.False(link.CanResume(Now));
    }

    [Fact]
    public void Open_BindsThePhone_ThenOnlyResumesWithinTheSigningWindow()
    {
        var link = NewLink();
        var openedAt = Now.AddMinutes(5);

        link.Open(DeviceHash, openedAt);

        Assert.True(link.IsOpened);
        Assert.Equal(DeviceHash, link.DeviceKeyHash);
        Assert.False(link.CanOpen(openedAt));
        Assert.True(link.CanResume(openedAt + RemoteSigningLink.SignWindow - TimeSpan.FromSeconds(1)));
        Assert.False(link.CanResume(openedAt + RemoteSigningLink.SignWindow));
        Assert.Throws<DomainException>(() => link.Open(DeviceHash, openedAt));
    }

    [Fact]
    public void FiveWrongBirthDates_CancelTheLink()
    {
        var link = NewLink();

        var results = Enumerable.Range(1, RemoteSigningLink.MaxBirthDateAttempts).Select(_ => link.RegisterFailedAttempt(Now)).ToList();

        Assert.Equal([false, false, false, false, true], results);
        Assert.Equal(RemoteSigningCancelReason.TooManyAttempts, link.CancelReason);
        Assert.False(link.CanOpen(Now));
        Assert.Throws<DomainException>(() => link.RegisterFailedAttempt(Now));
    }

    [Fact]
    public void Cancel_KeepsTheFirstReason_AndStopsResuming()
    {
        var link = NewLink();
        link.Open(DeviceHash, Now);

        link.Cancel(RemoteSigningCancelReason.Staff, Now.AddMinutes(1));
        link.Cancel(RemoteSigningCancelReason.TooManyAttempts, Now.AddMinutes(2));

        Assert.Equal(RemoteSigningCancelReason.Staff, link.CancelReason);
        Assert.Equal(Now.AddMinutes(1), link.CancelledAtUtc);
        Assert.False(link.CanResume(Now.AddMinutes(3)));
    }

    [Fact]
    public void Create_NeedsForms_AndHashesNotPlainCodes()
    {
        var form = Guid.NewGuid();

        var deduplicated = NewLink(form, form);

        Assert.Equal([form], deduplicated.FormIds);
        Assert.Throws<DomainException>(() => RemoteSigningLink.Create(CodeHash, Guid.NewGuid(), Guid.NewGuid(), [], null, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => RemoteSigningLink.Create(
            "ma-trong-qr", Guid.NewGuid(), Guid.NewGuid(), [form], null, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => NewLink().Open(CodeHash.ToUpperInvariant(), Now));
    }
}
