using System.Text.RegularExpressions;

namespace Consent.Domain.Patients;

/// <summary>
/// Định dạng số thẻ BHYT được chấp nhận:
/// <list type="bullet">
/// <item>Mã thẻ mới 17 ký tự: 5 ký tự đầu như mã cũ (2 chữ + 3 số) và 12 số định danh (CCCD).</item>
/// <item>Mã thẻ cũ 15 ký tự (2 chữ + 13 số): vẫn chấp nhận cho hồ sơ đã có, nhưng cần báo người dùng cập nhật.</item>
/// </list>
/// </summary>
public static partial class HealthInsuranceNumber
{
    public const int MaxLength = 17;

    public const string Pattern = "^[A-Za-z]{2}([0-9]{15}|[0-9]{13})$";

    public const string LegacyPattern = "^[A-Za-z]{2}[0-9]{13}$";

    public const string FormatMessage =
        "Số thẻ BHYT gồm 17 ký tự (2 chữ + 15 số); mã thẻ cũ 15 ký tự (2 chữ + 13 số) vẫn được chấp nhận.";

    public const string LegacyNotice =
        "Số thẻ BHYT trong hồ sơ người bệnh là mã cũ 15 ký tự. Mã cũ vẫn được chấp nhận, nhưng nên cập nhật hồ sơ sang mã thẻ mới 17 ký tự.";

    public static bool IsValid(string? value) => value is not null && ValidRegex().IsMatch(value);

    public static bool IsLegacy(string? value) => value is not null && LegacyRegex().IsMatch(value);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex ValidRegex();

    [GeneratedRegex(LegacyPattern, RegexOptions.CultureInvariant)]
    private static partial Regex LegacyRegex();
}
