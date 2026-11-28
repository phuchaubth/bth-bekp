using System.Globalization;
using Consent.Application.CriticalResults;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Consent.Infrastructure.Pdf;

/// <summary>
/// In PDF sổ kết quả nguy cấp (quyết định 60) theo bố cục sổ giấy bệnh viện gửi: QT-KQNC/ST01 (khoa cận lâm sàng, 10 cột),
/// QT-KQNC/ST02 (khoa điều trị, 10 cột), sổ Khoa Vi sinh QT-VS-21/ST02 (13 cột). A4 ngang; đầu trang có logo, tên đơn vị, mã số
/// sổ (lấy từ thông số cấu hình); tiêu đề cột lặp lại ở mỗi trang. Hồ sơ đã hủy in chữ xám gạch ngang, kèm dòng ghi chú lý do.
/// </summary>
internal sealed class MigraDocCriticalLogbookPdfRenderer : ICriticalLogbookPdfRenderer
{
    /// <summary>Bề rộng vùng in: A4 ngang (297 mm) trừ lề trái, phải 12 mm.</summary>
    internal const double ContentWidthMm = 273;

    private const double TableFontSize = 8;

    /// <summary>Ô ☒/☐ phông DejaVu to hơn chữ Tinos cùng cỡ: nhỏ hơn một chút để ô cao gần bằng chữ hoa trong ô sổ.</summary>
    private const double BoxFontSize = 7;
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);
    private static readonly Color Cancelled = new(0x80, 0x80, 0x80);
    private static readonly Color HeaderFill = new(0xEE, 0xF2, 0xF2);
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock FontGate = new();

    public MigraDocCriticalLogbookPdfRenderer()
    {
        lock (FontGate)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }
        }
    }

    /// <summary>Bề rộng các cột (mm) của từng mẫu sổ, theo tỉ lệ cột của sổ giấy; tổng bằng vùng in.</summary>
    internal static IReadOnlyList<double> ColumnsMm(CriticalLogbookForm form) => form switch
    {
        CriticalLogbookForm.Laboratory => [10, 45, 34, 22, 16, 26, 26, 30, 38, 26],
        CriticalLogbookForm.Clinical => [20, 38, 26, 24, 24, 28, 22, 24, 30, 37],
        _ => [18, 18, 30, 18, 28, 24, 11, 20, 20, 24, 22, 26, 14],
    };

    internal static (string Title, string Unit, string English) TitleOf(CriticalLogbookForm form) => form switch
    {
        CriticalLogbookForm.Laboratory => (
            "SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP", "KHOA CẬN LÂM SÀNG",
            "CRITICAL RESULT NOTIFICATION LOGBOOK – LABORATORY DEPARTMENT"),
        CriticalLogbookForm.Clinical => (
            "SỔ THEO DÕI THÔNG BÁO KẾT QUẢ NGUY CẤP", "KHOA ĐIỀU TRỊ",
            "CRITICAL RESULT NOTIFICATION LOGBOOK – CLINICAL DEPARTMENT"),
        _ => (
            "SỔ THÔNG BÁO KẾT QUẢ NGUY CẤP QUA ĐIỆN THOẠI", "KHOA VI SINH",
            "NOTIFICATION OF CRITICAL RESULTS BY TELEPHONE – MICROBIOLOGY DEPARTMENT"),
    };

    public byte[] Render(CriticalLogbookDocument document)
    {
        var renderer = new PdfDocumentRenderer { Document = Build(document) };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>Bố cục sổ (MigraDoc) trước khi in; test xem được từng phần.</summary>
    internal static Document Build(CriticalLogbookDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var widths = ColumnsMm(document.Form);
        if (widths.Count != document.Columns.Count)
        {
            throw new ArgumentException("Số cột của sổ không khớp mẫu.", nameof(document));
        }

        var (title, unit, english) = TitleOf(document.Form);
        var pdf = new Document();
        pdf.Info.Title = $"{title} – {document.DepartmentName} – {document.PeriodText}";
        var normal = pdf.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = 0;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;

        var section = pdf.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.TopMargin = Unit.FromMillimeter(10);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(14);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(12);
        section.PageSetup.RightMargin = Unit.FromMillimeter(12);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(6);
        AddFooter(section, document.PrintedText);
        AddHead(section, document);

        var heading = section.AddParagraph();
        heading.Format.Alignment = ParagraphAlignment.Center;
        heading.Format.SpaceBefore = Unit.FromMillimeter(4);
        heading.Format.SpaceAfter = Unit.FromMillimeter(3);
        heading.Format.Font.Bold = true;
        heading.Format.Font.Size = 15;
        heading.AddText(title);
        heading.AddLineBreak();
        heading.AddFormattedText(unit).Size = 13;
        heading.AddLineBreak();
        var englishText = heading.AddFormattedText(english);
        englishText.Size = 10;
        englishText.Bold = false;
        englishText.Italic = true;

        var info = section.AddParagraph();
        info.Format.SpaceAfter = Unit.FromMillimeter(2);
        info.Format.TabStops.AddTabStop(Unit.FromMillimeter(ContentWidthMm), TabAlignment.Right);
        info.AddFormattedText("Khoa/ Department: ", TextFormat.Bold);
        info.AddText(document.DepartmentName);
        info.AddTab();
        info.AddText(document.PeriodText);

        AddRows(section, document, widths);
        return pdf;
    }

    private static void AddFooter(Section section, string printedText)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.Format.TabStops.AddTabStop(Unit.FromMillimeter(ContentWidthMm / 2), TabAlignment.Center);
        footer.Format.TabStops.AddTabStop(Unit.FromMillimeter(ContentWidthMm), TabAlignment.Right);
        footer.AddText(printedText);
        footer.AddTab();
        footer.AddText("Lưu hành nội bộ/ For Internal Use Only");
        footer.AddTab();
        footer.AddText("Trang ");
        footer.AddPageField();
        footer.AddText("/");
        footer.AddNumPagesField();
    }

    /// <summary>Đầu trang: logo, cơ quan chủ quản và tên bệnh viện ở giữa, mã số sổ bên phải.</summary>
    private static void AddHead(Section section, CriticalLogbookDocument document)
    {
        const double LogoMm = 26;
        const double CodeMm = 60;
        var table = section.AddTable();
        table.Format.Font.Size = 10;
        table.AddColumn(Unit.FromMillimeter(LogoMm));
        table.AddColumn(Unit.FromMillimeter(ContentWidthMm - LogoMm - CodeMm));
        table.AddColumn(Unit.FromMillimeter(CodeMm));
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Top;
        if (document.Logo is { } logo && MigraDocConsentPdfRenderer.CanDecode(logo))
        {
            MigraDocConsentPdfRenderer.AddLogo(row.Cells[0], logo, LogoMm - 4, 15, alignWithTextOfSize: 10);
        }

        var unit = row.Cells[1].AddParagraph();
        unit.Format.Alignment = ParagraphAlignment.Center;
        unit.AddText(document.ParentAgency.ToUpper(Vietnamese));
        unit.AddLineBreak();
        unit.AddFormattedText(document.HospitalName.ToUpper(Vietnamese), TextFormat.Bold);

        var code = row.Cells[2].AddParagraph();
        code.Format.Alignment = ParagraphAlignment.Right;
        code.AddText("Mã số/ Code: ");
        code.AddFormattedText(document.Code, TextFormat.Bold);
    }

    private static void AddRows(Section section, CriticalLogbookDocument document, IReadOnlyList<double> widths)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Black;
        table.Format.Font.Size = TableFontSize;
        table.LeftPadding = Unit.FromMillimeter(1);
        table.RightPadding = Unit.FromMillimeter(1);
        table.TopPadding = Unit.FromMillimeter(0.8);
        table.BottomPadding = Unit.FromMillimeter(0.8);
        foreach (var width in widths)
        {
            table.AddColumn(Unit.FromMillimeter(width));
        }

        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = HeaderFill;
        head.VerticalAlignment = VerticalAlignment.Center;
        for (var i = 0; i < document.Columns.Count; i++)
        {
            var paragraph = head.Cells[i].AddParagraph();
            paragraph.Format.Alignment = ParagraphAlignment.Center;
            paragraph.AddFormattedText(document.Columns[i], TextFormat.Bold);
        }

        var numbers = table.AddRow();
        numbers.HeadingFormat = true;
        numbers.Format.Font.Size = 7;
        for (var i = 0; i < document.Columns.Count; i++)
        {
            var paragraph = numbers.Cells[i].AddParagraph((i + 1).ToString(CultureInfo.InvariantCulture));
            paragraph.Format.Alignment = ParagraphAlignment.Center;
        }

        if (document.Rows.Count == 0)
        {
            var empty = table.AddRow();
            empty.Cells[0].MergeRight = document.Columns.Count - 1;
            var paragraph = empty.Cells[0].AddParagraph("Không có hồ sơ trong kỳ này.");
            paragraph.Format.Alignment = ParagraphAlignment.Center;
            paragraph.Format.Font.Color = Muted;
            return;
        }

        foreach (var data in document.Rows)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Top;
            for (var i = 0; i < data.Cells.Count && i < document.Columns.Count; i++)
            {
                var text = data.Cancelled ? MigraDocQcLogPdfRenderer.Struck(data.Cells[i]) : data.Cells[i];
                var paragraph = row.Cells[i].AddParagraph();
                AddCellText(paragraph, text);
                if (data.Cancelled)
                {
                    paragraph.Format.Font.Color = Cancelled;
                }
            }

            if (data.Note is { Length: > 0 } note)
            {
                var noteRow = table.AddRow();
                noteRow.KeepWith = 0;
                noteRow.Cells[0].MergeRight = document.Columns.Count - 1;
                var paragraph = noteRow.Cells[0].AddParagraph();
                paragraph.Format.Font.Size = 7.5;
                paragraph.Format.Font.Color = Muted;
                paragraph.AddFormattedText("Ghi chú: ", TextFormat.Bold);
                paragraph.AddFormattedText(note, TextFormat.Italic);
                row.KeepWith = 1;
            }
        }
    }

    /// <summary>
    /// Chữ một ô sổ. Ô đánh dấu ☒/☐ (cột xác nhận lại thông tin, cột khẩn) in bằng phông DejaVu Sans nhúng sẵn: Tinos không có
    /// hai ký tự này nên in ra ô trống dù đã đánh dấu. Màu theo dòng (dòng đã hủy màu xám); dấu gạch của dòng đã hủy giữ ở
    /// phông Tinos.
    /// </summary>
    internal static void AddCellText(Paragraph paragraph, string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != MigraDocConsentPdfRenderer.Checked[0] && text[i] != MigraDocConsentPdfRenderer.Unchecked[0])
            {
                continue;
            }

            if (i > start)
            {
                paragraph.AddText(text[start..i]);
            }

            var box = paragraph.AddFormattedText(text[i..(i + 1)]);
            box.FontName = EmbeddedFontResolver.BoxFamilyName;
            box.Size = BoxFontSize;
            box.Bold = false;
            box.Italic = false;
            start = i + 1;
        }

        if (start < text.Length)
        {
            paragraph.AddText(text[start..]);
        }
    }
}
