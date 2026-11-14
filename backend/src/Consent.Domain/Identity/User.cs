using Consent.Domain.Common;

namespace Consent.Domain.Identity;

/// <summary>Tài khoản nhân viên bệnh viện.</summary>
public sealed class User : Entity, IAuditableEntity
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 50;
    public const int FullNameMaxLength = 200;
    public const int EmailMaxLength = 256;
    public const int PhoneNumberMaxLength = 20;
    public const int TitleMaxLength = 100;
    public const int SmartCaUserIdMaxLength = 50;
    public const int PasswordHashMaxLength = 500;

    private readonly List<UserRole> _roles = [];

    private User(Guid id, string userName, string fullName, DateTimeOffset createdAtUtc)
        : base(id)
    {
        UserName = userName;
        FullName = fullName;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    /// <summary>Tên đăng nhập, đã chuẩn hóa về chữ thường.</summary>
    public string UserName { get; private set; }

    public string FullName { get; private set; }

    public string? Email { get; private set; }

    public string? PhoneNumber { get; private set; }

    /// <summary>Chức danh, ví dụ "Bác sĩ CKI".</summary>
    public string? Title { get; private set; }

    public Guid? DepartmentId { get; private set; }

    /// <summary>Định danh tài khoản VNPT SmartCA (thường là số CCCD). Không phải bí mật.</summary>
    public string? SmartCaUserId { get; private set; }

    [SensitiveData]
    public string PasswordHash { get; private set; } = string.Empty;

    /// <summary>Bắt buộc đổi mật khẩu ở lần đăng nhập tiếp theo.</summary>
    public bool MustChangePassword { get; private set; }

    /// <summary>Tài khoản được phép đăng nhập. Quản trị viên khóa/mở khóa bằng cờ này.</summary>
    public bool IsActive { get; private set; }

    public int FailedLoginCount { get; private set; }

    /// <summary>Thời điểm hết khóa tạm thời do đăng nhập sai nhiều lần.</summary>
    public DateTimeOffset? LockoutEndUtc { get; private set; }

    public DateTimeOffset? LastLoginAtUtc { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles;

    public IReadOnlyList<Guid> RoleIds => _roles.ConvertAll(r => r.RoleId);

    public static User Create(string userName, string fullName, DateTimeOffset nowUtc)
    {
        var normalized = NormalizeUserName(userName);
        if (normalized.Length is < UserNameMinLength or > UserNameMaxLength)
        {
            throw new DomainException($"Tên đăng nhập phải có từ {UserNameMinLength} đến {UserNameMaxLength} ký tự.");
        }

        return new User(Guid.CreateVersion7(), normalized, RequireFullName(fullName), nowUtc);
    }

    public static string NormalizeUserName(string userName) => (userName ?? string.Empty).Trim().ToLowerInvariant();

    public void UpdateProfile(
        string fullName, string? email, string? phoneNumber, string? title, Guid? departmentId, string? smartCaUserId)
    {
        FullName = RequireFullName(fullName);
        Email = Clean(email);
        PhoneNumber = Clean(phoneNumber);
        Title = Clean(title);
        DepartmentId = departmentId;
        SmartCaUserId = Clean(smartCaUserId);
    }

    /// <summary>Thay toàn bộ danh sách vai trò.</summary>
    public void SetRoles(IEnumerable<Guid> roleIds)
    {
        ArgumentNullException.ThrowIfNull(roleIds);
        var desired = roleIds.ToHashSet();
        if (desired.Count == 0)
        {
            throw new DomainException("Người dùng phải có ít nhất một vai trò.");
        }

        _roles.RemoveAll(r => !desired.Contains(r.RoleId));
        foreach (var roleId in desired.Where(id => _roles.TrueForAll(r => r.RoleId != id)))
        {
            _roles.Add(new UserRole(Id, roleId));
        }
    }

    /// <summary>
    /// Chữ ký mẫu của nhân viên (ảnh PNG đã chuẩn hóa), tự ký ở trang Tài khoản. Hiện cạnh khung ký khi xác nhận phiếu để
    /// đối chiếu chữ ký vừa ký với chữ ký đã đăng ký. Dữ liệu cá nhân: lưu dạng bản mã.
    /// </summary>
    [SensitiveData]
    public byte[]? SignatureSpecimenPng { get; private set; }

    public DateTimeOffset? SignatureSpecimenUpdatedAtUtc { get; private set; }

    public void SetSignatureSpecimen(byte[] png, DateTimeOffset nowUtc)
    {
        if (png is not { Length: > 0 })
        {
            throw new DomainException("Thiếu ảnh chữ ký mẫu.");
        }

        SignatureSpecimenPng = png;
        SignatureSpecimenUpdatedAtUtc = nowUtc;
    }

    public void ClearSignatureSpecimen()
    {
        SignatureSpecimenPng = null;
        SignatureSpecimenUpdatedAtUtc = null;
    }

    public void SetPassword(string passwordHash, bool mustChangePassword)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("Mật khẩu đã băm không được rỗng.");
        }

        PasswordHash = passwordHash;
        MustChangePassword = mustChangePassword;
    }

    public bool IsLockedOut(DateTimeOffset nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    /// <summary>Ghi nhận một lần đăng nhập sai. Trả về <c>true</c> nếu tài khoản vừa bị khóa tạm thời.</summary>
    public bool RegisterFailedLogin(DateTimeOffset nowUtc, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        FailedLoginCount++;
        if (FailedLoginCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        LockoutEndUtc = nowUtc + policy.LockoutDuration;
        FailedLoginCount = 0;
        return true;
    }

    public void RegisterSuccessfulLogin(DateTimeOffset nowUtc)
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        LastLoginAtUtc = nowUtc;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Mở khóa: cho phép đăng nhập lại và xóa trạng thái khóa tạm thời.</summary>
    public void Activate()
    {
        IsActive = true;
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }

    private static string RequireFullName(string fullName)
    {
        var value = (fullName ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > FullNameMaxLength)
        {
            throw new DomainException($"Họ tên phải có từ 1 đến {FullNameMaxLength} ký tự.");
        }

        return value;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Gán vai trò cho người dùng.</summary>
public sealed class UserRole : IAuditableEntity
{
    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }
}
