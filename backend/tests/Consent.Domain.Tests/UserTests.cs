using Consent.Domain.Common;
using Consent.Domain.Identity;

namespace Consent.Domain.Tests;

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 8, 0, 0, TimeSpan.Zero);
    private static readonly LockoutPolicy Policy = new(maxFailedAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));

    [Fact]
    public void Create_NormalizesUserNameToLowerCase_AndIsActive()
    {
        var user = User.Create("  BacSi.Demo ", "Bác sĩ Demo", Now);

        Assert.Equal("bacsi.demo", user.UserName);
        Assert.True(user.IsActive);
        Assert.Equal(Now, user.CreatedAtUtc);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("")]
    public void Create_WithTooShortUserName_Throws(string userName)
    {
        Assert.Throws<DomainException>(() => User.Create(userName, "Họ tên", Now));
    }

    [Fact]
    public void FifthFailedLogin_LocksAccount_ForConfiguredDuration()
    {
        var user = User.Create("tiepdon.demo", "Tiếp đón Demo", Now);

        for (var i = 0; i < 4; i++)
        {
            Assert.False(user.RegisterFailedLogin(Now, Policy));
        }

        var lockedNow = user.RegisterFailedLogin(Now, Policy);

        Assert.True(lockedNow);
        Assert.True(user.IsLockedOut(Now));
        Assert.True(user.IsLockedOut(Now.AddMinutes(14)));
        Assert.False(user.IsLockedOut(Now.AddMinutes(15)));
    }

    [Fact]
    public void AfterLockoutExpires_UserGetsFreshAttempts()
    {
        var user = User.Create("tiepdon.demo", "Tiếp đón Demo", Now);
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(Now, Policy);
        }

        var later = Now.AddMinutes(20);

        Assert.False(user.RegisterFailedLogin(later, Policy));
        Assert.False(user.IsLockedOut(later));
    }

    [Fact]
    public void SuccessfulLogin_ResetsFailures_AndRecordsLoginTime()
    {
        var user = User.Create("bacsi.demo", "Bác sĩ Demo", Now);
        user.RegisterFailedLogin(Now, Policy);
        user.RegisterFailedLogin(Now, Policy);

        user.RegisterSuccessfulLogin(Now.AddMinutes(1));

        Assert.Equal(0, user.FailedLoginCount);
        Assert.Null(user.LockoutEndUtc);
        Assert.Equal(Now.AddMinutes(1), user.LastLoginAtUtc);
    }

    [Fact]
    public void Activate_ClearsTemporaryLockout()
    {
        var user = User.Create("bacsi.demo", "Bác sĩ Demo", Now);
        for (var i = 0; i < 5; i++)
        {
            user.RegisterFailedLogin(Now, Policy);
        }

        user.Deactivate();
        user.Activate();

        Assert.True(user.IsActive);
        Assert.False(user.IsLockedOut(Now));
    }

    [Fact]
    public void SetRoles_ReplacesRoles_AndRejectsEmptySet()
    {
        var user = User.Create("bacsi.demo", "Bác sĩ Demo", Now);
        var roleA = Guid.NewGuid();
        var roleB = Guid.NewGuid();
        var roleC = Guid.NewGuid();

        user.SetRoles([roleA, roleB]);
        user.SetRoles([roleB, roleC, roleC]);

        Assert.Equal(new[] { roleB, roleC }.Order(), user.RoleIds.Order());
        Assert.Throws<DomainException>(() => user.SetRoles([]));
    }

    [Fact]
    public void UpdateProfile_TrimsValues_AndConvertsBlankToNull()
    {
        var user = User.Create("bacsi.demo", "Bác sĩ Demo", Now);

        user.UpdateProfile("  Bác sĩ Demo 2 ", "  ", null, " Bác sĩ CKI ", "");

        Assert.Equal("Bác sĩ Demo 2", user.FullName);
        Assert.Null(user.Email);
        Assert.Equal("Bác sĩ CKI", user.Title);
        Assert.Null(user.SmartCaUserId);
    }

    // Quyết định 41: một tài khoản thuộc nhiều khoa (khoa chính và các khoa/phòng khác).
    [Fact]
    public void SetDepartments_KeepsPrimaryFirst_IgnoresPrimaryAndDuplicatesInOthers_AndReplacesOthers()
    {
        var user = User.Create("dieuduong.thu", "Điều dưỡng Thử", Now);
        Guid primary = Guid.CreateVersion7(), second = Guid.CreateVersion7(), third = Guid.CreateVersion7();

        user.SetDepartments(primary, [second, primary, second, third]);

        Assert.Equal(primary, user.DepartmentId);
        Assert.Equal([second, third], user.AdditionalDepartmentIds);
        Assert.Equal([primary, second, third], user.DepartmentIds);
        Assert.All(user.AdditionalDepartments, d => Assert.Equal(user.Id, d.UserId));

        // Đổi khoa chính sang một khoa khác: khoa đó không còn nằm trong danh sách khoa khác.
        user.SetDepartments(third, [primary, third]);

        Assert.Equal(third, user.DepartmentId);
        Assert.Equal([primary], user.AdditionalDepartmentIds);
        Assert.Equal([third, primary], user.DepartmentIds);

        user.SetDepartments(third, []);
        Assert.Empty(user.AdditionalDepartments);
        Assert.Equal([third], user.DepartmentIds);

        user.SetDepartments(null, []);
        Assert.Null(user.DepartmentId);
        Assert.Empty(user.DepartmentIds);
    }

    [Fact]
    public void SetDepartments_RejectsOthersWithoutPrimary_EmptyIds_AndTooMany()
    {
        var user = User.Create("dieuduong.thu", "Điều dưỡng Thử", Now);
        var primary = Guid.CreateVersion7();
        user.SetDepartments(primary, [Guid.CreateVersion7()]);
        var before = user.DepartmentIds;

        var withoutPrimary = Assert.Throws<DomainException>(() => user.SetDepartments(null, [Guid.CreateVersion7()]));
        Assert.Contains("khoa/phòng chính", withoutPrimary.Message, StringComparison.Ordinal);
        Assert.Throws<DomainException>(() => user.SetDepartments(primary, [Guid.Empty]));
        Assert.Throws<DomainException>(() => user.SetDepartments(Guid.Empty, []));
        var tooMany = Enumerable.Range(0, User.MaxAdditionalDepartments + 1).Select(_ => Guid.CreateVersion7()).ToList();
        Assert.Throws<DomainException>(() => user.SetDepartments(primary, tooMany));

        // Lỗi thì không đổi gì.
        Assert.Equal(before, user.DepartmentIds);

        // Đúng giới hạn thì được.
        user.SetDepartments(primary, tooMany.Take(User.MaxAdditionalDepartments));
        Assert.Equal(User.MaxAdditionalDepartments + 1, user.DepartmentIds.Count);
    }

    [Fact]
    public void SignatureSpecimen_IsSetAndCleared_AndCannotBeEmpty()
    {
        var user = User.Create("bacsi.thu", "Bác sĩ Thử", DateTimeOffset.UnixEpoch);

        Assert.Throws<DomainException>(() => user.SetSignatureSpecimen([], DateTimeOffset.UnixEpoch));
        user.SetSignatureSpecimen([1, 2, 3], DateTimeOffset.UnixEpoch.AddDays(1));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddDays(1), user.SignatureSpecimenUpdatedAtUtc);
        user.ClearSignatureSpecimen();
        Assert.Null(user.SignatureSpecimenPng);
        Assert.Null(user.SignatureSpecimenUpdatedAtUtc);
    }

    [Fact]
    public void Delete_OnlyAStoppedAccount_KeepsTheName_DropsRolesAndDepartments_AndCannotBeUsedAgain()
    {
        var user = User.Create("dieuduong.nghi", "Điều dưỡng Nghỉ việc", Now);
        user.SetRoles([Guid.NewGuid()]);
        user.SetDepartments(Guid.NewGuid(), [Guid.NewGuid()]);
        var admin = Guid.NewGuid();
        var later = Now.AddDays(30);

        // Đang sử dụng thì chưa xóa được (phải ngưng sử dụng trước).
        Assert.Throws<DomainException>(() => user.Delete(admin, later));
        Assert.False(user.IsDeleted);

        user.Deactivate();
        Assert.Throws<DomainException>(() => user.Delete(Guid.Empty, later));
        user.Delete(admin, later);

        Assert.True(user.IsDeleted);
        Assert.Equal(later, user.DeletedAtUtc);
        Assert.Equal(admin, user.DeletedByUserId);
        Assert.Equal("Điều dưỡng Nghỉ việc", user.FullName);
        Assert.Equal("dieuduong.nghi", user.UserName);
        Assert.Empty(user.RoleIds);
        Assert.Null(user.DepartmentId);
        Assert.Empty(user.AdditionalDepartmentIds);
        Assert.Empty(user.DepartmentIds);
        Assert.False(user.IsActive);
        Assert.Throws<DomainException>(() => user.Activate());
        Assert.False(user.IsActive);
        Assert.Throws<DomainException>(() => user.Delete(admin, later));
    }
}
