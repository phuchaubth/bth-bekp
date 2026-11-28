using Consent.Application.CriticalResults;
using Consent.Application.Settings;
using Consent.Infrastructure.Pdf;
using MigraDoc.Rendering;
using Mdom = MigraDoc.DocumentObjectModel;

namespace Consent.Infrastructure.Tests;

/// <summary>
/// PDF sổ kết quả nguy cấp (quyết định 60) theo bố cục sổ giấy: ST01 (khoa cận lâm sàng), ST02 (khoa điều trị), sổ Khoa Vi sinh.
/// Dòng lấy sẵn từ tầng nghiệp vụ; PDF chỉ dàn trang. Dữ liệu giả lập.
/// </summary>
public sealed class CriticalLogbookPdfTests
{
    private static readonly CriticalLogbookForm[] Forms = [CriticalLogbookForm.Laboratory, CriticalLogbookForm.Clinical, CriticalLogbookForm.Microbiology];

    private static CriticalLogbookRow Row(CriticalLogbookForm form, int number, bool cancelled = false, string? note = null, string? text = null)
    {
        var count = CriticalLogbookService.ColumnsOf(form).Count;
        return new CriticalLogbookRow(
            [.. Enumerable.Range(0, count).Select(i => i == 0 ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : text ?? $"Ô {i + 1} (giả lập)")],
            cancelled,
            note);
    }

    private static CriticalLogbookDocument Document(
        CriticalLogbookForm form = CriticalLogbookForm.Laboratory, IReadOnlyList<CriticalLogbookRow>? rows = null, LogoImage? logo = null) => new(
        form,
        form == CriticalLogbookForm.Microbiology ? "QT-VS-21/ST02" : "QT-KQNC/ST01",
        "Sở Y tế Giả Lập",
        "Bệnh viện Giả Lập",
        logo,
        "Khoa Huyết học – Sinh hóa (giả lập)",
        "Từ 01/11/2026 đến 30/11/2026",
        CriticalLogbookService.ColumnsOf(form),
        rows ?? [Row(form, 1), Row(form, 2, cancelled: true, note: "Đã hủy 09:50 27/11/2026 (KTV giả lập): Nhập nhầm người bệnh"), Row(form, 3)],
        "In lúc 10:30 27/11/2026 · KTV giả lập");

    private static List<Mdom.DocumentObject> Body(Mdom.Document document) =>
        [.. document.Sections[0].Elements.Cast<Mdom.DocumentObject>()];

    private static string TextOf(Mdom.ParagraphElements elements) =>
        string.Concat(elements.Cast<Mdom.DocumentObject>().Select(element => element switch
        {
            Mdom.Text text => text.Content,
            Mdom.FormattedText formatted => TextOf(formatted.Elements),
            Mdom.Character { SymbolName: Mdom.SymbolName.LineBreak } => "\n",
            Mdom.Character { SymbolName: Mdom.SymbolName.Tab } => "\t",
            _ => string.Empty,
        }));

    private static string CellText(Mdom.Tables.Cell cell) =>
        string.Join('\n', cell.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().Select(p => TextOf(p.Elements)));

    private static List<string[]> Rows(Mdom.Tables.Table table) =>
        [.. Enumerable.Range(0, table.Rows.Count).Select(r => Enumerable.Range(0, table.Columns.Count).Select(c => CellText(table.Rows[r].Cells[c])).ToArray())];

    [Fact]
    public void ColumnWidths_FillTheLandscapePage_OneWidthPerColumn()
    {
        foreach (var form in Forms)
        {
            var widths = MigraDocCriticalLogbookPdfRenderer.ColumnsMm(form);
            Assert.Equal(CriticalLogbookService.ColumnsOf(form).Count, widths.Count);
            Assert.Equal(MigraDocCriticalLogbookPdfRenderer.ContentWidthMm, widths.Sum(), 3);
        }
    }

    [Theory]
    [InlineData(CriticalLogbookForm.Laboratory, "SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP\nKHOA CẬN LÂM SÀNG\nCRITICAL RESULT NOTIFICATION LOGBOOK – LABORATORY DEPARTMENT")]
    [InlineData(CriticalLogbookForm.Clinical, "SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP\nKHOA ĐIỀU TRỊ\nCRITICAL RESULT NOTIFICATION LOGBOOK – CLINICAL DEPARTMENT")]
    [InlineData(CriticalLogbookForm.Microbiology, "SỔ THÔNG BÁO KẾT QUẢ NGUY CẤP QUA ĐIỆN THOẠI\nKHOA VI SINH\nNOTIFICATION OF CRITICAL RESULTS BY TELEPHONE – MICROBIOLOGY DEPARTMENT")]
    public void Head_HasUnit_Code_TitleOfTheForm_DepartmentAndPeriod(CriticalLogbookForm form, string title)
    {
        var document = MigraDocCriticalLogbookPdfRenderer.Build(Document(form));

        Assert.Equal(Mdom.Orientation.Landscape, document.Sections[0].PageSetup.Orientation);
        var head = Rows(Body(document).OfType<Mdom.Tables.Table>().First())[0];
        Assert.Equal("SỞ Y TẾ GIẢ LẬP\nBỆNH VIỆN GIẢ LẬP", head[1]);
        Assert.Equal($"Mã số/ Code: {(form == CriticalLogbookForm.Microbiology ? "QT-VS-21/ST02" : "QT-KQNC/ST01")}", head[2]);
        var paragraphs = Body(document).OfType<Mdom.Paragraph>().Select(p => TextOf(p.Elements)).ToList();
        Assert.Contains(title, paragraphs);
        Assert.Contains("Khoa/ Department: Khoa Huyết học – Sinh hóa (giả lập)\tTừ 01/11/2026 đến 30/11/2026", paragraphs);
        var footer = TextOf(document.Sections[0].Footers.Primary.Elements.Cast<Mdom.Paragraph>().Single().Elements);
        Assert.Equal("In lúc 10:30 27/11/2026 · KTV giả lập\tLưu hành nội bộ/ For Internal Use Only\tTrang /", footer);
    }

    [Fact]
    public void Rows_RepeatHeadings_NumberColumns_StrikeCancelledRows_AndMergeNotes()
    {
        var table = Body(MigraDocCriticalLogbookPdfRenderer.Build(Document())).OfType<Mdom.Tables.Table>().Last();
        var rows = Rows(table);

        Assert.True(table.Rows[0].HeadingFormat && table.Rows[1].HeadingFormat);
        Assert.Equal(CriticalLogbookService.ColumnsOf(CriticalLogbookForm.Laboratory), rows[0]);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)), rows[1]);
        // Hai dòng tiêu đề, ba dòng sổ, một dòng ghi chú của dòng đã hủy.
        Assert.Equal(6, rows.Count);
        Assert.Equal("1", rows[2][0]);
        Assert.Equal(MigraDocQcLogPdfRenderer.Struck("2"), rows[3][0]);
        Assert.Equal(MigraDocQcLogPdfRenderer.Struck("Ô 2 (giả lập)"), rows[3][1]);
        Assert.Equal(new Mdom.Color(0x80, 0x80, 0x80).RGB, table.Rows[3].Cells[1].Elements.Cast<Mdom.Paragraph>().Single().Format.Font.Color.RGB);
        Assert.Equal("Ghi chú: Đã hủy 09:50 27/11/2026 (KTV giả lập): Nhập nhầm người bệnh", rows[4][0]);
        Assert.Equal(9, table.Rows[4].Cells[0].MergeRight);
        Assert.Equal(1, table.Rows[3].KeepWith);
        Assert.Equal("3", rows[5][0]);
        Assert.DoesNotContain(MigraDocQcLogPdfRenderer.StrikeMark, string.Concat(rows[5]));
    }

    [Fact]
    public void CheckBoxes_UseTheEmbeddedBoxFont_RestOfTheCellStaysInTinos()
    {
        // Tinos không có ☒/☐: in bằng Tinos thì ô đã đánh dấu thành ô trống (lỗi thấy khi xem PDF sổ ST01 thật).
        var count = CriticalLogbookService.ColumnsOf(CriticalLogbookForm.Laboratory).Count;
        string[] cells = [.. Enumerable.Range(0, count).Select(i => i == 0 ? "1" : $"Ô {i + 1} (giả lập)")];
        const string readBack = "☒ Chính xác (đọc lại lần 2)";
        cells[count - 1] = readBack;
        var table = Body(MigraDocCriticalLogbookPdfRenderer.Build(Document(rows: [new(cells, false, null), new(cells, true, "Đã hủy (giả lập)")])))
            .OfType<Mdom.Tables.Table>()
            .Last();

        foreach (var (row, cancelled) in new[] { (2, false), (3, true) })
        {
            var parts = table.Rows[row].Cells[count - 1].Elements.Cast<Mdom.Paragraph>().Single().Elements.Cast<Mdom.DocumentObject>().ToList();
            Assert.Equal(2, parts.Count);
            var box = Assert.IsType<Mdom.FormattedText>(parts[0]);
            Assert.Equal("☒", TextOf(box.Elements));
            Assert.Equal(EmbeddedFontResolver.BoxFamilyName, box.FontName);
            var expected = cancelled ? MigraDocQcLogPdfRenderer.Struck(readBack)[1..] : readBack[1..];
            Assert.Equal(expected, Assert.IsType<Mdom.Text>(parts[1]).Content);
        }

        // Ô không có ký tự đánh dấu: một đoạn chữ Tinos như cũ.
        Assert.IsType<Mdom.Text>(table.Rows[2].Cells[1].Elements.Cast<Mdom.Paragraph>().Single().Elements.Cast<Mdom.DocumentObject>().Single());
    }

    [Theory]
    [InlineData("☐", 1)]
    [InlineData("Khẩn ☒ / ☐ (giả lập)", 5)]
    [InlineData("☒☒", 2)]
    public void CheckBoxes_EveryBoxIsItsOwnRun(string text, int elements)
    {
        var paragraph = new Mdom.Paragraph();
        MigraDocCriticalLogbookPdfRenderer.AddCellText(paragraph, text);

        var parts = paragraph.Elements.Cast<Mdom.DocumentObject>().ToList();
        Assert.Equal(elements, parts.Count);
        Assert.Equal(text, TextOf(paragraph.Elements));
        Assert.All(parts.OfType<Mdom.FormattedText>(), box => Assert.Equal(EmbeddedFontResolver.BoxFamilyName, box.FontName));
        Assert.Equal(text.Count(c => c is '☒' or '☐'), parts.OfType<Mdom.FormattedText>().Count());
    }

    [Fact]
    public void EmptyLogbook_SaysThereIsNoRecord()
    {
        var table = Body(MigraDocCriticalLogbookPdfRenderer.Build(Document(CriticalLogbookForm.Clinical, rows: []))).OfType<Mdom.Tables.Table>().Last();

        Assert.Equal(3, table.Rows.Count);
        Assert.Equal("Không có hồ sơ trong kỳ này.", CellText(table.Rows[2].Cells[0]));
        Assert.Equal(9, table.Rows[2].Cells[0].MergeRight);
    }

    [Fact]
    public void ColumnsNotMatchingTheForm_AreRejected()
    {
        var wrong = Document(CriticalLogbookForm.Laboratory) with { Columns = CriticalLogbookService.ColumnsOf(CriticalLogbookForm.Microbiology) };

        Assert.Throws<ArgumentException>(() => MigraDocCriticalLogbookPdfRenderer.Build(wrong));
    }

    [Theory]
    [InlineData(CriticalLogbookForm.Laboratory)]
    [InlineData(CriticalLogbookForm.Clinical)]
    [InlineData(CriticalLogbookForm.Microbiology)]
    public void FullMonth_WithLongText_StaysInsideTheLandscapePages(CriticalLogbookForm form)
    {
        var longText = string.Join(' ', Enumerable.Repeat("Nội dung dài (giả lập)", 12));
        var rows = Enumerable.Range(1, 60)
            .Select(i => Row(form, i, cancelled: i % 7 == 0, note: i % 5 == 0 ? longText : null, text: i % 4 == 0 ? longText : null))
            .ToList();
        var document = MigraDocCriticalLogbookPdfRenderer.Build(Document(form, rows, Logo()));

        _ = new MigraDocCriticalLogbookPdfRenderer(); // cài bộ phông nhúng
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var formatted = renderer.FormattedDocument;
        Assert.NotNull(formatted);
        Assert.True(formatted.PageCount >= 3);
        var bottom = Mdom.Unit.FromMillimeter(210 - 14).Point; // A4 ngang trừ lề dưới
        var right = Mdom.Unit.FromMillimeter(297 - 12).Point;
        for (var page = 1; page <= formatted.PageCount; page++)
        {
            var infos = renderer.GetRenderInfoFromPage(page);
            Assert.NotNull(infos);
            Assert.NotEmpty(infos);
            foreach (var info in infos)
            {
                var area = info.LayoutInfo.ContentArea;
                Assert.True(area.Y.Point + area.Height.Point <= bottom + 0.5, $"Trang {page}: {info.DocumentObject.GetType().Name} tràn lề dưới");
                Assert.True(area.X.Point + area.Width.Point <= right + 0.5, $"Trang {page}: {info.DocumentObject.GetType().Name} tràn lề phải");
            }
        }
    }

    [Fact]
    public void Render_ProducesAPdf_WithOrWithoutLogo()
    {
        var renderer = new MigraDocCriticalLogbookPdfRenderer();

        foreach (var logo in new[] { null, Logo() })
        {
            var bytes = renderer.Render(Document(CriticalLogbookForm.Microbiology, logo: logo));
            Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
        }
    }

    private static LogoImage Logo() => LogoImage.Read(Convert.FromBase64String(TestLogos.Png64))!;
}
