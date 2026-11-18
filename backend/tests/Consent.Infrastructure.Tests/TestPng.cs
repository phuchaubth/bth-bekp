using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Consent.Infrastructure.Tests;

/// <summary>Tạo ảnh PNG nhỏ cho test (giống ảnh khung ký trình duyệt xuất ra), không cần thư viện ảnh.</summary>
internal static class TestPng
{
    /// <summary>Ảnh RGBA: nền trong suốt (hoặc trắng), điểm có <paramref name="ink"/> = true là nét đen.</summary>
    public static byte[] Rgba(int width, int height, Func<int, int, bool> ink, bool whiteBackground = false)
    {
        var raw = new byte[height * ((width * 4) + 1)];
        var i = 0;
        for (var y = 0; y < height; y++)
        {
            raw[i++] = 0;
            for (var x = 0; x < width; x++)
            {
                var (r, g, b, a) = ink(x, y) ? ((byte)20, (byte)20, (byte)40, (byte)255)
                    : whiteBackground ? ((byte)255, (byte)255, (byte)255, (byte)255) : ((byte)0, (byte)0, (byte)0, (byte)0);
                raw[i++] = r;
                raw[i++] = g;
                raw[i++] = b;
                raw[i++] = a;
            }
        }

        return Build(width, height, bitDepth: 8, colorType: 6, raw);
    }

    /// <summary>Một nét ký hình sóng ở giữa khung.</summary>
    public static byte[] Signature(int width = 600, int height = 200, bool whiteBackground = false) =>
        Rgba(width, height, (x, y) => x is > 60 and < 540 && Math.Abs(y - (100 + (int)(40 * Math.Sin(x / 30.0)))) < 3, whiteBackground);

    public static byte[] Blank(int width = 600, int height = 200) => Rgba(width, height, (_, _) => false);

    /// <summary>Ghép file PNG từ dữ liệu thô (đã thêm byte bộ lọc mỗi dòng).</summary>
    public static byte[] Build(int width, int height, byte bitDepth, byte colorType, byte[] raw, bool corruptCrc = false)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = bitDepth;
        header[9] = colorType;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(png, "IHDR", header, corruptCrc);
        WriteChunk(png, "tEXt", Encoding.ASCII.GetBytes("Comment\0du lieu kem theo"), false);
        WriteChunk(png, "IDAT", compressed.ToArray(), false);
        WriteChunk(png, "IEND", [], false);
        return png.ToArray();
    }

    public static (int Width, int Height, byte ColorType) ReadHeader(byte[] png) =>
        (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)), png[25]);

    private static void WriteChunk(Stream output, string type, byte[] data, bool corruptCrc)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeAndData = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typeAndData);
        data.CopyTo(typeAndData, 4);
        output.Write(typeAndData);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeAndData) ^ (corruptCrc ? 1u : 0u));
        output.Write(crc);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
