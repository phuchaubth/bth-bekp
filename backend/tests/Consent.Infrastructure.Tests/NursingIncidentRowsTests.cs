using System.Text;
using Consent.Application.NursingReports;
using Consent.Domain.NursingReports;
using Consent.Infrastructure.Exports;

namespace Consent.Infrastructure.Tests;

/// <summary>
/// Mục F của bảng tổng hợp điều dưỡng: phần nhập thêm dài được chia thành nhiều dòng bảng để PDF tách được trang và dòng Excel
/// không vượt chiều cao tối đa, không mất chữ nào.
/// </summary>
public sealed class NursingIncidentRowsTests
{
    /// <summary>Phần nhập thêm dài gần tới giới hạn 20.000 ký tự (dữ liệu giả lập): câu có đánh số, xuống dòng sau mỗi 3 câu.</summary>
    internal static string LongNotes()
    {
        var text = new StringBuilder();
        for (var i = 1; ; i++)
        {
            var sentence = (i == 1 ? string.Empty : i % 3 == 1 ? "\n" : " ")
                + $"Câu {i:0000}: người bệnh giả lập theo dõi sau mổ, dấu hiệu sinh tồn ổn định, đã báo bác sĩ trực.";
            if (text.Length + sentence.Length > NursingDailyReport.NotesMaxLength)
            {
                return text.ToString();
            }

            text.Append(sentence);
        }
    }

    /// <summary>Phần nhập thêm gồm nhiều dòng ngắn (dữ liệu giả lập): mỗi dòng bảng đủ 20 dòng chữ, dòng Excel cao nhất.</summary>
    internal static string ManyShortLines() =>
        string.Join('\n', Enumerable.Range(1, 600).Select(i => $"Dòng {i:000}: NB giả lập ổn định."));

    private static string[] Words(string text) => text.Split([' ', '\n'], StringSplitOptions.RemoveEmptyEntries);

    private static int Lines(string text) => text.Split('\n').Length;

    [Fact]
    public void ShortText_StaysOneRow()
    {
        Assert.Equal(["Người bệnh A (giả lập) sốt về đêm\nĐã báo bác sĩ trực"], NursingIncidentRows.Split("Người bệnh A (giả lập) sốt về đêm\r\nĐã báo bác sĩ trực"));
        var limit = new string('x', NursingIncidentRows.MaxChars);
        Assert.Equal([limit], NursingIncidentRows.Split(limit));
    }

    [Fact]
    public void LongText_IsSplitWithinLimits_AtSpacesOrLineBreaks_WithoutLosingWords()
    {
        var notes = LongNotes();
        Assert.InRange(notes.Length, NursingDailyReport.NotesMaxLength - 100, NursingDailyReport.NotesMaxLength);

        var parts = NursingIncidentRows.Split(notes);

        Assert.True(parts.Count > 1);
        Assert.All(parts, part =>
        {
            Assert.InRange(part.Length, 1, NursingIncidentRows.MaxChars);
            Assert.InRange(Lines(part), 1, NursingIncidentRows.MaxLines);
            Assert.Equal(part.Trim(), part);
        });
        // Mọi từ còn đủ, đúng thứ tự: không cắt giữa từ, không mất chữ.
        Assert.Equal(Words(notes), parts.SelectMany(Words));
        Assert.EndsWith("đã báo bác sĩ trực.", parts[^1], StringComparison.Ordinal);
    }

    [Fact]
    public void LineBreak_InTheSecondHalf_IsPreferredOverALaterSpace()
    {
        var first = string.Join(' ', Enumerable.Repeat("chữ", 175)); // 699 ký tự
        var second = string.Join(' ', Enumerable.Repeat("số", 300));

        var parts = NursingIncidentRows.Split(first + "\n" + second);

        Assert.Equal(first, parts[0]);
        Assert.StartsWith("số", parts[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ManyShortLines_AreSplitByLineCount()
    {
        var notes = string.Join('\n', Enumerable.Range(1, 95).Select(i => $"Dòng {i}"));

        var parts = NursingIncidentRows.Split(notes);

        Assert.Equal([20, 20, 20, 20, 15], parts.Select(Lines));
        Assert.Equal("Dòng 1", parts[0].Split('\n')[0]);
        Assert.Equal("Dòng 21", parts[1].Split('\n')[0]);
        Assert.Equal(Words(notes), parts.SelectMany(Words));
    }

    [Fact]
    public void VeryLongWord_IsCutWithoutLosingCharacters()
    {
        var word = string.Concat(Enumerable.Repeat("abcdefghij", 250));

        var parts = NursingIncidentRows.Split(word);

        Assert.Equal([NursingIncidentRows.MaxChars, NursingIncidentRows.MaxChars, 500], parts.Select(p => p.Length));
        Assert.Equal(word, string.Concat(parts));
        // Phần cuối chỉ còn một ký tự vẫn được giữ.
        Assert.Equal([new string('x', NursingIncidentRows.MaxChars), "y"], NursingIncidentRows.Split(new string('x', NursingIncidentRows.MaxChars) + "y"));
    }

    [Fact]
    public void For_LabelsContinuationRows_KeepingDepartmentOrder()
    {
        var incidents = new[]
        {
            new NursingSummaryNoteDto(Guid.NewGuid(), "NOIA", "Khoa Nội A (giả lập)", LongNotes()),
            new NursingSummaryNoteDto(Guid.NewGuid(), "NGOAIB", "Khoa Ngoại B (giả lập)", "Người bệnh B (giả lập) chuyển khoa"),
        };

        var rows = NursingIncidentRows.For(incidents).ToList();

        Assert.Equal("NOIA", rows[0].Label);
        var continued = rows.Skip(1).TakeWhile(r => r.Label != "NGOAIB").ToList();
        Assert.NotEmpty(continued);
        Assert.All(continued, r => Assert.Equal("NOIA (tiếp)", r.Label));
        Assert.Equal(("NGOAIB", "Người bệnh B (giả lập) chuyển khoa"), rows[^1]);
        Assert.Equal(continued.Count + 2, rows.Count);
    }
}
