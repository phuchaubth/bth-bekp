namespace Consent.Domain.Identity;

/// <summary>Chính sách độ mạnh mật khẩu.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// <summary>Trả về danh sách lỗi (tiếng Việt); rỗng nếu mật khẩu hợp lệ.</summary>
    public static IReadOnlyList<string> Validate(string? password, string? userName = null)
    {
        var errors = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            errors.Add("Mật khẩu không được để trống.");
            return errors;
        }

        if (password.Length < MinLength)
        {
            errors.Add($"Mật khẩu phải có ít nhất {MinLength} ký tự.");
        }

        if (password.Length > MaxLength)
        {
            errors.Add($"Mật khẩu không được dài quá {MaxLength} ký tự.");
        }

        if (!password.Any(char.IsLower))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ thường.");
        }

        if (!password.Any(char.IsUpper))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ hoa.");
        }

        if (!password.Any(char.IsDigit))
        {
            errors.Add("Mật khẩu phải có ít nhất một chữ số.");
        }

        if (password.All(char.IsLetterOrDigit))
        {
            errors.Add("Mật khẩu phải có ít nhất một ký tự đặc biệt.");
        }

        if (!string.IsNullOrWhiteSpace(userName)
            && userName.Trim().Length >= User.UserNameMinLength
            && password.Contains(userName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Mật khẩu không được chứa tên đăng nhập.");
        }

        return errors;
    }
}
