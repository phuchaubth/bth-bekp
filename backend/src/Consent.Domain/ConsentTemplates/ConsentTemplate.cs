using Consent.Domain.Common;

namespace Consent.Domain.ConsentTemplates;

public enum TemplateVersionStatus
{
    /// <summary>Đang soạn, sửa được, chưa dùng để lập phiếu.</summary>
    Draft,

    /// <summary>Đang dùng để lập phiếu. Mỗi mẫu có tối đa một phiên bản phát hành.</summary>
    Published,

    /// <summary>Ngừng dùng cho phiếu mới; phiếu đã lập vẫn giữ phiên bản này.</summary>
    Retired,
}

/// <summary>
/// Loại mẫu phiếu (ví dụ CK-01). Nội dung (định nghĩa trường, câu chữ) nằm ở các phiên bản. Phiếu đã lập luôn
/// tham chiếu đúng phiên bản tại thời điểm lập, nên sửa mẫu không làm thay đổi phiếu cũ.
/// </summary>
public sealed class ConsentTemplate : Entity, IAuditableEntity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int RoleCodeMaxLength = 50;

    private readonly List<ConsentTemplateVersion> _versions = [];

    private ConsentTemplate(
        Guid id, string code, string name, string hospitalSignerRoleCode, Guid? departmentId, DateTimeOffset createdAtUtc)
        : base(id)
    {
        Code = code;
        Name = name;
        HospitalSignerRoleCode = hospitalSignerRoleCode;
        DepartmentId = departmentId;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    /// <summary>Mã mẫu, in trên phiếu (ví dụ "CK-01"). Viết hoa, không trùng.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    /// <summary>Mã vai trò của người xác nhận phía bệnh viện (ví dụ "NURSE"), cấu hình theo từng mẫu.</summary>
    public string HospitalSignerRoleCode { get; private set; }

    /// <summary>Khoa được dùng mẫu này; <c>null</c> = toàn viện.</summary>
    public Guid? DepartmentId { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<ConsentTemplateVersion> Versions => _versions;

    public ConsentTemplateVersion? PublishedVersion => _versions.FirstOrDefault(v => v.Status == TemplateVersionStatus.Published);

    public ConsentTemplateVersion? DraftVersion => _versions.FirstOrDefault(v => v.Status == TemplateVersionStatus.Draft);

    /// <summary>Tạo mẫu mới kèm phiên bản nháp số 1.</summary>
    public static ConsentTemplate Create(
        string code, string name, string hospitalSignerRoleCode, Guid? departmentId, string initialContentJson, DateTimeOffset nowUtc)
    {
        var template = new ConsentTemplate(
            Guid.CreateVersion7(), NormalizeCode(code), Required(name, nameof(name)), NormalizeRole(hospitalSignerRoleCode), departmentId, nowUtc);
        template._versions.Add(new ConsentTemplateVersion(Guid.CreateVersion7(), template.Id, 1, initialContentJson, nowUtc));
        return template;
    }

    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var normalized = code.Trim().ToUpperInvariant();
        return normalized.Length == 0 ? throw new DomainException("Mã mẫu phiếu không được trống.") : normalized;
    }

    public void Update(string name, string hospitalSignerRoleCode, Guid? departmentId, bool isActive, DateTimeOffset nowUtc)
    {
        Name = Required(name, nameof(name));
        HospitalSignerRoleCode = NormalizeRole(hospitalSignerRoleCode);
        DepartmentId = departmentId;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }

    public ConsentTemplateVersion? FindVersion(int versionNo) => _versions.FirstOrDefault(v => v.VersionNo == versionNo);

    /// <summary>Có thể tạo nháp mới khi chưa có nháp nào (mỗi lúc chỉ soạn một phiên bản).</summary>
    public bool CanCreateDraft => DraftVersion is null;

    /// <summary>Tạo phiên bản nháp mới, sao chép nội dung của phiên bản mới nhất.</summary>
    public ConsentTemplateVersion CreateDraftFromLatest(DateTimeOffset nowUtc)
    {
        if (!CanCreateDraft)
        {
            throw new DomainException("Mẫu phiếu đang có một phiên bản nháp.");
        }

        var latest = _versions.MaxBy(v => v.VersionNo) ?? throw new DomainException("Mẫu phiếu chưa có phiên bản nào.");
        var draft = new ConsentTemplateVersion(Guid.CreateVersion7(), Id, latest.VersionNo + 1, latest.ContentJson, nowUtc);
        _versions.Add(draft);
        UpdatedAtUtc = nowUtc;
        return draft;
    }

    public void UpdateDraft(int versionNo, string contentJson, DateTimeOffset nowUtc)
    {
        var version = RequireVersion(versionNo);
        version.UpdateContent(contentJson, nowUtc);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Phát hành phiên bản nháp; phiên bản đang phát hành (nếu có) chuyển sang ngừng dùng.</summary>
    public void Publish(int versionNo, DateTimeOffset nowUtc)
    {
        var version = RequireVersion(versionNo);
        if (version.Status != TemplateVersionStatus.Draft)
        {
            throw new DomainException("Chỉ phát hành được phiên bản nháp.");
        }

        PublishedVersion?.Retire(nowUtc);
        version.Publish(nowUtc);
        UpdatedAtUtc = nowUtc;
    }

    public void Retire(int versionNo, DateTimeOffset nowUtc)
    {
        var version = RequireVersion(versionNo);
        if (version.Status != TemplateVersionStatus.Published)
        {
            throw new DomainException("Chỉ ngừng dùng được phiên bản đang phát hành.");
        }

        version.Retire(nowUtc);
        UpdatedAtUtc = nowUtc;
    }

    private ConsentTemplateVersion RequireVersion(int versionNo) =>
        FindVersion(versionNo) ?? throw new DomainException($"Không có phiên bản {versionNo}.");

    private static string NormalizeRole(string roleCode) => Required(roleCode, nameof(roleCode)).ToUpperInvariant();

    private static string Required(string value, string name)
    {
        ArgumentNullException.ThrowIfNull(value, name);
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? throw new DomainException($"{name} không được trống.") : trimmed;
    }
}

/// <summary>Một phiên bản nội dung của mẫu phiếu. Chỉ phiên bản nháp sửa được.</summary>
public sealed class ConsentTemplateVersion : Entity, IAuditableEntity
{
    internal ConsentTemplateVersion(Guid id, Guid templateId, int versionNo, string contentJson, DateTimeOffset createdAtUtc)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(contentJson);
        TemplateId = templateId;
        VersionNo = versionNo;
        ContentJson = contentJson;
        Status = TemplateVersionStatus.Draft;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid TemplateId { get; private set; }

    public int VersionNo { get; private set; }

    public TemplateVersionStatus Status { get; private set; }

    /// <summary>Định nghĩa mẫu (JSON): tiêu đề, các phần, trường dữ liệu, câu chữ cố định, khối chữ ký.</summary>
    public string ContentJson { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public DateTimeOffset? RetiredAtUtc { get; private set; }

    internal void UpdateContent(string contentJson, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(contentJson);
        if (Status != TemplateVersionStatus.Draft)
        {
            throw new DomainException("Chỉ sửa được phiên bản nháp. Hãy tạo phiên bản mới.");
        }

        ContentJson = contentJson;
        UpdatedAtUtc = nowUtc;
    }

    internal void Publish(DateTimeOffset nowUtc)
    {
        Status = TemplateVersionStatus.Published;
        PublishedAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }

    internal void Retire(DateTimeOffset nowUtc)
    {
        Status = TemplateVersionStatus.Retired;
        RetiredAtUtc = nowUtc;
        UpdatedAtUtc = nowUtc;
    }
}
