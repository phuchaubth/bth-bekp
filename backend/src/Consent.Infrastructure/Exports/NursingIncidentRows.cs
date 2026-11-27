using Consent.Application.NursingReports;

namespace Consent.Infrastructure.Exports;

/// <summary>
/// Mục F "Báo cáo cụ thể sự cố" của bảng tổng hợp điều dưỡng, dùng chung cho PDF và Excel: phần nhập thêm của một khoa (tối đa
/// 20.000 ký tự) được chia thành nhiều dòng bảng. Một dòng bảng PDF không tách sang trang sau được, một dòng Excel cao tối đa
/// 409 điểm: để cả phần nhập thêm dài trong một dòng thì khi in chỉ được khoảng 5.000 ký tự đầu, phần sau bị mất. Mỗi dòng tối
/// đa <see cref="MaxChars"/> ký tự và <see cref="MaxLines"/> dòng chữ (vừa một trang PDF và chiều cao tối đa của dòng Excel); chỗ
/// chia là chỗ xuống dòng hoặc dấu cách, không cắt giữa từ (trừ từ dài hơn <see cref="MaxChars"/> ký tự). Dòng đầu ghi mã khoa,
/// các dòng sau ghi "mã khoa (tiếp)".
/// </summary>
internal static class NursingIncidentRows
{
    internal const int MaxChars = 1000;

    internal const int MaxLines = 20;

    internal const string ContinuedSuffix = " (tiếp)";

    /// <summary>Các dòng của mục F theo thứ tự khoa: (cột khoa, nội dung).</summary>
    public static IEnumerable<(string Label, string Text)> For(IEnumerable<NursingSummaryNoteDto> incidents) =>
        incidents.SelectMany(incident => Split(incident.Text)
            .Select((part, index) => (index == 0 ? incident.DepartmentCode : incident.DepartmentCode + ContinuedSuffix, part)));

    /// <summary>Chia chữ thành các phần theo giới hạn trên; chữ ngắn giữ nguyên một phần. Chỉ bỏ khoảng trắng ở chỗ chia.</summary>
    internal static IReadOnlyList<string> Split(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var parts = new List<string>();
        var start = 0;
        while (start < normalized.Length)
        {
            var end = Math.Min(normalized.Length, start + MaxChars);
            var lines = 1;
            for (var i = start; i < end; i++)
            {
                // Lần xuống dòng làm phần này vượt quá MaxLines dòng chữ là chỗ chia.
                if (normalized[i] == '\n' && ++lines > MaxLines)
                {
                    end = i + 1;
                    break;
                }
            }

            if (end < normalized.Length && normalized[end - 1] != '\n')
            {
                // Chia ở chỗ xuống dòng nếu nằm ở nửa sau của phần, không thì ở dấu cách cuối cùng; không có cả hai (một từ rất
                // dài) thì đành cắt ngang.
                var count = end - start;
                var newline = normalized.LastIndexOf('\n', end - 1, count);
                var space = normalized.LastIndexOf(' ', end - 1, count);
                var cut = newline >= start + (count / 2) ? newline : Math.Max(newline, space);
                if (cut > start)
                {
                    end = cut + 1;
                }
            }

            var part = normalized[start..end].Trim(' ', '\n');
            if (part.Length > 0)
            {
                parts.Add(part);
            }

            start = end;
        }

        return parts;
    }
}
