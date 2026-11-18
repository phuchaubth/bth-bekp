using System.Globalization;
using System.Text;
using Consent.Domain.Identity;

namespace Consent.Application.Roles;

/// <summary>Mã của vai trò do quản trị tạo (quyết định 40).</summary>
public static class RoleCodes
{
    /// <summary>
    /// Sinh mã từ tên: tiền tố <see cref="Role.CustomCodePrefix"/>, tên bỏ dấu viết hoa, các chữ nối bằng "_"
    /// ("Điều dưỡng trưởng khoa" → "VT_DIEU_DUONG_TRUONG_KHOA"). Trùng mã đã có thì thêm "_2", "_3"… Mã chỉ gồm chữ A–Z, số
    /// và "_", nên dùng được trong cấu hình ký của mẫu phiếu.
    /// </summary>
    public static string FromName(string name, IEnumerable<string> existingCodes)
    {
        ArgumentNullException.ThrowIfNull(existingCodes);
        var core = new StringBuilder();
        foreach (var ch in (name ?? string.Empty).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = ch is 'đ' or 'Đ' ? 'D' : char.ToUpperInvariant(ch);
            var keep = upper is >= 'A' and <= 'Z' or >= '0' and <= '9';
            if (keep)
            {
                core.Append(upper);
            }
            else if (core.Length > 0 && core[^1] != '_')
            {
                core.Append('_');
            }
        }

        var text = core.ToString().TrimEnd('_');
        if (text.Length == 0)
        {
            text = "VAI_TRO";
        }

        // Chừa chỗ cho hậu tố "_999" để mã không quá độ dài cho phép.
        var maxCore = Role.CodeMaxLength - Role.CustomCodePrefix.Length - 4;
        if (text.Length > maxCore)
        {
            text = text[..maxCore].TrimEnd('_');
        }

        var code = Role.CustomCodePrefix + text;
        var taken = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        for (var suffix = 2; taken.Contains(code); suffix++)
        {
            code = $"{Role.CustomCodePrefix}{text}_{suffix}";
        }

        return code;
    }
}
