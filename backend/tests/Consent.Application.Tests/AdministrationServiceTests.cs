using Consent.Application.Auth;
using Consent.Application.Departments;
using Consent.Application.Roles;
using Consent.Application.Tests.Fakes;
using Consent.Application.Users;
using Consent.Domain.Authorization;
using Consent.Domain.ConsentTemplates;
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

    // Quyết định 41: một tài khoản thuộc nhiều khoa.
    [Fact]
    public async Task Create_WithOtherDepartments_SavesThem_IgnoringPrimaryAndDuplicates_AndShowsNamesSorted()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var ngoai = ctx.AddDepartment("NGOAI", "Khoa Ngoại");
        var capCuu = ctx.AddDepartment("CC", "Khoa Cấp cứu");
        var service = ctx.CreateUserAdminService();
        var request = NewUser(ctx.RoleByCode(SystemRoles.Nurse).Id, departmentId: noi.Id) with
        {
            AdditionalDepartmentIds = [ngoai.Id, noi.Id, capCuu.Id, ngoai.Id],
        };

        var result = await service.CreateAsync(request, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Khoa Nội", result.Value.DepartmentName);
        Assert.Equal(["Khoa Cấp cứu", "Khoa Ngoại"], result.Value.AdditionalDepartments.Select(d => d.Name));
        var user = Assert.Single(ctx.Store.Users);
        Assert.Equal([noi.Id, ngoai.Id, capCuu.Id], user.DepartmentIds);
    }

    [Fact]
    public async Task Update_ReplacesOtherDepartments_AndEmptyOrMissingListRemovesThem()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var ngoai = ctx.AddDepartment("NGOAI", "Khoa Ngoại");
        var capCuu = ctx.AddDepartment("CC", "Khoa Cấp cứu");
        var nurse = ctx.AddUser("dieuduong", role: SystemRoles.Nurse, departmentId: noi.Id);
        var service = ctx.CreateUserAdminService();
        UpdateUserRequest Request(IReadOnlyList<Guid>? others) =>
            new(nurse.FullName, null, null, null, noi.Id, null, [ctx.RoleByCode(SystemRoles.Nurse).Id], others);

        var added = await service.UpdateAsync(nurse.Id, Request([ngoai.Id]), CancellationToken.None);
        Assert.Equal([ngoai.Id], added.Value.AdditionalDepartments.Select(d => d.Id));

        var replaced = await service.UpdateAsync(nurse.Id, Request([capCuu.Id]), CancellationToken.None);
        Assert.Equal([capCuu.Id], replaced.Value.AdditionalDepartments.Select(d => d.Id));
        Assert.Equal([noi.Id, capCuu.Id], nurse.DepartmentIds);

        var removed = await service.UpdateAsync(nurse.Id, Request(null), CancellationToken.None);
        Assert.Empty(removed.Value.AdditionalDepartments);
        Assert.Equal([noi.Id], nurse.DepartmentIds);
    }

    [Fact]
    public async Task OtherDepartments_WithoutPrimary_Inactive_Unknown_OrTooMany_ReturnFieldErrors_AndSaveNothing()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var closed = ctx.AddDepartment("CU", "Khoa cũ", active: false);
        var service = ctx.CreateUserAdminService();
        var nurseRole = ctx.RoleByCode(SystemRoles.Nurse).Id;
        var many = Enumerable.Range(0, User.MaxAdditionalDepartments + 1)
            .Select(i => ctx.AddDepartment($"K{i}", $"Khoa {i}").Id).ToList();

        var withoutPrimary = await service.CreateAsync(
            NewUser(nurseRole) with { AdditionalDepartmentIds = [noi.Id] }, CancellationToken.None);
        var inactive = await service.CreateAsync(
            NewUser(nurseRole, departmentId: noi.Id) with { AdditionalDepartmentIds = [closed.Id] }, CancellationToken.None);
        var unknown = await service.CreateAsync(
            NewUser(nurseRole, departmentId: noi.Id) with { AdditionalDepartmentIds = [Guid.NewGuid()] }, CancellationToken.None);
        var tooMany = await service.CreateAsync(
            NewUser(nurseRole, departmentId: noi.Id) with { AdditionalDepartmentIds = many }, CancellationToken.None);

        Assert.Contains("chính", Assert.Single(withoutPrimary.Error!.FieldErrors!["additionalDepartmentIds"]), StringComparison.Ordinal);
        Assert.Contains("additionalDepartmentIds", inactive.Error!.FieldErrors!.Keys);
        Assert.Contains("additionalDepartmentIds", unknown.Error!.FieldErrors!.Keys);
        Assert.Contains(
            $"tối đa {User.MaxAdditionalDepartments}",
            Assert.Single(tooMany.Error!.FieldErrors!["additionalDepartmentIds"]),
            StringComparison.Ordinal);
        Assert.Empty(ctx.Store.Users);

        // Đúng giới hạn (khoa chính nằm trong danh sách thì không tính) thì được.
        var atLimit = await service.CreateAsync(
            NewUser(nurseRole, departmentId: many[0]) with { AdditionalDepartmentIds = many }, CancellationToken.None);
        Assert.True(atLimit.IsSuccess, atLimit.Error?.Message);
        Assert.Equal(User.MaxAdditionalDepartments, atLimit.Value.AdditionalDepartments.Count);
    }

    [Fact]
    public async Task KioskAccount_CannotHaveOtherDepartments()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var ngoai = ctx.AddDepartment("NGOAI", "Khoa Ngoại");
        var service = ctx.CreateUserAdminService();

        var result = await service.CreateAsync(
            NewUser(ctx.RoleByCode(SystemRoles.Kiosk).Id, "kiosk.noi", noi.Id) with { AdditionalDepartmentIds = [ngoai.Id] },
            CancellationToken.None);

        Assert.Contains("chỉ thuộc một khoa", Assert.Single(result.Error!.FieldErrors!["additionalDepartmentIds"]), StringComparison.Ordinal);
        Assert.Empty(ctx.Store.Users);
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
    public async Task Delete_DepartmentThatIsAnotherDepartmentOfSomeUser_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var noi = ctx.AddDepartment("NOI", "Khoa Nội");
        var ngoai = ctx.AddDepartment("NGOAI", "Khoa Ngoại");
        var nurse = ctx.AddUser("dieuduong", departmentId: noi.Id);
        nurse.SetDepartments(noi.Id, [ngoai.Id]);
        var service = ctx.CreateDepartmentService();

        var result = await service.DeleteAsync(ngoai.Id, CancellationToken.None);

        Assert.Equal("departments.in_use", result.Error!.Code);
        Assert.Equal(2, ctx.Store.Departments.Count);
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

    private const string KioskOnly = "Quyền dùng kiosk chỉ dành cho vai trò Kiosk người bệnh.";

    /// <summary>Định nghĩa mẫu tối thiểu, có (hoặc không) một bước xác nhận trung gian của vai trò <paramref name="stepRoleCode"/>.</summary>
    private static string Definition(string? stepRoleCode = null) =>
        stepRoleCode is null
            ? """{"title":"Phiếu thử","sections":[]}"""
            : $$$"""{"title":"Phiếu thử","sections":[],"signatures":{"steps":[{"key":"buoc1","roleCode":"{{{stepRoleCode}}}","label":"Bước 1"}]}}""";

    [Theory]
    [InlineData("Điều dưỡng trưởng khoa", "VT_DIEU_DUONG_TRUONG_KHOA")]
    [InlineData("  Kiểm   soát nhiễm khuẩn (KSNK) ", "VT_KIEM_SOAT_NHIEM_KHUAN_KSNK")]
    [InlineData("Ủy ban 2026", "VT_UY_BAN_2026")]
    [InlineData("!!!", "VT_VAI_TRO")]
    public void RoleCode_FromName_RemovesDiacritics_AndAddsThePrefix(string name, string expected) =>
        Assert.Equal(expected, RoleCodes.FromName(name, []));

    [Fact]
    public void RoleCode_FromName_AddsASuffixWhenTaken_AndFitsTemplateRoleCodes()
    {
        Assert.Equal("VT_DUOC_SI_3", RoleCodes.FromName("Dược sĩ", ["VT_DUOC_SI", "vt_duoc_si_2"]));
        var longCode = RoleCodes.FromName(new string('a', 200), []);
        Assert.True(longCode.Length <= Role.CodeMaxLength - 4, longCode);
        // Cấu hình ký của mẫu phiếu chỉ nhận mã dạng này.
        Assert.Matches("^[A-Z][A-Z0-9_]{0,49}$", RoleCodes.FromName("Phó giám đốc – kiêm trưởng phòng KHTH", []));
    }

    [Fact]
    public async Task Create_AddsACustomRole_WithGeneratedCode_AndPermissions()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var result = await service.CreateAsync(
            new CreateRoleRequest("  Dược sĩ lâm sàng ", "Khoa Dược", [Permissions.ConsentsSign, Permissions.ConsentsRead]),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("VT_DUOC_SI_LAM_SANG", result.Value.Code);
        Assert.Equal("Dược sĩ lâm sàng", result.Value.Name);
        Assert.Equal("Khoa Dược", result.Value.Description);
        Assert.False(result.Value.IsSystem);
        Assert.False(result.Value.IsLocked);
        Assert.Equal([Permissions.ConsentsRead, Permissions.ConsentsSign], result.Value.Permissions);
        Assert.Contains(ctx.Store.Roles, r => r.Id == result.Value.Id && !r.IsSystem);
    }

    [Fact]
    public async Task Create_NamesThatGiveTheSameCode_GetDifferentCodes()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var first = await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, null), CancellationToken.None);
        var second = await service.CreateAsync(new CreateRoleRequest("Duoc si", null, null), CancellationToken.None);

        Assert.Equal("VT_DUOC_SI", first.Value.Code);
        Assert.Equal("VT_DUOC_SI_2", second.Value.Code);
    }

    [Fact]
    public async Task Create_ValidatesName_Description_AndPermissions()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var empty = await service.CreateAsync(new CreateRoleRequest("   ", null, null), CancellationToken.None);
        var tooLong = await service.CreateAsync(new CreateRoleRequest(new string('a', Role.NameMaxLength + 1), null, null), CancellationToken.None);
        var sameName = await service.CreateAsync(new CreateRoleRequest("  bác   SĨ ", null, null), CancellationToken.None);
        var longDescription = await service.CreateAsync(
            new CreateRoleRequest("Dược sĩ", new string('a', Role.DescriptionMaxLength + 1), null), CancellationToken.None);
        var unknown = await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, ["khong.ton.tai"]), CancellationToken.None);
        var kiosk = await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, [Permissions.KioskUse]), CancellationToken.None);

        Assert.Equal(["Nhập tên vai trò."], empty.Error!.FieldErrors!["name"]);
        Assert.Contains("name", tooLong.Error!.FieldErrors!.Keys);
        Assert.StartsWith("Đã có vai trò tên", sameName.Error!.FieldErrors!["name"][0], StringComparison.Ordinal);
        Assert.Contains("description", longDescription.Error!.FieldErrors!.Keys);
        Assert.Contains("khong.ton.tai", unknown.Error!.FieldErrors!["permissions"][0], StringComparison.Ordinal);
        Assert.Equal([KioskOnly], kiosk.Error!.FieldErrors!["permissions"]);
        Assert.DoesNotContain(ctx.Store.Roles, r => !r.IsSystem);
    }

    [Fact]
    public async Task Update_RenamesEvenASystemRole_KeepingItsCode()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var nurse = ctx.RoleByCode(SystemRoles.Nurse);

        var result = await service.UpdateAsync(nurse.Id, new UpdateRoleRequest(" Điều dưỡng lâm sàng ", "Mô tả mới"), CancellationToken.None);
        var ownNameInCapitals = await service.UpdateAsync(nurse.Id, new UpdateRoleRequest("ĐIỀU DƯỠNG LÂM SÀNG", null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SystemRoles.Nurse, result.Value.Code);
        Assert.Equal("Điều dưỡng lâm sàng", result.Value.Name);
        Assert.Equal("Mô tả mới", result.Value.Description);
        Assert.True(ownNameInCapitals.IsSuccess);
        Assert.Equal("ĐIỀU DƯỠNG LÂM SÀNG", nurse.Name);
        Assert.Null(nurse.Description);
    }

    [Fact]
    public async Task Update_ToAnotherRolesName_OrAnUnknownRole_IsRejected()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var duplicate = await service.UpdateAsync(ctx.RoleByCode(SystemRoles.Nurse).Id, new UpdateRoleRequest("bác sĩ", null), CancellationToken.None);
        var missing = await service.UpdateAsync(Guid.NewGuid(), new UpdateRoleRequest("Dược sĩ", null), CancellationToken.None);

        Assert.Contains("name", duplicate.Error!.FieldErrors!.Keys);
        Assert.Equal("Điều dưỡng", ctx.RoleByCode(SystemRoles.Nurse).Name);
        Assert.Equal("roles.not_found", missing.Error!.Code);
    }

    [Fact]
    public async Task Delete_RemovesAnUnusedCustomRole()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var created = (await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, [Permissions.ConsentsRead]), CancellationToken.None)).Value;

        var result = await service.DeleteAsync(created.Id, CancellationToken.None);
        var again = await service.DeleteAsync(created.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(ctx.Store.Roles, r => r.Id == created.Id);
        Assert.Equal("roles.not_found", again.Error!.Code);
    }

    [Fact]
    public async Task Delete_ASystemRole_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();

        var result = await service.DeleteAsync(ctx.RoleByCode(SystemRoles.Reporter).Id, CancellationToken.None);

        Assert.Equal("roles.system_role", result.Error!.Code);
        Assert.Contains(ctx.Store.Roles, r => r.Code == SystemRoles.Reporter);
    }

    [Fact]
    public async Task Delete_ARoleStillAssigned_EvenToALockedAccount_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var created = (await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, null), CancellationToken.None)).Value;
        ctx.AddUser("duocsi.khoa", role: created.Code, active: false);

        var result = await service.DeleteAsync(created.Id, CancellationToken.None);

        Assert.Equal("roles.in_use", result.Error!.Code);
        Assert.Contains("1 tài khoản", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains(ctx.Store.Roles, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Delete_ARoleUsedInATemplatesSigningConfig_IsNotAllowed()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var finalSigner = (await service.CreateAsync(new CreateRoleRequest("Dược sĩ", null, null), CancellationToken.None)).Value;
        var stepRole = (await service.CreateAsync(new CreateRoleRequest("Kỹ thuật viên", null, null), CancellationToken.None)).Value;
        var unused = (await service.CreateAsync(new CreateRoleRequest("Hộ lý", null, null), CancellationToken.None)).Value;
        ctx.Store.ConsentTemplates.Add(
            ConsentTemplate.Create("THU-A", "Mẫu thử A", finalSigner.Code, null, Definition(), ctx.Clock.UtcNow));
        var withStep = ConsentTemplate.Create("THU-B", "Mẫu thử B", SystemRoles.Doctor, null, Definition(stepRole.Code), ctx.Clock.UtcNow);
        withStep.Publish(1, ctx.Clock.UtcNow);
        withStep.Update(withStep.Name, withStep.HospitalSignerRoleCode, null, isActive: false, ctx.Clock.UtcNow);
        ctx.Store.ConsentTemplates.Add(withStep);

        var byFinalSigner = await service.DeleteAsync(finalSigner.Id, CancellationToken.None);
        var byStep = await service.DeleteAsync(stepRole.Id, CancellationToken.None);
        var notUsed = await service.DeleteAsync(unused.Id, CancellationToken.None);

        Assert.Equal("roles.used_by_templates", byFinalSigner.Error!.Code);
        Assert.Contains("Mẫu thử A", byFinalSigner.Error.Message, StringComparison.Ordinal);
        // Mẫu đã ngừng dùng vẫn tính: phiếu đã lập theo mẫu đó có thể còn chờ xác nhận.
        Assert.Equal("roles.used_by_templates", byStep.Error!.Code);
        Assert.Contains("Mẫu thử B", byStep.Error.Message, StringComparison.Ordinal);
        Assert.True(notUsed.IsSuccess);
    }

    [Fact]
    public async Task UpdatePermissions_GivingTheKioskPermissionToAnotherRole_IsRejected()
    {
        var ctx = new AppTestContext();
        var service = ctx.CreateRoleService();
        var receptionist = ctx.RoleByCode(SystemRoles.Receptionist);
        var before = receptionist.PermissionCodes.ToList();

        var result = await service.UpdatePermissionsAsync(
            receptionist.Id, new UpdateRolePermissionsRequest([Permissions.KioskUse, Permissions.PatientsRead]), CancellationToken.None);

        Assert.Equal([KioskOnly], result.Error!.FieldErrors!["permissions"]);
        Assert.Equal(before, receptionist.PermissionCodes);
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
