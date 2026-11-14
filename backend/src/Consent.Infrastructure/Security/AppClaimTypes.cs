namespace Consent.Infrastructure.Security;

/// <summary>Tên claim trong access token (ngắn gọn, không dùng URI dài của .NET).</summary>
public static class AppClaimTypes
{
    public const string Subject = "sub";
    public const string UserName = "name";
    public const string FullName = "full_name";
    public const string Department = "dept";
    public const string Role = "role";
    public const string Permission = "perm";
    public const string MustChangePassword = "pwd_change";

    /// <summary>Phiếu được ký (chỉ có trong token phiên ký).</summary>
    public const string SigningForm = "sign_form";

    /// <summary>Mã phiên ký (chỉ có trong token phiên ký).</summary>
    public const string SigningSession = "sign_session";
}
