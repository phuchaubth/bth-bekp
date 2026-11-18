using System.Text.RegularExpressions;

namespace BEKP.Domain.Common;

/// <summary>Validation of human-readable business codes (kept separate from UUID keys).</summary>
public static partial class BusinessCodes
{
    public const int MaxLength = 16;

    public static bool IsDepartmentCode(string? value) => value is not null && DepartmentPattern().IsMatch(value);

    /// <summary>Anonymous inpatient episode code, e.g. ADM-000123. Never a name or medical record number.</summary>
    public static bool IsStayCode(string? value) => value is not null && StayPattern().IsMatch(value);

    public static bool IsBedCode(string? value) => value is not null && BedPattern().IsMatch(value);

    [GeneratedRegex(@"^DEP-\d{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex DepartmentPattern();

    [GeneratedRegex(@"^ADM-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex StayPattern();

    [GeneratedRegex(@"^[A-Z0-9]{1,6}(-[A-Z0-9]{1,6})?$", RegexOptions.CultureInvariant)]
    private static partial Regex BedPattern();
}
