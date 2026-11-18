using Consent.Domain.Authorization;
using Consent.Domain.Common;
using Consent.Domain.Identity;

namespace Consent.Domain.Tests;

public sealed class RoleTests
{
    [Fact]
    public void SetPermissions_ReplacesPermissionSet()
    {
        var role = Role.Create("DOCTOR", "Bác sĩ", null, isSystem: true);

        role.SetPermissions([Permissions.ConsentsRead, Permissions.ConsentsCreate]);
        role.SetPermissions([Permissions.ConsentsRead, Permissions.PatientsRead]);

        Assert.Equal([Permissions.ConsentsRead, Permissions.PatientsRead], role.PermissionCodes.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SetPermissions_WithUnknownPermission_Throws()
    {
        var role = Role.Create("DOCTOR", "Bác sĩ", null, isSystem: true);

        Assert.Throws<DomainException>(() => role.SetPermissions(["khong.ton.tai"]));
    }

    [Fact]
    public void AdministratorRole_PermissionsCannotBeEdited_ButCanBeGrantedAll()
    {
        var admin = Role.Create(SystemRoles.Administrator, "Quản trị hệ thống", null, isSystem: true);

        admin.GrantAllPermissions();

        Assert.True(admin.IsAdministrator);
        Assert.Equal(PermissionCatalog.AllCodes.Count, admin.Permissions.Count);
        Assert.Throws<DomainException>(() => admin.SetPermissions([Permissions.ConsentsRead]));
    }

    [Fact]
    public void UpdateDetails_ChangesNameAndDescription_ButNotTheCode()
    {
        var role = Role.Create("VT_DUOC_SI", "Dược sĩ", "Mô tả cũ", isSystem: false);

        role.UpdateDetails("  Dược sĩ lâm sàng  ", "   ");

        Assert.Equal("VT_DUOC_SI", role.Code);
        Assert.Equal("Dược sĩ lâm sàng", role.Name);
        Assert.Null(role.Description);
    }

    [Fact]
    public void NameAndDescription_MustBeWithinLimits()
    {
        var role = Role.Create("VT_DUOC_SI", "Dược sĩ", null, isSystem: false);

        Assert.Throws<DomainException>(() => role.UpdateDetails("   ", null));
        Assert.Throws<DomainException>(() => role.UpdateDetails(new string('a', Role.NameMaxLength + 1), null));
        Assert.Throws<DomainException>(() => role.UpdateDetails("Dược sĩ", new string('a', Role.DescriptionMaxLength + 1)));
        Assert.Throws<DomainException>(() =>
            Role.Create("VT_DUOC_SI", "Dược sĩ", new string('a', Role.DescriptionMaxLength + 1), isSystem: false));
        Assert.Equal("Dược sĩ", role.Name);
    }

    [Fact]
    public void KioskPermission_BelongsOnlyToTheKioskRole()
    {
        var custom = Role.Create("VT_TIEP_DON_KIOSK", "Tiếp đón kiosk", null, isSystem: false);
        var doctor = Role.Create(SystemRoles.Doctor, "Bác sĩ", null, isSystem: true);
        var kiosk = Role.Create(SystemRoles.Kiosk, "Kiosk người bệnh", null, isSystem: true);

        Assert.Throws<DomainException>(() => custom.SetPermissions([Permissions.KioskUse, Permissions.PatientsRead]));
        Assert.Throws<DomainException>(() => doctor.SetPermissions([Permissions.KioskUse]));
        kiosk.SetPermissions(SystemRoles.KioskPermissions);

        Assert.Empty(custom.Permissions);
        Assert.Equal([Permissions.KioskUse], kiosk.PermissionCodes);
    }

    [Fact]
    public void SystemRoleDefaults_OnlyUseDefinedPermissions_AndHaveUniqueCodes()
    {
        Assert.All(SystemRoles.Defaults, definition =>
            Assert.All(definition.Permissions, code => Assert.True(PermissionCatalog.IsDefined(code), code)));
        Assert.Equal(SystemRoles.Defaults.Count, SystemRoles.Defaults.Select(d => d.Code).Distinct().Count());
        // Mã vai trò do quản trị tạo có tiền tố riêng, không trùng mã vai trò có sẵn (quyết định 40).
        Assert.DoesNotContain(SystemRoles.Defaults, d => d.Code.StartsWith(Role.CustomCodePrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void PermissionCatalog_HasUniqueCodes_AndVietnameseNames()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.AllCodes.Count);
        Assert.All(PermissionCatalog.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }
}
