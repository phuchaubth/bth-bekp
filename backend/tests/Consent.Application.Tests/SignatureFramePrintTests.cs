using Consent.Application.ConsentForms;
using Consent.Application.Tests.Fakes;

namespace Consent.Application.Tests;

/// <summary>In chữ ký lên PDF trong khung chữ ký của bệnh viện (quyết định 26).</summary>
public sealed class SignatureFramePrintTests
{
    private static readonly (byte R, byte G, byte B) Red = (237, 28, 36);

    private static readonly (byte R, byte G, byte B) White = (255, 255, 255);

    /// <summary>Khung giả lập (không phải khung thật): nền trong suốt, ô "logo" đỏ ở góc trên bên trái (x 40–119, y 20–59).</summary>
    private static byte[] Frame(int width = 400, int height = 300)
    {
        var stride = (width * 4) + 1;
        var raw = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x is >= 40 and < 120 && y is >= 20 and < 60)
                {
                    var i = (y * stride) + 1 + (x * 4);
                    (raw[i], raw[i + 1], raw[i + 2], raw[i + 3]) = (Red.R, Red.G, Red.B, 255);
                }
            }
        }

        return TestPng.Build(width, height, 8, 6, raw);
    }

    private static byte[] StoredSignature() => SignatureImage.Normalize(TestPng.Signature()).Png!;

    private static bool IsInk((byte R, byte G, byte B) pixel) => pixel.B > pixel.R + 60 && pixel.B > pixel.G + 40;

    [Fact]
    public void Signature_IsPrintedInBlueInsideTheEmptyAreaOfTheFrame_LeavingTheLogoUntouched()
    {
        var print = SignatureFramePrint.Prepare(Frame())!;

        var composed = print.Compose(StoredSignature())!;

        Assert.Equal((400, 300), (print.Width, print.Height));
        Assert.Equal((400, 300, (byte)2), TestPng.ReadHeader(composed));
        var rgb = TestPng.ReadRgb(composed, 400, 300);
        Assert.Equal(Red, TestPng.Pixel(rgb, 400, 80, 40));
        Assert.Equal(White, TestPng.Pixel(rgb, 400, 390, 5));
        var inside = 0;
        var outside = 0;
        for (var y = 0; y < 300; y++)
        {
            for (var x = 0; x < 400; x++)
            {
                if (!IsInk(TestPng.Pixel(rgb, 400, x, y)))
                {
                    continue;
                }

                // Vùng đặt chữ ký: 8% hai bên và dưới, 26% phía trên (dưới phần logo).
                if (x >= 32 && x < 368 && y >= 78 && y < 276)
                {
                    inside++;
                }
                else
                {
                    outside++;
                }
            }
        }

        Assert.True(inside > 500, $"Chỉ có {inside} điểm mực trong vùng ký.");
        Assert.Equal(0, outside);
    }

    [Fact]
    public void LargeFrame_IsShrunkToThePrintWidth_KeepingItsShape()
    {
        var print = SignatureFramePrint.Prepare(Frame(1440, 1080))!;

        Assert.Equal((SignatureFramePrint.PrintWidth, 375), (print.Width, print.Height));
        Assert.NotNull(print.Compose(StoredSignature()));
    }

    [Fact]
    public void PalettePng_IsRead_WhileJpegInterlacedOrBrokenFilesAreNot()
    {
        // PNG bảng màu 8 bit 20 × 10, mọi điểm là màu số 1 (đỏ).
        var raw = new byte[10 * 21];
        for (var y = 0; y < 10; y++)
        {
            Array.Fill(raw, (byte)1, (y * 21) + 1, 20);
        }

        var palette = TestPng.Build(20, 10, 8, 3, raw, palette: [0, 0, 0, Red.R, Red.G, Red.B]);
        var paletteWithoutColours = TestPng.Build(20, 10, 8, 3, raw);

        var print = SignatureFramePrint.Prepare(palette);

        Assert.NotNull(print);
        Assert.Equal(Red, TestPng.Pixel(TestPng.ReadRgb(print.Compose(StoredSignature())!, 20, 10), 20, 0, 0));
        Assert.Null(SignatureFramePrint.Prepare(paletteWithoutColours));
        Assert.Null(SignatureFramePrint.Prepare(Convert.FromBase64String(TestLogos.Jpeg40x32)));
        Assert.Null(SignatureFramePrint.Prepare(TestPng.Build(40, 40, 8, 2, new byte[40 * 121], interlace: 1)));
        Assert.Null(SignatureFramePrint.Prepare(TestPng.Build(40, 40, 8, 2, new byte[40 * 121], corruptCrc: true)));
    }

    [Fact]
    public void UnreadableSignature_GivesNothingToPrint()
    {
        var print = SignatureFramePrint.Prepare(Frame())!;

        Assert.Null(print.Compose("khong phai anh chu ky PNG nao ca"u8.ToArray()));
    }
}
