using System.Text;
using System.Text.RegularExpressions;

namespace Consent.Infrastructure.Pdf;

/// <summary>Một đoạn chữ của mẫu phiếu song ngữ: tiếng Việt (in đen) hoặc tiếng Anh (in xanh).</summary>
internal readonly record struct TextSegment(string Text, bool IsEnglish);

/// <summary>
/// Tách chữ của mẫu phiếu song ngữ thành phần tiếng Việt và tiếng Anh. Mẫu ghi tiếng Anh theo hai cách:
/// trong ngoặc sau tiếng Việt ("Có (Yes)") hoặc thành dòng riêng ở tiêu đề/phụ đề ("Giấy đồng thuận\nConsent Form").
/// Một đoạn được coi là tiếng Anh khi có chữ thường không dấu và tối đa 1/4 số từ có dấu tiếng Việt (cho phép tên riêng
/// trong ngoặc kép như “BV Truyền máu Huyết học”). Trong ngoặc ghép "tiếng Việt – tiếng Anh" thì xét từng phần.
/// Cùng quy tắc với frontend/src/features/consents/bilingual.ts.
/// </summary>
internal static partial class BilingualText
{
    private const string PartSeparator = " – ";

    /// <param name="wholeLines">Cho phép cả dòng là tiếng Anh (chỉ dùng cho tiêu đề, phụ đề).</param>
    public static IReadOnlyList<TextSegment> Split(string text, bool wholeLines = false)
    {
        var segments = new List<TextSegment>();
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                Add(segments, "\n", false);
            }

            if (wholeLines && IsEnglish(lines[i], allowVietnameseWords: false))
            {
                Add(segments, lines[i], true);
            }
            else
            {
                SplitParentheses(segments, lines[i]);
            }
        }

        return segments;
    }

    private static void SplitParentheses(List<TextSegment> segments, string line)
    {
        var plain = new StringBuilder();
        var i = 0;
        while (i < line.Length)
        {
            var close = line[i] == '(' ? MatchingClose(line, i) : -1;
            if (close < 0)
            {
                plain.Append(line[i]);
                i++;
                continue;
            }

            var inner = line[(i + 1)..close];
            if (IsEnglish(inner))
            {
                Add(segments, plain.ToString(), false);
                plain.Clear();
                Add(segments, line[i..(close + 1)], true);
            }
            else if (inner.Contains(PartSeparator, StringComparison.Ordinal))
            {
                plain.Append('(');
                var parts = inner.Split(PartSeparator);
                for (var p = 0; p < parts.Length; p++)
                {
                    if (p > 0)
                    {
                        plain.Append(PartSeparator);
                    }

                    if (IsEnglish(parts[p]))
                    {
                        Add(segments, plain.ToString(), false);
                        plain.Clear();
                        Add(segments, parts[p], true);
                    }
                    else
                    {
                        plain.Append(parts[p]);
                    }
                }

                plain.Append(')');
            }
            else
            {
                plain.Append(line, i, close - i + 1);
            }

            i = close + 1;
        }

        Add(segments, plain.ToString(), false);
    }

    private static int MatchingClose(string line, int open)
    {
        var depth = 0;
        for (var i = open; i < line.Length; i++)
        {
            if (line[i] == '(')
            {
                depth++;
            }
            else if (line[i] == ')' && --depth == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <param name="allowVietnameseWords">
    /// Cho phép tối đa 1/4 số từ có dấu (tên riêng trong câu tiếng Anh). Cả dòng tiêu đề thì không cho phép, để dòng như
    /// "(Outpatient …) – Mã số (Code): …" chỉ tô phần trong ngoặc.
    /// </param>
    internal static bool IsEnglish(string text, bool allowVietnameseWords = true)
    {
        var trimmed = text.Trim();
        if (!trimmed.Any(c => c is >= 'a' and <= 'z'))
        {
            return false;
        }

        // Đơn vị, chữ viết tắt ngắn viết thường ("kg", "cm") không coi là tiếng Anh; "No", "Yes" thì có.
        if (trimmed.Length < 4 && !char.IsAsciiLetterUpper(trimmed[0]))
        {
            return false;
        }

        // Tên riêng trong ngoặc kép (“BV Truyền máu Huyết học”) không tính khi đếm từ có dấu.
        var words = QuotedText().Replace(trimmed, " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Any(char.IsLetter)).ToList();
        var vietnamese = words.Count(w => w.Any(c => c > '\u007F' && char.IsLetter(c)));
        return allowVietnameseWords ? vietnamese * 4 <= words.Count : vietnamese == 0;
    }

    private static void Add(List<TextSegment> segments, string text, bool english)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (segments.Count > 0 && segments[^1].IsEnglish == english && text != "\n" && segments[^1].Text != "\n")
        {
            segments[^1] = new TextSegment(segments[^1].Text + text, english);
            return;
        }

        segments.Add(new TextSegment(text, english));
    }

    [GeneratedRegex("“[^”]*”")]
    private static partial Regex QuotedText();
}
