using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.ConsentForms;
using Consent.Domain.Identity;

namespace Consent.Domain.Tests;

/// <summary>Kiosk người bệnh tự làm phiếu (quyết định 24): vai trò Kiosk, khóa tra cứu, đánh dấu phiếu lập trên kiosk.</summary>
public sealed class KioskDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void KioskRole_HasOnlyTheKioskPermission_AndItCannotBeChanged()
    {
        var definition = SystemRoles.Defaults.Single(d => d.Code == SystemRoles.Kiosk);
        Assert.Equal([Permissions.KioskUse], definition.Permissions);

        var role = Role.Create(definition.Code, definition.Name, definition.Description, isSystem: true);
        role.SetPermissions(definition.Permissions);
        Assert.True(role.IsKiosk);
        Assert.Equal([Permissions.KioskUse], role.PermissionCodes);

        Assert.Throws<DomainException>(() => role.SetPermissions([Permissions.KioskUse, Permissions.PatientsRead]));
        Assert.Throws<DomainException>(() => role.SetPermissions([]));
        Assert.Equal([Permissions.KioskUse], role.PermissionCodes);
    }

    [Fact]
    public void KioskPermission_IsInTheCatalog_SoTheAdministratorGetsIt()
    {
        Assert.True(PermissionCatalog.IsDefined(Permissions.KioskUse));
        var admin = Role.Create(SystemRoles.Administrator, "Quản trị", null, isSystem: true);
        admin.GrantAllPermissions();
        Assert.Contains(Permissions.KioskUse, admin.PermissionCodes);
    }

    [Fact]
    public void KioskLookup_LocksAfterFiveFailures_ForFiveMinutes_SeparatelyFromLogin()
    {
        var user = User.Create("kiosk.kkb", "Kiosk Khoa Khám bệnh", Now);
        for (var i = 1; i < KioskLookupPolicy.MaxFailedAttempts; i++)
        {
            Assert.False(user.RegisterKioskLookupFailure(Now, KioskLookupPolicy.Default));
            Assert.Equal(i, user.KioskLookupFailures);
            Assert.False(user.IsKioskLookupLocked(Now));
        }

        Assert.True(user.RegisterKioskLookupFailure(Now, KioskLookupPolicy.Default));
        Assert.True(user.IsKioskLookupLocked(Now.AddMinutes(4)));
        Assert.False(user.IsKioskLookupLocked(Now.AddMinutes(5)));
        Assert.Equal(0, user.KioskLookupFailures);

        // Khóa tra cứu không khóa đăng nhập của máy kiosk.
        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(0, user.FailedLoginCount);
    }

    [Fact]
    public void KioskLookup_ResetsOnSuccess_AndWhenTheAdministratorUnlocksTheAccount()
    {
        var user = User.Create("kiosk.kkb", "Kiosk Khoa Khám bệnh", Now);
        user.RegisterKioskLookupFailure(Now, KioskLookupPolicy.Default);
        user.ResetKioskLookupFailures();
        Assert.Equal(0, user.KioskLookupFailures);

        for (var i = 0; i < KioskLookupPolicy.MaxFailedAttempts; i++)
        {
            user.RegisterKioskLookupFailure(Now, KioskLookupPolicy.Default);
        }

        Assert.True(user.IsKioskLookupLocked(Now));
        user.Activate();
        Assert.False(user.IsKioskLookupLocked(Now));
        Assert.Null(user.KioskLookupLockedUntilUtc);
    }

    [Fact]
    public void ConsentForm_RemembersHowThePatientIdentifiedOnTheKiosk()
    {
        var staffForm = ConsentForm.Create("PDT-2026-000001", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", Guid.NewGuid(), Now);
        Assert.Null(staffForm.KioskIdentity);

        var kioskForm = ConsentForm.Create(
            "PDT-2026-000002", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", Guid.NewGuid(), Now,
            kioskIdentity: KioskIdentityMethod.SelfReported);
        Assert.Equal(KioskIdentityMethod.SelfReported, kioskForm.KioskIdentity);

        Assert.Throws<DomainException>(() => ConsentForm.Create(
            "PDT-2026-000003", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "{}", Guid.NewGuid(), Now,
            kioskIdentity: (KioskIdentityMethod)42));
    }
}
