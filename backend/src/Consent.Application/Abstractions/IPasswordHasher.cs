namespace Consent.Application.Abstractions;

public enum PasswordVerification
{
    Failed,
    Success,

    /// <summary>Đúng mật khẩu nhưng nên băm lại bằng thuật toán/tham số mới.</summary>
    SuccessRehashNeeded,
}

public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerification Verify(string passwordHash, string password);
}
