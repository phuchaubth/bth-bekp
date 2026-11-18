namespace Consent.Domain.Patients;

/// <summary>
/// Quy tắc số định danh cá nhân/CCCD 12 chữ số:
/// 3 số đầu là mã tỉnh nơi đăng ký khai sinh (001–096); số thứ 4 là mã thế kỷ và giới tính
/// (0/1: thế kỷ 20 nam/nữ, 2/3: thế kỷ 21, 4/5: thế kỷ 22, 6/7: thế kỷ 23, 8/9: thế kỷ 19);
/// số thứ 5–6 là hai số cuối năm sinh.
/// </summary>
public static class CitizenIdNumber
{
    public const int Length = 12;
    private const int MaxProvinceCode = 96;

    private static readonly int[] CenturyStarts = [1900, 2000, 2100, 2200, 1800];

    public static bool HasValidFormat(string? value)
    {
        if (value is not { Length: Length } || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        var province = int.Parse(value.AsSpan(0, 3), System.Globalization.CultureInfo.InvariantCulture);
        return province is >= 1 and <= MaxProvinceCode;
    }

    /// <summary>Năm sinh suy ra từ số CCCD.</summary>
    public static int BirthYear(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var centuryCode = value[3] - '0';
        return CenturyStarts[centuryCode / 2] + int.Parse(value.AsSpan(4, 2), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static Gender GenderOf(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return (value[3] - '0') % 2 == 0 ? Gender.Male : Gender.Female;
    }

    /// <summary>Kiểm tra số CCCD khớp năm sinh và giới tính. Trả về thông báo lỗi tiếng Việt hoặc <c>null</c>.</summary>
    public static string? CheckConsistency(string value, DateOnly dateOfBirth, Gender gender)
    {
        if (!HasValidFormat(value))
        {
            return "Số CCCD phải gồm 12 chữ số, 3 số đầu là mã tỉnh hợp lệ.";
        }

        if (BirthYear(value) != dateOfBirth.Year)
        {
            return "Số CCCD không khớp với năm sinh.";
        }

        return GenderOf(value) != gender ? "Số CCCD không khớp với giới tính." : null;
    }
}
