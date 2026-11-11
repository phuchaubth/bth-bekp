using Consent.Domain.Common;

namespace Consent.Domain.Auditing;

/// <summary>Bản ghi nhật ký audit. Chỉ được ghi thêm, không sửa, không xóa.</summary>
public sealed class AuditLog : Entity
{
    public const int ActionMaxLength = 100;
    public const int EntityTypeMaxLength = 100;
    public const int EntityIdMaxLength = 200;
    public const int UserNameMaxLength = 100;
    public const int IpAddressMaxLength = 64;
    public const int UserAgentMaxLength = 512;
    public const int CorrelationIdMaxLength = 100;

    private AuditLog(Guid id, DateTimeOffset occurredAtUtc, string action)
        : base(id)
    {
        OccurredAtUtc = occurredAtUtc;
        Action = action;
    }

    public DateTimeOffset OccurredAtUtc { get; private set; }

    /// <summary>Hành động, ví dụ <c>Create</c>, <c>Update</c>, <c>Auth.LoginSucceeded</c>.</summary>
    public string Action { get; private set; }

    public string? EntityType { get; private set; }

    public string? EntityId { get; private set; }

    public Guid? UserId { get; private set; }

    public string? UserName { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public string? CorrelationId { get; private set; }

    /// <summary>Chi tiết dạng JSON (giá trị trước/sau khi thay đổi, đã che dữ liệu nhạy cảm).</summary>
    public string? Details { get; private set; }

    public static AuditLog Create(
        DateTimeOffset occurredAtUtc,
        string action,
        AuditActor actor,
        string? entityType = null,
        string? entityId = null,
        string? details = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (string.IsNullOrWhiteSpace(action))
        {
            throw new DomainException("Hành động audit không được rỗng.");
        }

        return new AuditLog(Guid.CreateVersion7(), occurredAtUtc, Truncate(action, ActionMaxLength)!)
        {
            EntityType = Truncate(entityType, EntityTypeMaxLength),
            EntityId = Truncate(entityId, EntityIdMaxLength),
            UserId = actor.UserId,
            UserName = Truncate(actor.UserName, UserNameMaxLength),
            IpAddress = Truncate(actor.IpAddress, IpAddressMaxLength),
            UserAgent = Truncate(actor.UserAgent, UserAgentMaxLength),
            CorrelationId = Truncate(actor.CorrelationId, CorrelationIdMaxLength),
            Details = details,
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is { Length: > 0 } && value.Length > maxLength ? value[..maxLength] : value;
}

/// <summary>Ai thực hiện hành động và từ đâu.</summary>
public sealed record AuditActor(Guid? UserId, string? UserName, string? IpAddress, string? UserAgent, string? CorrelationId)
{
    public static AuditActor System { get; } = new(null, "system", null, null, null);
}

/// <summary>Tên hành động audit chuẩn.</summary>
public static class AuditActions
{
    public const string Create = "Create";
    public const string Update = "Update";
    public const string Delete = "Delete";
    public const string LoginSucceeded = "Auth.LoginSucceeded";
    public const string LoginFailed = "Auth.LoginFailed";
    public const string LockedOut = "Auth.LockedOut";
    public const string Logout = "Auth.Logout";
    public const string RefreshTokenReuseDetected = "Auth.RefreshTokenReuseDetected";
    public const string PasswordChanged = "Auth.PasswordChanged";
    public const string PatientViewed = "Patients.Viewed";
    public const string PatientLookedUp = "Patients.LookedUp";
    public const string ConsentFormViewed = "ConsentForms.Viewed";
    public const string ConsentFormPdfViewed = "ConsentForms.PdfViewed";
}
