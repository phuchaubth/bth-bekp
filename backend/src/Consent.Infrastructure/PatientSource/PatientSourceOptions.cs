namespace Consent.Infrastructure.PatientSource;

/// <summary>
/// Nguồn thông tin hành chính người bệnh (mục <c>PatientSource</c>): gọi dịch vụ riêng "API thông tin người bệnh"
/// (PatientInfo.Api). Phần mềm phiếu đồng thuận không kết nối trực tiếp CSDL bệnh viện.
/// </summary>
public sealed class PatientSourceOptions
{
    public const string SectionName = "PatientSource";

    public const string NoneProvider = "None";

    public const string ApiProvider = "Api";

    public const int MinimumApiKeyLength = 32;

    /// <summary><c>None</c> (mặc định, tắt) hoặc <c>Api</c>.</summary>
    public string Provider { get; set; } = NoneProvider;

    /// <summary>Địa chỉ gốc của API, ví dụ <c>https://thongtinbn.benhvien.local/</c>. Bắt buộc HTTPS, trừ khi cùng máy (localhost).</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Khóa API gửi trong header <c>X-Api-Key</c>: chỉ đặt bằng biến môi trường <c>PatientSource__ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    public bool IsApi => string.Equals(Provider, ApiProvider, StringComparison.OrdinalIgnoreCase);

    public bool IsKnownProvider => IsApi || string.Equals(Provider, NoneProvider, StringComparison.OrdinalIgnoreCase);

    public bool HasValidBaseUrl =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        && string.IsNullOrEmpty(uri.UserInfo);

    public bool HasValidApiKey => ApiKey is { Length: >= MinimumApiKeyLength };
}
