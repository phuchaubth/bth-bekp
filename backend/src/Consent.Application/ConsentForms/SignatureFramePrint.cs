namespace Consent.Application.ConsentForms;

/// <summary>
/// In chữ ký lên PDF trong khung chữ ký của bệnh viện (quyết định 26): ghép ảnh khung với chữ ký mực xanh thành một ảnh. Chữ ký
/// đặt giữa vùng trống của khung, tránh phần logo phía trên, giống khung hiện chữ ký mẫu trên màn hình; giữ tỉ lệ chữ ký.
/// Chỗ chưa ký in khung trống (<see cref="EmptyPng"/>). Ảnh khung được thu nhỏ một lần khi chuẩn bị, rồi dùng cho mọi ô ký của
/// một phiếu.
/// </summary>
public sealed class SignatureFramePrint
{
    /// <summary>Bề rộng ảnh ghép (điểm ảnh): in khoảng 50 mm vẫn nét (khoảng 250 dpi) mà PDF không quá nặng.</summary>
    public const int PrintWidth = 500;

    /// <summary>Cạnh lớn nhất của ảnh khung đọc được.</summary>
    public const int MaxFrameSide = 4000;

    // Vùng đặt chữ ký trong khung, theo tỉ lệ khung (trùng SignatureFrameBox trên màn hình: 26% trên, 8% hai bên và dưới).
    private const double AreaTop = 0.26;
    private const double AreaBottom = 0.92;
    private const double AreaLeft = 0.08;
    private const double AreaRight = 0.92;

    private readonly byte[] _rgb;

    private byte[]? _emptyPng;

    private SignatureFramePrint(int width, int height, byte[] rgb)
    {
        Width = width;
        Height = height;
        _rgb = rgb;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Ảnh PNG của khung trống, in ở chỗ chưa ký (phiếu nháp, người ký chưa ký).</summary>
    public byte[] EmptyPng => _emptyPng ??= SignatureImage.EncodeRgb(_rgb, Width, Height);

    /// <summary>
    /// Đọc ảnh khung (PNG 8 bit không xen kẽ, kể cả PNG bảng màu; vùng trong suốt thành trắng) và thu nhỏ còn
    /// <see cref="PrintWidth"/> điểm ảnh. Không đọc được (ảnh JPG, PNG 16 bit hay xen kẽ, file hỏng) thì null: phiếu in chữ ký
    /// không khung như trước.
    /// </summary>
    public static SignatureFramePrint? Prepare(byte[] framePng)
    {
        ArgumentNullException.ThrowIfNull(framePng);
        if (SignatureImage.ReadColorOnWhite(framePng, MaxFrameSide) is not { } frame)
        {
            return null;
        }

        if (frame.Width <= PrintWidth)
        {
            return new SignatureFramePrint(frame.Width, frame.Height, frame.Rgb);
        }

        var height = Math.Max(1, (int)Math.Round(frame.Height * (double)PrintWidth / frame.Width));
        return new SignatureFramePrint(PrintWidth, height, Shrink(frame.Rgb, frame.Width, frame.Height, PrintWidth, height, 3));
    }

    /// <summary>
    /// Ảnh PNG: khung + chữ ký đã lưu (ảnh xám, kết quả của <see cref="SignatureImage.Normalize"/>) tô màu mực
    /// <see cref="SignatureImage.InkColor"/>, đặt giữa vùng trống của khung. Ảnh chữ ký không đọc được thì null.
    /// </summary>
    public byte[]? Compose(byte[] storedSignaturePng)
    {
        ArgumentNullException.ThrowIfNull(storedSignaturePng);
        if (SignatureImage.ReadGray(storedSignaturePng) is not { } signature)
        {
            return null;
        }

        var areaX = (int)Math.Round(Width * AreaLeft);
        var areaY = (int)Math.Round(Height * AreaTop);
        var areaWidth = Math.Max(1, (int)Math.Round(Width * AreaRight) - areaX);
        var areaHeight = Math.Max(1, (int)Math.Round(Height * AreaBottom) - areaY);
        var scale = Math.Min((double)areaWidth / signature.Width, (double)areaHeight / signature.Height);
        var width = Math.Clamp((int)Math.Round(signature.Width * scale), 1, areaWidth);
        var height = Math.Clamp((int)Math.Round(signature.Height * scale), 1, areaHeight);
        var placed = scale < 1
            ? Shrink(signature.Pixels, signature.Width, signature.Height, width, height, 1)
            : Enlarge(signature.Pixels, signature.Width, signature.Height, width, height);

        var output = (byte[])_rgb.Clone();
        var left = areaX + ((areaWidth - width) / 2);
        var top = areaY + ((areaHeight - height) / 2);
        var full = 255 - SignatureImage.InkLuminance;
        var ink = SignatureImage.InkColor;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // Độ đậm của mực: nền trắng = 0; tối bằng mực xanh (hoặc tối hơn, như mực đen) = 255.
                var strength = Math.Min(255, (255 - placed[(y * width) + x]) * 255 / full);
                if (strength == 0)
                {
                    continue;
                }

                var i = (((top + y) * Width) + left + x) * 3;
                output[i] = Mix(output[i], ink.R, strength);
                output[i + 1] = Mix(output[i + 1], ink.G, strength);
                output[i + 2] = Mix(output[i + 2], ink.B, strength);
            }
        }

        return SignatureImage.EncodeRgb(output, Width, Height);
    }

    private static byte Mix(byte background, byte ink, int strength) => (byte)(((background * (255 - strength)) + (ink * strength) + 127) / 255);

    /// <summary>Thu nhỏ bằng trung bình vùng (mỗi điểm mới là trung bình các điểm cũ nó phủ), <paramref name="channels"/> kênh.</summary>
    private static byte[] Shrink(byte[] source, int width, int height, int targetWidth, int targetHeight, int channels)
    {
        var result = new byte[targetWidth * targetHeight * channels];
        Span<int> sums = stackalloc int[channels];
        for (var ty = 0; ty < targetHeight; ty++)
        {
            var y0 = (int)((long)ty * height / targetHeight);
            var y1 = Math.Max(y0 + 1, (int)((long)(ty + 1) * height / targetHeight));
            for (var tx = 0; tx < targetWidth; tx++)
            {
                var x0 = (int)((long)tx * width / targetWidth);
                var x1 = Math.Max(x0 + 1, (int)((long)(tx + 1) * width / targetWidth));
                sums.Clear();
                for (var y = y0; y < y1; y++)
                {
                    for (var x = x0; x < x1; x++)
                    {
                        for (var c = 0; c < channels; c++)
                        {
                            sums[c] += source[(((y * width) + x) * channels) + c];
                        }
                    }
                }

                var count = (y1 - y0) * (x1 - x0);
                for (var c = 0; c < channels; c++)
                {
                    result[(((ty * targetWidth) + tx) * channels) + c] = (byte)((sums[c] + (count / 2)) / count);
                }
            }
        }

        return result;
    }

    /// <summary>Phóng to ảnh xám bằng nội suy song tuyến (chữ ký nhỏ hơn vùng đặt chữ ký).</summary>
    private static byte[] Enlarge(byte[] source, int width, int height, int targetWidth, int targetHeight)
    {
        var result = new byte[targetWidth * targetHeight];
        for (var ty = 0; ty < targetHeight; ty++)
        {
            var sy = Math.Clamp(((ty + 0.5) * height / targetHeight) - 0.5, 0, height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, height - 1);
            var fy = sy - y0;
            for (var tx = 0; tx < targetWidth; tx++)
            {
                var sx = Math.Clamp(((tx + 0.5) * width / targetWidth) - 0.5, 0, width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, width - 1);
                var fx = sx - x0;
                var top = (source[(y0 * width) + x0] * (1 - fx)) + (source[(y0 * width) + x1] * fx);
                var bottom = (source[(y1 * width) + x0] * (1 - fx)) + (source[(y1 * width) + x1] * fx);
                result[(ty * targetWidth) + tx] = (byte)Math.Round((top * (1 - fy)) + (bottom * fy));
            }
        }

        return result;
    }
}
