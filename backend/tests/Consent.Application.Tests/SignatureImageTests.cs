using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;

namespace Consent.Application.Tests;

public sealed class SignatureImageTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Signature_IsCroppedToTheInk_AndReencodedAsGrayscale(bool whiteBackground)
    {
        var original = TestPng.Signature(whiteBackground: whiteBackground);

        var result = SignatureImage.Normalize(original);

        Assert.True(result.IsValid, result.Error);
        var (width, height, colorType) = TestPng.ReadHeader(result.Png!);
        Assert.Equal(0, colorType);
        Assert.InRange(width, 470, 500);
        Assert.InRange(height, 80, 110);
        // Chỉ còn điểm ảnh: dữ liệu kèm theo (chunk tEXt) trong file gốc bị loại bỏ.
        Assert.DoesNotContain("tEXt"u8.ToArray(), Chunks(result.Png!));
        // Ảnh đã chuẩn hóa vẫn hợp lệ khi kiểm tra lại.
        Assert.True(SignatureImage.Normalize(result.Png!).IsValid);
    }

    [Fact]
    public void BlankPad_OrASingleDot_IsReportedAsBlank()
    {
        var blank = SignatureImage.Normalize(TestPng.Blank());
        var dot = SignatureImage.Normalize(TestPng.Rgba(600, 200, (x, y) => Math.Abs(x - 300) < 4 && Math.Abs(y - 100) < 4));

        Assert.True(blank.IsBlank);
        Assert.True(dot.IsBlank);
        Assert.False(dot.IsValid);
    }

    [Fact]
    public void NotAPng_IsRejected()
    {
        var result = SignatureImage.Normalize("%PDF-1.7 không phải ảnh"u8.ToArray());

        Assert.False(result.IsValid);
        Assert.False(result.IsBlank);
        Assert.Contains("PNG", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptedChecksum_IsRejected()
    {
        var raw = new byte[10 * ((10 * 4) + 1)];
        var result = SignatureImage.Normalize(TestPng.Build(10, 10, 8, 6, raw, corruptCrc: true));

        Assert.False(result.IsValid);
        Assert.Contains("hỏng", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void UnsupportedFormat_IsRejected()
    {
        var sixteenBit = TestPng.Build(10, 10, bitDepth: 16, colorType: 6, new byte[10 * ((10 * 8) + 1)]);
        var palette = TestPng.Build(10, 10, bitDepth: 8, colorType: 3, new byte[10 * 11]);

        Assert.Contains("không được hỗ trợ", SignatureImage.Normalize(sixteenBit).Error, StringComparison.Ordinal);
        Assert.Contains("không được hỗ trợ", SignatureImage.Normalize(palette).Error, StringComparison.Ordinal);
    }

    [Fact]
    public void HugeDimensions_AreRejected_BeforeDecoding()
    {
        // Khai báo 20.000 × 20.000 điểm ảnh nhưng dữ liệu rất nhỏ (kiểu "bom giải nén"): bị chặn ngay ở phần đầu file.
        var result = SignatureImage.Normalize(TestPng.Build(20_000, 20_000, 8, 6, new byte[10]));

        Assert.False(result.IsValid);
        Assert.Contains("Kích thước", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedImageData_IsRejected()
    {
        // Dữ liệu ảnh ít hơn kích thước khai báo.
        var result = SignatureImage.Normalize(TestPng.Build(100, 100, 8, 6, new byte[100]));

        Assert.False(result.IsValid);
        Assert.Contains("hỏng", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TooLargeFile_IsRejected()
    {
        var result = SignatureImage.Normalize(new byte[SignatureImage.MaxBytes + 1]);

        Assert.Contains("quá lớn", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Colorize_PrintsTheStoredGraySignatureInBlueInk_OnAWhiteBackground()
    {
        // Ảnh đã lưu (xám, 30 × 4): cột 0–9 mực đen (chữ ký cũ), cột 10–19 xám bằng độ tối của mực xanh (chữ ký vẽ bằng mực
        // xanh), cột 20–29 nền trắng.
        var ink = SignatureImage.InkColor;
        var inkGray = (byte)(((299 * ink.R) + (587 * ink.G) + (114 * ink.B)) / 1000);
        const int Width = 30, Height = 4;
        var raw = new byte[Height * (Width + 1)];
        for (var row = 0; row < Height; row++)
        {
            for (var x = 0; x < Width; x++)
            {
                raw[(row * (Width + 1)) + 1 + x] = x < 10 ? (byte)0 : x < 20 ? inkGray : (byte)255;
            }
        }

        var colored = SignatureImage.Colorize(TestPng.Build(Width, Height, 8, 0, raw));

        Assert.Equal((Width, Height, (byte)2), TestPng.ReadHeader(colored));
        var pixels = DecodeRgb(colored, Width, Height);
        Assert.Equal((ink.R, ink.G, ink.B), Pixel(pixels, Width, 3, 1));
        Assert.Equal((ink.R, ink.G, ink.B), Pixel(pixels, Width, 15, 2));
        Assert.Equal(((byte)255, (byte)255, (byte)255), Pixel(pixels, Width, 25, 3));
        Assert.DoesNotContain("tEXt"u8.ToArray(), Chunks(colored));
    }

    [Fact]
    public void Colorize_KeepsTheSizeOfWhatNormalizeStores()
    {
        var stored = SignatureImage.Normalize(TestPng.Signature()).Png!;

        var colored = SignatureImage.Colorize(stored);

        var (width, height, _) = TestPng.ReadHeader(stored);
        Assert.Equal((width, height, (byte)2), TestPng.ReadHeader(colored));
    }

    [Fact]
    public void Colorize_ReturnsTheImageUnchanged_WhenItCannotBeDecoded()
    {
        var notPng = "khong phai anh PNG nao ca, chi la chu"u8.ToArray();
        var corrupt = TestPng.Build(30, 4, 8, 0, new byte[4 * 31], corruptCrc: true);

        Assert.Same(notPng, SignatureImage.Colorize(notPng));
        Assert.Same(corrupt, SignatureImage.Colorize(corrupt));
    }

    /// <summary>Đọc điểm ảnh RGB của PNG do máy chủ tạo (một khối nén, mọi dòng không dùng bộ lọc).</summary>
    private static byte[] DecodeRgb(byte[] png, int width, int height)
    {
        using var idat = new MemoryStream();
        for (var offset = 8; offset + 8 <= png.Length;)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            if (Encoding.ASCII.GetString(png, offset + 4, 4) == "IDAT")
            {
                idat.Write(png, offset + 8, length);
            }

            offset += 12 + length;
        }

        idat.Position = 0;
        var stride = width * 3;
        var raw = new byte[height * (stride + 1)];
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.ReadExactly(raw);
        }

        var pixels = new byte[height * stride];
        for (var row = 0; row < height; row++)
        {
            Assert.Equal(0, raw[row * (stride + 1)]);
            Array.Copy(raw, (row * (stride + 1)) + 1, pixels, row * stride, stride);
        }

        return pixels;
    }

    private static (byte R, byte G, byte B) Pixel(byte[] pixels, int width, int x, int y)
    {
        var i = ((y * width) + x) * 3;
        return (pixels[i], pixels[i + 1], pixels[i + 2]);
    }

    private static IEnumerable<byte[]> Chunks(byte[] png)
    {
        for (var offset = 8; offset + 8 <= png.Length;)
        {
            var length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            yield return png[(offset + 4)..(offset + 8)];
            offset += 12 + length;
        }
    }
}
