using Consent.Application.Abstractions;
using Consent.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Consent.Infrastructure.Security;

/// <summary>Băm mật khẩu bằng thuật toán của ASP.NET Core Identity (PBKDF2, có salt, hỗ trợ nâng cấp tham số).</summary>
internal sealed class IdentityPasswordHasher : Application.Abstractions.IPasswordHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _hasher.HashPassword(null!, password);
    }

    public PasswordVerification Verify(string passwordHash, string password)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(password))
        {
            return PasswordVerification.Failed;
        }

        try
        {
            return _hasher.VerifyHashedPassword(null!, passwordHash, password) switch
            {
                PasswordVerificationResult.Success => PasswordVerification.Success,
                PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
                _ => PasswordVerification.Failed,
            };
        }
        catch (FormatException)
        {
            // Chuỗi băm không đúng định dạng: coi như sai mật khẩu.
            return PasswordVerification.Failed;
        }
    }
}
