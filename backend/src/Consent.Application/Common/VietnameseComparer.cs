using System.Globalization;

namespace Consent.Application.Common;

/// <summary>So sánh chuỗi theo thứ tự chữ cái tiếng Việt (Đ sau D, không phân biệt hoa/thường) để sắp xếp hiển thị.</summary>
public static class VietnameseComparer
{
    public static StringComparer Instance { get; } = StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), ignoreCase: true);
}
