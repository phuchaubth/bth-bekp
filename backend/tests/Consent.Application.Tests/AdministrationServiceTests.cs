using Consent.Application.Auth;
using Consent.Application.Departments;
using Consent.Application.Roles;
using Consent.Application.Tests.Fakes;
using Consent.Application.Users;
using Consent.Domain.Authorization;
using Consent.Domain.Identity;
using Consent.Shared;

namespace Consent.Application.Tests;

public sealed class UserAdminServiceTests
{
    private static CreateUserRequest NewUser(Guid roleId, string userName = "dieuduong.moi", Guid? departmentId = null) =>
        new(userName, "Điều dưỡng Mới", "dd@example.test", "0900000000", "Điều dưỡng", departmentId, null, [roleId], "Mat-Khau-Tam-2026");

    [Fact]
    public async Task Create_HashesPassword_AndRequiresPasswordChange()
    {
        var ctx = new AppTestContext();
        var department = ctx.AddDepartment("NOI", "Khoa Nội");
        var service = ctx.CreateUserAdminService();

        var result = await service.CreateAsync(NewUser(ctx.RoleByCode(SystemRoles.Nurse).Id, departmentId: department.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.MustChangePassword);
        Assert.Equal("Khoa Nội", result.Value.DepartmentName);
        var user = Assert.Single(ctx.Store.Users);
        Assert.Equal("hashed:Mat-Khau-Tam-2026", user.PasswordHash);
    }

    [Fact]
    public async Task Create_WithExistingUserName_ReturnsConflict()
    {
        var ctx = new AppTestContext();
        ctx.AddUser("dieuduong.moi");
        var service = ctx.CreateUserAdminService();

        var result = await service.CreateAsync(NewUser(ctx.RoleByCode(SystemRoles.Nurse).Id), CancellationToken.None);

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
    }

    [Fact]
    public async Task Create_WithUnknownRole_OrInactiveDepartment_ReturnsFieldErrors()
    {
        var ctx = new AppTestContext();
        var closed = ctx.AddDepartment("CU", "Khoa cũ", active: false);
        var service = ctx.CreateUserAdminService();

        var unknownRole = await service.CreateAsync(NewUser(Guid.NewGuid()), CancellationToken.None);
        var inactiveDepartment = await service.CreateAsync(
            NewUser(ctx.RoleByCode(SystemRoles.Nurse).Id, departmentId: closed.Id), CancellationToken.None);

        Assert.Contains("roleIds", unknownRole.Error!.FieldErrors!.Keys);
        Assert.Contains("departmentId", inactiveDepartment.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Create_WithWeakPasswordAndBadEmail_ReturnsVietnameseFieldErrors()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateUserAdminService();
        var request = NewUser(ctx.RoleByCode(SystemRoles.Nurse).Id) with { Password = "123", Email = "khong-hop-le" };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.Contains("password", result.Error!.FieldErrors!.Keys);
        Assert.Equal(["Email không hợp lệ."], result.Error.FieldErrors["email"]);
    }

    [Fact]
    public async Task Lock_Self_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var admin = ctx.AddUser("admin1", role: SystemRoles.Administrator);
        ctx.CurrentUser.UserId = admin.Id;
        var service = ctx.CreateUserAdminService();

        var result = await service.LockAsync(admin.Id, CancellationToken.None);

        Assert.Equal("users.cannot_lock_self", result.Error!.Code);
    }

    [Fact]
    public async Task Lock_LastActiveAdministrator_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var admin = ctx.AddUser("admin1", role: SystemRoles.Administrator);
        ctx.CurrentUser.UserId = Guid.NewGuid();
        var service = ctx.CreateUserAdminService();

        var result = await service.LockAsync(admin.Id, CancellationToken.None);

        Assert.Equal("users.last_administrator", result.Error!.Code);
        Assert.True(admin.IsActive);
    }

    [Fact]
    public async Task Lock_DeactivatesUser_AndRevokesSessions()
    {
        var ctx = new AppTestContext();
        var admin = ctx.AddUser("admin1", role: SystemRoles.Administrator);
        var doctor = ctx.AddUser("bacsi");
        ctx.CurrentUser.UserId = admin.Id;
        await ctx.CreateAuthService().LoginAsync(new LoginRequest("bacsi", AppTestContext.DefaultPassword), CancellationToken.None);
        var service = ctx.CreateUserAdminService();

        var result = await service.LockAsync(doctor.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(doctor.IsActive);
        Assert.All(ctx.Store.RefreshTokens.Where(t => t.UserId == doctor.Id), t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Update_RemovingAdministratorRoleFromLastAdministrator_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var admin = ctx.AddUser("admin1", role: SystemRoles.Administrator);
        var service = ctx.CreateUserAdminService();
        var request = new UpdateUserRequest(admin.FullName, null, null, null, null, null, [ctx.RoleByCode(SystemRoles.Doctor).Id]);

        var result = await service.UpdateAsync(admin.Id, request, CancellationToken.None);

        Assert.Equal("users.last_administrator", result.Error!.Code);
    }

    [Fact]
    public async Task ResetPassword_RequiresChangeAtNextLogin_AndRevokesSessions()
    {
        var ctx = new AppTestContext();
        var doctor = ctx.AddUser("bacsi");
        await ctx.CreateAuthService().LoginAsync(new LoginRequest("bacsi", AppTestContext.DefaultPassword), CancellationToken.None);
        var service = ctx.CreateUserAdminService();

        var result = await service.ResetPasswordAsync(doctor.Id, new ResetPasswordRequest("Mat-Khau-Cap-Lai-2026"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(doctor.MustChangePassword);
        Assert.All(ctx.Store.RefreshTokens, t => Assert.True(t.IsRevoked));
    }

    [Fact]
    public async Task Unlock_ReactivatesUser()
    {
        var ctx = new AppTestContext();
        var doctor = ctx.AddUser("bacsi", active: false);
        var service = ctx.CreateUserAdminService();

        var result = await service.UnlockAsync(doctor.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(doctor.IsActive);
    }
}

public sealed class DepartmentServiceTests
{
    [Fact]
    public async Task Create_WithDuplicateCode_ReturnsConflict_CaseInsensitive()
    {
        var ctx = new AppTestContext();
        ctx.AddDepartment("KKB", "Khoa Khám bệnh");
        var service = ctx.CreateDepartmentService();

        var result = await service.CreateAsync(new DepartmentRequest("kkb", "Trùng"), CancellationToken.None);

        Assert.Equal("departments.code_exists", result.Error!.Code);
    }

    [Fact]
    public async Task Create_WithAccentedCode_ReturnsFieldError()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateDepartmentService();

        var result = await service.CreateAsync(new DepartmentRequest("KHOA-NỘI", "Khoa Nội"), CancellationToken.None);

        Assert.Contains("code", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Delete_DepartmentWithUsers_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var department = ctx.AddDepartment("NOI", "Khoa Nội");
        ctx.AddUser("bacsi", departmentId: department.Id);
        var service = ctx.CreateDepartmentService();

        var result = await service.DeleteAsync(department.Id, CancellationToken.None);

        Assert.Equal("departments.in_use", result.Error!.Code);
        Assert.Single(ctx.Store.Departments);
    }

    [Fact]
    public async Task Delete_UnusedDepartment_Succeeds()
    {
        var ctx = new AppTestContext();
        var department = ctx.AddDepartment("NOI", "Khoa Nội");
        var service = ctx.CreateDepartmentService();

        var result = await service.DeleteAsync(department.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(ctx.Store.Departments);
    }
}

public sealed class RoleServiceTests
{
    [Fact]
    public async Task UpdatePermissions_ForAdministrator_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var result = await service.UpdatePermissionsAsync(
            ctx.RoleByCode(SystemRoles.Administrator).Id, new UpdateRolePermissionsRequest([]), CancellationToken.None);

        Assert.Equal("roles.admin_immutable", result.Error!.Code);
    }

    [Fact]
    public async Task UpdatePermissions_WithUnknownCode_ReturnsFieldError()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var result = await service.UpdatePermissionsAsync(
            ctx.RoleByCode(SystemRoles.Doctor).Id, new UpdateRolePermissionsRequest(["khong.ton.tai"]), CancellationToken.None);

        Assert.Contains("permissions", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task UpdatePermissions_ReplacesPermissions()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var doctor = ctx.RoleByCode(SystemRoles.Doctor);

        var result = await service.UpdatePermissionsAsync(
            doctor.Id, new UpdateRolePermissionsRequest([Permissions.ConsentsRead, Permissions.ReportsRead]), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([Permissions.ConsentsRead, Permissions.ReportsRead], result.Value.Permissions);
    }

    [Fact]
    public async Task List_PutsAdministratorFirst_ThenSortsByVietnameseName()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var roles = await service.ListAsync(CancellationToken.None);

        Assert.Equal(SystemRoles.Administrator, roles[0].Code);
        var names = roles.Skip(1).Select(r => r.Name).ToList();
        Assert.True(names.IndexOf("Điều dưỡng") < names.IndexOf("Kiểm tra"), "Đ phải đứng trước K theo thứ tự tiếng Việt.");
    }

    [Fact]
    public void PermissionResolver_GivesAdministratorAllPermissions_AndMergesOthers()
    {
        var ctx = new AppTestContext();

        var admin = PermissionResolver.Resolve([ctx.RoleByCode(SystemRoles.Administrator)]);
        var merged = PermissionResolver.Resolve([ctx.RoleByCode(SystemRoles.Doctor), ctx.RoleByCode(SystemRoles.Auditor)]);

        Assert.Equal(PermissionCatalog.AllCodes.Count, admin.Count);
        Assert.Contains(Permissions.ConsentsCreate, merged);
        Assert.Contains(Permissions.AuditRead, merged);
        Assert.Equal(merged.Count, merged.Distinct().Count());
    }
}
