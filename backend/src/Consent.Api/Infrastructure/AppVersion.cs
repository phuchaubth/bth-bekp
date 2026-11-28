using System.Globalization;
using System.Text.Json;

namespace Consent.Api.Infrastructure;

/// <summary>
/// Phiên bản của bản API đang chạy (quyết định 67). Quy trình đóng gói (<c>.github/workflows/package-iis.yml</c>) ghi file
/// <see cref="FileName"/> cạnh Consent.Api.dll gồm số gói (số lần chạy quy trình "Gói triển khai IIS"), mã commit và thời điểm
/// đóng gói. API đọc file một lần lúc khởi động nên giá trị luôn là của mã đang chạy: file mới chép vào chỉ có hiệu lực khi API
/// khởi động lại. Bản phát triển, bản chạy bằng Docker, bản cài từ gói chưa ghi phiên bản không có file: các giá trị là <c>null</c>.
/// </summary>
/// <param name="Package">Số gói, ví dụ 97; <c>null</c> khi không rõ.</param>
/// <param name="Commit">Mã commit đầy đủ (40 ký tự 0-9, a-f); <c>null</c> khi không rõ.</param>
/// <param name="BuiltAtUtc">Thời điểm đóng gói (UTC); <c>null</c> khi không rõ.</param>
public sealed record AppVersion(int? Package, string? Commit, DateTimeOffset? BuiltAtUtc)
{
    /// <summary>Tên file phiên bản (cùng tên trong các thư mục ConsentApi, ConsentWeb, PatientInfoApi của gói).</summary>
    public const string FileName = "version.json";

    /// <summary>File do quy trình đóng gói ghi chỉ khoảng 100 byte: file lớn hơn giới hạn này không phải file đó, không đọc.</summary>
    internal const int MaxFileBytes = 4096;

    /// <summary>Định dạng thời điểm đóng gói quy trình đóng gói ghi (<c>date -u +%Y-%m-%dT%H:%M:%SZ</c>).</summary>
    private const string BuiltAtFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static AppVersion Unknown { get; } = new(null, null, null);

    /// <summary>
    /// Đọc <see cref="FileName"/> trong <paramref name="directory"/>. Không có file, file quá lớn, không đọc được hoặc không phải
    /// JSON thì trả <see cref="Unknown"/>: thiếu thông tin phiên bản không được làm API không khởi động được.
    /// </summary>
    public static AppVersion Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxFileBytes ? Parse(File.ReadAllText(path)) : Unknown;
        }
        catch (IOException)
        {
            return Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return Unknown;
        }
    }

    /// <summary>Nội dung file phiên bản. Giá trị nào sai định dạng thì giá trị đó là <c>null</c>; không phải JSON object thì <see cref="Unknown"/>.</summary>
    internal static AppVersion Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                ? new AppVersion(ReadPackage(root), ReadCommit(root), ReadBuiltAt(root))
                : Unknown;
        }
        catch (JsonException)
        {
            return Unknown;
        }
    }

    private static int? ReadPackage(JsonElement root) =>
        root.TryGetProperty("package", out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number) && number > 0
            ? number
            : null;

    private static string? ReadCommit(JsonElement root) =>
        root.TryGetProperty("commit", out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: 40 } commit && commit.All(char.IsAsciiHexDigitLower)
            ? commit
            : null;

    private static DateTimeOffset? ReadBuiltAt(JsonElement root) =>
        root.TryGetProperty("builtAtUtc", out var value) && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParseExact(
            value.GetString(), BuiltAtFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var builtAt)
            ? builtAt
            : null;
}
