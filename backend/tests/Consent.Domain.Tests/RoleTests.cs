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
    public void SystemRoleDefaults_OnlyUseDefinedPermissions_AndHaveUniqueCodes()
    {
        Assert.All(SystemRoles.Defaults, definition =>
            Assert.All(definition.Permissions, code => Assert.True(PermissionCatalog.IsDefined(code), code)));
        Assert.Equal(SystemRoles.Defaults.Count, SystemRoles.Defaults.Select(d => d.Code).Distinct().Count());
    }

    [Fact]
    public void PermissionCatalog_HasUniqueCodes_AndVietnameseNames()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.AllCodes.Count);
        Assert.All(PermissionCatalog.All, p => Assert.False(string.IsNullOrWhiteSpace(p.Name)));
    }
}
