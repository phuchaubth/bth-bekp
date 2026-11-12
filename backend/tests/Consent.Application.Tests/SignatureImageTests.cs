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
