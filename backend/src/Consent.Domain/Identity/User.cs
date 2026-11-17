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

    /// <summary>
    /// Số khoa/phòng khác tối đa (quyết định 41). Người cần xem dữ liệu nhiều khoa hơn thì gán vai trò có quyền xem dữ liệu
    /// toàn viện; giới hạn giữ access token gọn (mỗi khoa một claim).
    /// </summary>
    public const int MaxAdditionalDepartments = 20;

    private readonly List<UserRole> _roles = [];

    private readonly List<UserDepartment> _additionalDepartments = [];

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

    /// <summary>
    /// Khoa/phòng chính: khoa mặc định khi lập phiếu, lập báo cáo điều dưỡng, và khoa của phiếu làm trên kiosk.
    /// </summary>
    public Guid? DepartmentId { get; private set; }

    /// <summary>Khoa/phòng khác của tài khoản (quyết định 41), không gồm khoa chính.</summary>
    public IReadOnlyCollection<UserDepartment> AdditionalDepartments => _additionalDepartments;

    public IReadOnlyList<Guid> AdditionalDepartmentIds => _additionalDepartments.ConvertAll(d => d.DepartmentId);

    /// <summary>Mọi khoa/phòng của tài khoản, khoa chính đứng đầu: tài khoản thấy dữ liệu của các khoa này.</summary>
    public IReadOnlyList<Guid> DepartmentIds =>
        DepartmentId is { } primary ? [primary, .. AdditionalDepartmentIds] : [];

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

    /// <summary>
    /// Số lần tra cứu người bệnh sai liên tiếp trên kiosk (mã y tế/số CCCD không khớp ngày sinh, hoặc không có hồ sơ).
    /// Tách riêng với đăng nhập sai: khóa tra cứu không làm máy kiosk bị đăng xuất.
    /// </summary>
    public int KioskLookupFailures { get; private set; }

    /// <summary>Thời điểm hết khóa tra cứu người bệnh trên kiosk (quyết định 24: sai 5 lần thì khóa 5 phút).</summary>
    public DateTimeOffset? KioskLookupLockedUntilUtc { get; private set; }

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

    public void UpdateProfile(string fullName, string? email, string? phoneNumber, string? title, string? smartCaUserId)
    {
        FullName = RequireFullName(fullName);
        Email = Clean(email);
        PhoneNumber = Clean(phoneNumber);
        Title = Clean(title);
        SmartCaUserId = Clean(smartCaUserId);
    }

    /// <summary>
    /// Đặt khoa chính và các khoa/phòng khác (quyết định 41). Khoa trùng khoa chính hay lặp lại thì bỏ qua; có khoa khác thì
    /// phải có khoa chính (khoa mặc định khi lập phiếu, báo cáo).
    /// </summary>
    public void SetDepartments(Guid? primaryDepartmentId, IEnumerable<Guid> additionalDepartmentIds)
    {
        ArgumentNullException.ThrowIfNull(additionalDepartmentIds);
        var additional = additionalDepartmentIds.Where(id => id != primaryDepartmentId).Distinct().ToList();
        if (additional.Count > 0 && primaryDepartmentId is null)
        {
            throw new DomainException("Chọn khoa/phòng chính trước khi thêm khoa/phòng khác.");
        }

        if (additional.Contains(Guid.Empty) || primaryDepartmentId == Guid.Empty)
        {
            throw new DomainException("Khoa/phòng không hợp lệ.");
        }

        if (additional.Count > MaxAdditionalDepartments)
        {
            throw new DomainException(
                $"Một tài khoản có tối đa {MaxAdditionalDepartments} khoa/phòng khác. Người cần xem nhiều khoa hơn thì gán vai trò có quyền xem dữ liệu toàn viện.");
        }

        DepartmentId = primaryDepartmentId;
        _additionalDepartments.RemoveAll(d => !additional.Contains(d.DepartmentId));
        foreach (var departmentId in additional.Where(id => _additionalDepartments.TrueForAll(d => d.DepartmentId != id)))
        {
            _additionalDepartments.Add(new UserDepartment(Id, departmentId));
        }
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

    public bool IsKioskLookupLocked(DateTimeOffset nowUtc) => KioskLookupLockedUntilUtc is { } until && until > nowUtc;

    /// <summary>Ghi nhận một lần tra cứu người bệnh sai trên kiosk. Trả về <c>true</c> nếu tra cứu vừa bị khóa tạm thời.</summary>
    public bool RegisterKioskLookupFailure(DateTimeOffset nowUtc, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        KioskLookupFailures++;
        if (KioskLookupFailures < policy.MaxFailedAttempts)
        {
            return false;
        }

        KioskLookupLockedUntilUtc = nowUtc + policy.LockoutDuration;
        KioskLookupFailures = 0;
        return true;
    }

    /// <summary>Tra cứu đúng: xóa đếm lần sai và trạng thái khóa tra cứu.</summary>
    public void ResetKioskLookupFailures()
    {
        KioskLookupFailures = 0;
        KioskLookupLockedUntilUtc = null;
    }

    public void Deactivate() => IsActive = false;

    /// <summary>Mở khóa: cho phép đăng nhập lại và xóa trạng thái khóa tạm thời (cả khóa tra cứu trên kiosk).</summary>
    public void Activate()
    {
        IsActive = true;
        FailedLoginCount = 0;
        LockoutEndUtc = null;
        ResetKioskLookupFailures();
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

/// <summary>Khoa/phòng khác của người dùng (ngoài khoa chính), quyết định 41.</summary>
public sealed class UserDepartment : IAuditableEntity
{
    internal UserDepartment(Guid userId, Guid departmentId)
    {
        UserId = userId;
        DepartmentId = departmentId;
    }

    public Guid UserId { get; private set; }

    public Guid DepartmentId { get; private set; }
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
