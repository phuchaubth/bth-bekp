using Consent.Domain.Identity;

namespace Consent.Domain.Tests;

public sealed class PasswordPolicyTests
{
    [Fact]
    public void StrongPassword_IsValid()
    {
        Assert.Empty(PasswordPolicy.Validate("Mat-Khau-Manh-2026", "bacsi.demo"));
    }

    [Theory]
    [InlineData("Ngan@1", "ít nhất 10 ký tự")]
    [InlineData("khongchuhoa@2026", "chữ hoa")]
    [InlineData("KHONGCHUTHUONG@2026", "chữ thường")]
    [InlineData("KhongChuSo@abcdef", "chữ số")]
    [InlineData("KhongKyTuDacBiet2026", "ký tự đặc biệt")]
    public void WeakPassword_ReportsVietnameseReason(string password, string expectedFragment)
    {
        var errors = PasswordPolicy.Validate(password);

        Assert.Contains(errors, e => e.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void PasswordContainingUserName_IsRejected()
    {
        var errors = PasswordPolicy.Validate("Admin@Manh2026", "admin");

        Assert.Contains(errors, e => e.Contains("tên đăng nhập", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyPassword_IsRejected()
    {
        Assert.NotEmpty(PasswordPolicy.Validate(""));
        Assert.NotEmpty(PasswordPolicy.Validate(null));
    }
}
