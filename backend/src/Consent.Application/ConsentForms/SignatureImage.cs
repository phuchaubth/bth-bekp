using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Consent.Application.ConsentForms;

/// <summary>
/// Kiểm tra và chuẩn hóa ảnh chữ ký tay gửi từ trình duyệt (PNG của khung ký).
/// Máy chủ tự giải mã ảnh (chỉ hỗ trợ PNG 8 bit không xen kẽ, đúng loại trình duyệt xuất ra), từ chối ảnh sai định dạng,
/// quá lớn hoặc khung ký trống, rồi mã hóa lại thành PNG xám đã cắt sát nét ký. Ảnh lưu lại chỉ còn điểm ảnh:
/// mọi dữ liệu kèm theo trong file gốc (metadata, chunk lạ) bị loại bỏ.
/// </summary>
public static class SignatureImage
{
    /// <summary>Dung lượng tối đa của file PNG gửi lên.</summary>
    public const int MaxBytes = 512 * 1024;

    public const int MaxWidth = 3000;

    public const int MaxHeight = 1500;

    /// <summary>Số điểm mực tối thiểu: ít hơn coi như khung ký trống (chấm hoặc vạch rất ngắn).</summary>
    public const int MinInkPixels = 150;

    /// <summary>Chiều dài/rộng tối thiểu (điểm ảnh) của vùng có nét ký.</summary>
    public const int MinInkExtent = 30;

    /// <summary>Điểm ảnh (đã phủ lên nền trắng) tối hơn ngưỡng này được coi là nét ký.</summary>
    private const int InkThreshold = 160;

    private const int Padding = 8;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>Kết quả: ảnh PNG đã chuẩn hóa, hoặc lỗi. <see cref="IsBlank"/> = ảnh hợp lệ nhưng không có nét ký.</summary>
    public sealed record Outcome(byte[]? Png, string? Error, bool IsBlank)
    {
        public bool IsValid => Png is not null;
    }

    public static Outcome Normalize(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length > MaxBytes)
        {
            return Invalid($"Ảnh chữ ký quá lớn (tối đa {MaxBytes / 1024} KB).");
        }

        Gray? image;
        try
        {
            image = Decode(png, out var error);
            if (image is null)
            {
                return Invalid(error!);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return Invalid("Ảnh chữ ký bị hỏng, không đọc được.");
        }

        var (count, left, top, right, bottom) = FindInk(image);
        if (count < MinInkPixels || Math.Max(right - left + 1, bottom - top + 1) < MinInkExtent)
        {
            return new Outcome(null, "Chưa có chữ ký: khung ký đang trống hoặc nét ký quá ngắn.", IsBlank: true);
        }

        var x = Math.Max(0, left - Padding);
        var y = Math.Max(0, top - Padding);
        var width = Math.Min(image.Width, right + Padding + 1) - x;
        var height = Math.Min(image.Height, bottom + Padding + 1) - y;
        return new Outcome(EncodeGray(image, x, y, width, height), null, IsBlank: false);
    }

    private static Outcome Invalid(string error) => new(null, error, IsBlank: false);

    /// <summary>Ảnh xám (0 = đen, 255 = trắng) đã phủ lên nền trắng.</summary>
    private sealed record Gray(int Width, int Height, byte[] Pixels);

    private static Gray? Decode(byte[] png, out string? error)
    {
        error = "Ảnh chữ ký không đúng định dạng PNG.";
        if (png.Length < PngSignature.Length + 25 || !png.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
        {
            return null;
        }

        int width = 0, height = 0, channels = 0;
        var seenHeader = false;
        using var idat = new MemoryStream();
        var offset = PngSignature.Length;
        while (true)
        {
            if (offset + 12 > png.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (length < 0 || offset + 12L + length > png.Length)
            {
                return null;
            }

            var type = Encoding.ASCII.GetString(png, offset + 4, 4);
            var data = png.AsSpan(offset + 8, length);
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset + 8 + length));
            if (crc != Crc(png.AsSpan(offset + 4, length + 4)))
            {
                error = "Ảnh chữ ký bị hỏng (sai mã kiểm tra).";
                return null;
            }

            offset += 12 + length;
            if (!seenHeader)
            {
                if (type != "IHDR" || length != 13)
                {
                    return null;
                }

                width = BinaryPrimitives.ReadInt32BigEndian(data);
                height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                var bitDepth = data[8];
                var colorType = data[9];
                if (width <= 0 || height <= 0 || width > MaxWidth || height > MaxHeight)
                {
                    error = $"Kích thước ảnh chữ ký không hợp lệ (tối đa {MaxWidth}×{MaxHeight} điểm ảnh).";
                    return null;
                }

                channels = colorType switch { 0 => 1, 2 => 3, 4 => 2, 6 => 4, _ => 0 };
                if (bitDepth != 8 || channels == 0 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                {
                    error = "Ảnh chữ ký dùng kiểu PNG không được hỗ trợ (cần ảnh 8 bit, không xen kẽ).";
                    return null;
                }

                seenHeader = true;
                continue;
            }

            if (type == "IDAT")
            {
                idat.Write(data);
            }
            else if (type == "IEND")
            {
                break;
            }
        }

        var stride = width * channels;
        var raw = new byte[(long)height * (stride + 1)];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.ReadExactly(raw);
        }

        error = null;
        return new Gray(width, height, ToGray(Unfilter(raw, height, stride, channels), width, height, channels));
    }

    /// <summary>Bỏ bộ lọc từng dòng của PNG (None, Sub, Up, Average, Paeth).</summary>
    private static byte[] Unfilter(byte[] raw, int height, int stride, int bytesPerPixel)
    {
        var pixels = new byte[height * stride];
        for (var row = 0; row < height; row++)
        {
            var filter = raw[row * (stride + 1)];
            var source = raw.AsSpan(row * (stride + 1) + 1, stride);
            var current = pixels.AsSpan(row * stride, stride);
            var previous = row == 0 ? Span<byte>.Empty : pixels.AsSpan((row - 1) * stride, stride);
            for (var i = 0; i < stride; i++)
            {
                int left = i >= bytesPerPixel ? current[i - bytesPerPixel] : 0;
                int up = row > 0 ? previous[i] : 0;
                int upLeft = row > 0 && i >= bytesPerPixel ? previous[i - bytesPerPixel] : 0;
                var predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException("Bộ lọc PNG không hợp lệ."),
                };
                current[i] = (byte)(source[i] + predictor);
            }
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>Chuyển về ảnh xám phủ lên nền trắng (vùng trong suốt thành trắng).</summary>
    private static byte[] ToGray(byte[] pixels, int width, int height, int channels)
    {
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            var p = i * channels;
            int luminance, alpha;
            switch (channels)
            {
                case 1:
                    (luminance, alpha) = (pixels[p], 255);
                    break;
                case 2:
                    (luminance, alpha) = (pixels[p], pixels[p + 1]);
                    break;
                case 3:
                    (luminance, alpha) = (Luminance(pixels[p], pixels[p + 1], pixels[p + 2]), 255);
                    break;
                default:
                    (luminance, alpha) = (Luminance(pixels[p], pixels[p + 1], pixels[p + 2]), pixels[p + 3]);
                    break;
            }

            gray[i] = (byte)(((luminance * alpha) + (255 * (255 - alpha)) + 127) / 255);
        }

        return gray;
    }

    private static int Luminance(int r, int g, int b) => ((299 * r) + (587 * g) + (114 * b)) / 1000;

    private static (int Count, int Left, int Top, int Right, int Bottom) FindInk(Gray image)
    {
        int count = 0, left = image.Width, top = image.Height, right = -1, bottom = -1;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Pixels[(y * image.Width) + x] < InkThreshold)
                {
                    count++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (count, left, top, right, bottom);
    }

    /// <summary>Mã hóa vùng ảnh thành PNG xám 8 bit (chỉ gồm IHDR, IDAT, IEND).</summary>
    private static byte[] EncodeGray(Gray image, int x, int y, int width, int height)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (var row = 0; row < height; row++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, ((y + row) * image.Width) + x, width);
            }
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;

        using var output = new MemoryStream();
        output.Write(PngSignature);
        WriteChunk(output, "IHDR", header);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        output.Write(buffer);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc(typeAndData));
        output.Write(buffer);
    }

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
