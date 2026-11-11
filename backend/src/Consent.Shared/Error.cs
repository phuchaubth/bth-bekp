namespace Consent.Shared;

/// <summary>Loại lỗi nghiệp vụ, dùng để ánh xạ sang mã HTTP ở tầng API.</summary>
public enum ErrorType
{
    /// <summary>Dữ liệu đầu vào không hợp lệ (HTTP 400).</summary>
    Validation,

    /// <summary>Chưa xác thực hoặc xác thực thất bại (HTTP 401).</summary>
    Unauthorized,

    /// <summary>Không có quyền (HTTP 403).</summary>
    Forbidden,

    /// <summary>Không tìm thấy (HTTP 404).</summary>
    NotFound,

    /// <summary>Xung đột dữ liệu, ví dụ trùng mã (HTTP 409).</summary>
    Conflict,

    /// <summary>Vi phạm quy tắc nghiệp vụ (HTTP 422).</summary>
    BusinessRule,

    /// <summary>Lỗi không lường trước (HTTP 500).</summary>
    Unexpected,
}

/// <summary>
/// Lỗi nghiệp vụ có mã ổn định (để client xử lý) và thông điệp tiếng Việt (để hiển thị).
/// <see cref="FieldErrors"/> chứa lỗi theo từng trường (tên trường dạng camelCase) khi kiểm tra dữ liệu.
/// </summary>
public sealed record Error(
    string Code,
    string Message,
    ErrorType Type,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static Error Validation(string code, string message, IReadOnlyDictionary<string, string[]>? fieldErrors = null) =>
        new(code, message, ErrorType.Validation, fieldErrors);

    /// <summary>Lỗi kiểm tra cho một trường cụ thể.</summary>
    public static Error ValidationField(string field, string message) =>
        new("validation", "Dữ liệu không hợp lệ.", ErrorType.Validation, new Dictionary<string, string[]> { [field] = [message] });

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error BusinessRule(string code, string message) => new(code, message, ErrorType.BusinessRule);
}
