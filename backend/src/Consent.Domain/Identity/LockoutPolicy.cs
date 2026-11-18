using Consent.Domain.Common;

namespace Consent.Domain.Identity;

/// <summary>Chính sách khóa tạm thời tài khoản khi đăng nhập sai nhiều lần.</summary>
public sealed record LockoutPolicy
{
    public LockoutPolicy(int maxFailedAttempts, TimeSpan lockoutDuration)
    {
        if (maxFailedAttempts < 1)
        {
            throw new DomainException("Số lần đăng nhập sai tối đa phải lớn hơn 0.");
        }

        if (lockoutDuration <= TimeSpan.Zero)
        {
            throw new DomainException("Thời gian khóa phải lớn hơn 0.");
        }

        MaxFailedAttempts = maxFailedAttempts;
        LockoutDuration = lockoutDuration;
    }

    public int MaxFailedAttempts { get; }

    public TimeSpan LockoutDuration { get; }
}
