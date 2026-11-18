using System.Text;
using Consent.Application.Settings;

namespace Consent.Application.Tests;

public sealed class LogoImageTests
{
    [Fact]
    public void Png_IsAccepted_WithSizeReadFromHeader()
    {
        var logo = LogoImage.Parse(TestLogos.Png64);

        Assert.True(logo.IsSuccess);
        Assert.Equal((LogoImage.Png, 64, 64), (logo.Value.ContentType, logo.Value.Width, logo.Value.Height));
    }

    [Fact]
    public void Jpeg_WithDataUrlPrefix_IsAccepted()
    {
        var logo = LogoImage.Parse("data:image/jpeg;base64," + TestLogos.Jpeg40x32);

        Assert.True(logo.IsSuccess);
        Assert.Equal((LogoImage.Jpeg, 40, 32), (logo.Value.ContentType, logo.Value.Width, logo.Value.Height));
    }

    public static TheoryData<string?, string> Rejected => new()
    {
        { null, "Vui lòng chọn file logo." },
        { "   ", "Vui lòng chọn file logo." },
        { "khong-phai-base64!", "File logo không hợp lệ." },
        // SVG (có thể chứa mã chạy được) và GIF: không nhận.
        { Convert.ToBase64String(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")), "Logo phải là ảnh PNG hoặc JPG." },
        { Convert.ToBase64String("GIF89a\u0001\u0000\u0001\u0000"u8.ToArray()), "Logo phải là ảnh PNG hoặc JPG." },
        { Convert.ToBase64String(new byte[LogoImage.MaxBytes + 1]), "Logo tối đa 1 MB." },
        { WithPngSize(8, 8), "Kích thước logo phải từ 32 đến 4000 điểm ảnh mỗi chiều." },
        { WithPngSize(5000, 100), "Kích thước logo phải từ 32 đến 4000 điểm ảnh mỗi chiều." },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void InvalidFiles_AreRejected_OnTheImageField(string? base64, string message)
    {
        var result = LogoImage.Parse(base64);

        Assert.Equal([message], result.Error!.FieldErrors!["image"]);
    }

    /// <summary>PNG giả lập với kích thước ghi trong phần đầu file đã bị đổi.</summary>
    private static string WithPngSize(int width, int height)
    {
        var bytes = Convert.FromBase64String(TestLogos.Png64);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16, 4), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20, 4), height);
        return Convert.ToBase64String(bytes);
    }
}
