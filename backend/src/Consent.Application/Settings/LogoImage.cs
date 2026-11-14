using System.Buffers.Binary;
using Consent.Shared;

namespace Consent.Application.Settings;

/// <summary>Logo bệnh viện đã kiểm tra: PNG hoặc JPEG, kèm kích thước điểm ảnh đọc từ phần đầu file.</summary>
public sealed record LogoImage(byte[] Content, string ContentType, int Width, int Height)
{
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    /// <summary>Tối đa 1 MB (đủ cho logo in trên giấy A4; yêu cầu tải lên cũng bị giới hạn 2 MB).</summary>
    public const int MaxBytes = 1024 * 1024;

    public const int MinSide = 32;
    public const int MaxSide = 4000;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Kiểm tra ảnh tải lên (Base64, có thể kèm tiền tố <c>data:image/...;base64,</c>). Chỉ nhận PNG/JPEG nhận ra được từ
    /// nội dung file (không tin phần mở rộng hay kiểu do trình duyệt gửi); không nhận SVG hay định dạng khác.
    /// </summary>
    public static Result<LogoImage> Parse(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return Invalid("Vui lòng chọn file logo.");
        }

        var data = base64.Trim();
        var comma = data.IndexOf(',', StringComparison.Ordinal);
        if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
        {
            data = data[(comma + 1)..];
        }

        if (data.Length > (MaxBytes + 2) / 3 * 4 + 4)
        {
            return Invalid("Logo tối đa 1 MB.");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(data);
        }
        catch (FormatException)
        {
            return Invalid("File logo không hợp lệ.");
        }

        if (content.Length > MaxBytes)
        {
            return Invalid("Logo tối đa 1 MB.");
        }

        if (Read(content) is not { } image)
        {
            return Invalid("Logo phải là ảnh PNG hoặc JPG.");
        }

        if (image.Width < MinSide || image.Height < MinSide || image.Width > MaxSide || image.Height > MaxSide)
        {
            return Invalid($"Kích thước logo phải từ {MinSide} đến {MaxSide} điểm ảnh mỗi chiều.");
        }

        return image;
    }

    /// <summary>Nhận dạng PNG/JPEG và đọc kích thước; không phải hai định dạng này hoặc file hỏng thì trả về null.</summary>
    public static LogoImage? Read(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length >= 24 && content.AsSpan(0, 8).SequenceEqual(PngSignature)
            && content.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            var width = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(content.AsSpan(20, 4));
            return width > 0 && height > 0 ? new LogoImage(content, Png, width, height) : null;
        }

        return content.Length >= 4 && content[0] == 0xFF && content[1] == 0xD8 ? ReadJpeg(content) : null;
    }

    /// <summary>Duyệt các đoạn (marker) của JPEG tới đoạn SOF để lấy kích thước.</summary>
    private static LogoImage? ReadJpeg(byte[] content)
    {
        var i = 2;
        while (i + 4 <= content.Length)
        {
            if (content[i] != 0xFF)
            {
                return null;
            }

            var marker = content[i + 1];
            if (marker == 0xFF)
            {
                i++;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(i + 2, 2));
            if (length < 2 || i + 2 + length > content.Length)
            {
                return null;
            }

            // SOF0–SOF15, trừ DHT (C4), JPG (C8), DAC (CC).
            if (marker is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (length < 7)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(i + 5, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(content.AsSpan(i + 7, 2));
                return width > 0 && height > 0 ? new LogoImage(content, Jpeg, width, height) : null;
            }

            i += 2 + length;
        }

        return null;
    }

    private static Error Invalid(string message) => Error.ValidationField("image", message);
}
