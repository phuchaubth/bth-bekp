namespace Consent.Shared;

/// <summary>Loại lỗi nghiệp vụ, dùng để ánh xạ sang mã HTTP ở tầng API.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unexpected,
}

/// <summary>Lỗi nghiệp vụ có mã ổn định (để client xử lý) và thông điệp tiếng Việt (để hiển thị).</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}
