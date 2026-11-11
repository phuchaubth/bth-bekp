using Consent.Domain.Common;

namespace Consent.Domain.Identity;

/// <summary>
/// Refresh token (chỉ lưu giá trị băm). Mỗi lần làm mới, token cũ bị thu hồi và thay bằng token mới
/// cùng "họ" (family). Dùng lại token đã bị thay thế được coi là bị đánh cắp: cả họ token bị thu hồi.
/// </summary>
public sealed class RefreshToken : Entity
{
    public const int TokenHashLength = 64;
    public const int ReasonMaxLength = 50;
    public const int IpMaxLength = 64;

    private RefreshToken(
        Guid id, Guid userId, string tokenHash, Guid familyId, DateTimeOffset createdAtUtc, DateTimeOffset expiresAtUtc, string? createdByIp)
        : base(id)
    {
        UserId = userId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIp = createdByIp;
    }

    public Guid UserId { get; private set; }

    /// <summary>SHA-256 (hex) của token. Không lưu token gốc.</summary>
    public string TokenHash { get; private set; }

    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public string? CreatedByIp { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public string? RevokedReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    /// <summary>Token đã được dùng để làm mới và bị thay thế bởi token khác.</summary>
    public bool WasRotated => ReplacedByTokenId is not null;

    public static RefreshToken Issue(
        Guid userId, string tokenHash, Guid familyId, DateTimeOffset nowUtc, TimeSpan lifetime, string? createdByIp)
    {
        if (string.IsNullOrWhiteSpace(tokenHash) || tokenHash.Length != TokenHashLength)
        {
            throw new DomainException("Giá trị băm của refresh token không hợp lệ.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("Thời hạn refresh token phải lớn hơn 0.");
        }

        var ip = createdByIp is { Length: > IpMaxLength } ? createdByIp[..IpMaxLength] : createdByIp;
        return new RefreshToken(Guid.CreateVersion7(), userId, tokenHash, familyId, nowUtc, nowUtc + lifetime, ip);
    }

    public bool IsExpired(DateTimeOffset nowUtc) => nowUtc >= ExpiresAtUtc;

    public bool IsActive(DateTimeOffset nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    public void Revoke(DateTimeOffset nowUtc, string reason, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAtUtc = nowUtc;
        RevokedReason = reason.Length > ReasonMaxLength ? reason[..ReasonMaxLength] : reason;
        ReplacedByTokenId = replacedByTokenId;
    }
}

/// <summary>Lý do thu hồi refresh token.</summary>
public static class RefreshTokenRevocationReasons
{
    public const string Rotated = "rotated";
    public const string Logout = "logout";
    public const string ReuseDetected = "reuse_detected";
    public const string PasswordChanged = "password_changed";
    public const string UserDeactivated = "user_deactivated";
    public const string PasswordReset = "password_reset";
    public const string UserUnavailable = "user_unavailable";
}
