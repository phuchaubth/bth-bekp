using System.Globalization;
using Consent.Application.NursingReports;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Consent.Infrastructure.Pdf;

/// <summary>
/// In PDF "Báo cáo công tác điều dưỡng" của khoa và "Tổng kết báo cáo Phòng Điều dưỡng hằng ngày" theo bố cục bản giấy bệnh
/// viện gửi (quyết định 31): bảng kẻ ô, mục in đậm, phần tick có ô ☒/☐. Dùng chung phông nhúng với PDF phiếu đồng thuận.
/// </summary>
internal sealed class MigraDocNursingReportPdfRenderer : INursingReportPdfRenderer
{
    /// <summary>Bề rộng vùng in (A4 trừ lề trái, phải 15 mm).</summary>
    internal const double ContentWidthMm = 180;

    private const double TableFontSize = 9;
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock FontGate = new();

    public MigraDocNursingReportPdfRenderer()
    {
        lock (FontGate)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }
        }
    }

    public byte[] RenderReport(NursingReportPdfDocument document) => Render(BuildReport(document));

    public byte[] RenderSummary(NursingSummaryDocument document) => Render(BuildSummary(document));

    /// <summary>Bố cục báo cáo của khoa (MigraDoc) trước khi in; test xem được từng phần.</summary>
    internal static Document BuildReport(NursingReportPdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var pdf = NewDocument($"{NursingReportForm.Title} – {document.DepartmentName} – {document.ReportDate:dd/MM/yyyy}");
        var section = AddSection(pdf, document.StatusLine);

        var head = AddHeadTable(section);
        var left = head.Rows[0].Cells[0].AddParagraph();
        left.AddFormattedText(document.HospitalName.ToUpper(Vietnamese), TextFormat.Bold);
        left.AddLineBreak();
        left.AddFormattedText(document.DepartmentName, TextFormat.Bold);
        var right = head.Rows[0].Cells[1].AddParagraph();
        right.Format.Alignment = ParagraphAlignment.Right;
        right.AddFormattedText(NursingReportForm.Category, TextFormat.Bold);

        AddTitle(section, NursingReportForm.Title, document.ReportDate, boldDate: true, dateSize: 8);
        AddHeading(section, "PHẦN SỐ LIỆU");
        AddFigures(section, document.Figures);
        AddHeading(section, "PHẦN TICK");
        AddChecks(section, document.Figures);
        AddHeading(section, "PHẦN BÁO CÁO (NHẬP THÊM)");
        var notes = section.AddParagraph();
        notes.Format.Font.Size = TableFontSize;
        notes.AddText(NursingReportForm.NotesLabel + " ");
        AddMultiline(notes, document.Notes ?? string.Empty);
        return pdf;
    }

    /// <summary>Bố cục bảng tổng hợp (MigraDoc) trước khi in; test xem được từng phần.</summary>
    internal static Document BuildSummary(NursingSummaryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var summary = document.Summary;
        var pdf = NewDocument($"{NursingReportForm.SummaryTitle} – {summary.ReportDate:dd/MM/yyyy}");
        var section = AddSection(pdf, document.PrintedLine);

        var head = AddHeadTable(section);
        var left = head.Rows[0].Cells[0].AddParagraph();
        left.AddFormattedText(document.HospitalName.ToUpper(Vietnamese), TextFormat.Bold);
        left.AddLineBreak();
        left.AddFormattedText(NursingReportForm.SummaryUnit, TextFormat.Bold);

        AddTitle(section, NursingReportForm.SummaryTitle, summary.ReportDate, boldDate: false, dateSize: 10);

        AddHeading(section, "A. Số liệu người bệnh nội trú");
        AddInpatientTable(section, summary);

        // Bảng B–E ngắn, số dòng cố định: in liền trên một trang. Bảng F dài tùy phần khoa nhập thêm nên được tách trang.
        AddHeading(section, "B. Số liệu khoa khám bệnh");
        AddLineTable(section, "NỘI DUNG", "SỐ LIỆU", summary.Outpatient.Select(l => (l.Label, Number(l.Value))), keepTogether: true);
        AddReporters(section, summary.Reporters.Outpatient);

        AddHeading(section, "C. Số liệu PTTT");
        AddLineTable(section, "NỘI DUNG", "SỐ LIỆU", summary.Procedures.Select(l => (l.Label, Number(l.Value))), keepTogether: true);
        AddReporters(section, summary.Reporters.Procedures);

        AddHeading(section, "D. Số liệu CĐHA");
        AddLineTable(section, "NỘI DUNG", "SỐ LIỆU", summary.Imaging.Select(l => (l.Label, Number(l.Value))), keepTogether: true);
        AddReporters(section, summary.Reporters.Imaging);

        AddHeading(section, "E. Tình hình trong ngày");
        AddLineTable(section, "NỘI DUNG", "PHẦN BÁO CÁO", summary.Situation.Select(c => (c.Label, c.Text ?? string.Empty)), keepTogether: true);
        AddReporters(section, summary.Reporters.Situation);

        AddHeading(section, "F. Báo cáo cụ thể sự cố (nếu có)");
        AddLineTable(
            section,
            "KHOA",
            "NỘI DUNG BÁO CÁO SỰ CỐ",
            summary.Incidents.Count == 0
                ? [(string.Empty, "Không có.")]
                : summary.Incidents.Select(i => (i.DepartmentCode, i.Text)),
            centerFirstColumn: true);
        AddReporters(section, summary.Reporters.Incidents);

        AddSignature(section);
        return pdf;
    }

    /// <summary>Dòng "Người báo cáo: …" dưới mỗi mục B–F (quyết định 44); mục chưa có số liệu ghi "chưa có".</summary>
    private static void AddReporters(Section section, string? reporters)
    {
        var line = section.AddParagraph();
        line.Format.Font.Size = TableFontSize;
        line.Format.SpaceBefore = Unit.FromMillimeter(1);
        line.AddFormattedText($"{NursingReportForm.ReporterHeader}: ", TextFormat.Bold);
        line.AddFormattedText(reporters ?? "chưa có", TextFormat.Italic);
    }

    /// <summary>Chỗ Phòng Điều dưỡng ký cuối bảng tổng hợp (quyết định 44): nửa phải trang, chừa chỗ ký, không tách trang.</summary>
    private static void AddSignature(Section section)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(ContentWidthMm / 2));
        table.AddColumn(Unit.FromMillimeter(ContentWidthMm / 2));
        var row = table.AddRow();
        row.TopPadding = Unit.FromMillimeter(6);
        var title = row.Cells[1].AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.Font.Size = 11;
        title.AddFormattedText(NursingReportForm.SignatureTitle, TextFormat.Bold);
        var hint = row.Cells[1].AddParagraph();
        hint.Format.Alignment = ParagraphAlignment.Center;
        hint.Format.Font.Size = TableFontSize;
        hint.Format.SpaceAfter = Unit.FromMillimeter(SignatureSpaceMm);
        hint.AddFormattedText(NursingReportForm.SignatureHint, TextFormat.Italic);
    }

    /// <summary>Khoảng trống để ký và ghi họ tên dưới chỗ ký của Phòng Điều dưỡng.</summary>
    internal const double SignatureSpaceMm = 22;

    private static byte[] Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    private static Document NewDocument(string title)
    {
        var pdf = new Document();
        pdf.Info.Title = title;
        var normal = pdf.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = 0;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;
        return pdf;
    }

    private static Section AddSection(Document pdf, string footerLine)
    {
        var section = pdf.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromMillimeter(12);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(15);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(15);
        section.PageSetup.RightMargin = Unit.FromMillimeter(15);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(7);

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7.5;
        footer.Format.Font.Color = Muted;
        footer.Format.TabStops.AddTabStop(Unit.FromMillimeter(ContentWidthMm), TabAlignment.Right);
        footer.AddText(footerLine);
        footer.AddTab();
        footer.AddText("Trang ");
        footer.AddPageField();
        footer.AddText("/");
        footer.AddNumPagesField();
        return section;
    }

    /// <summary>Hàng đầu báo cáo: tên đơn vị bên trái, ghi chú bên phải (cỡ 9, đậm như bản giấy).</summary>
    private static Table AddHeadTable(Section section)
    {
        var table = section.AddTable();
        table.Format.Font.Size = 9;
        table.AddColumn(Unit.FromMillimeter(120));
        table.AddColumn(Unit.FromMillimeter(ContentWidthMm - 120));
        table.AddRow().VerticalAlignment = VerticalAlignment.Top;
        return table;
    }

    private static void AddTitle(Section section, string title, DateOnly date, bool boldDate, double dateSize)
    {
        var heading = section.AddParagraph(title);
        heading.Format.Alignment = ParagraphAlignment.Center;
        heading.Format.SpaceBefore = Unit.FromMillimeter(5);
        heading.Format.Font.Size = 15;
        heading.Format.Font.Bold = true;

        var line = section.AddParagraph(string.Create(
            CultureInfo.InvariantCulture, $"Ngày {date.Day:00} tháng {date.Month:00} năm {date.Year}"));
        line.Format.Alignment = ParagraphAlignment.Center;
        line.Format.Font.Size = dateSize;
        line.Format.Font.Bold = boldDate;
        line.Format.SpaceAfter = Unit.FromMillimeter(4);
    }

    private static void AddHeading(Section section, string text)
    {
        var heading = section.AddParagraph(text);
        heading.Format.Font.Size = 11;
        heading.Format.Font.Bold = true;
        heading.Format.SpaceBefore = Unit.FromMillimeter(3);
        heading.Format.SpaceAfter = Unit.FromMillimeter(1);
        heading.Format.KeepWithNext = true;
    }

    private static Table AddGrid(Section section, params double[] widthsMm)
    {
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.Black;
        table.Format.Font.Size = TableFontSize;
        table.LeftPadding = Unit.FromMillimeter(1);
        table.RightPadding = Unit.FromMillimeter(1);
        table.TopPadding = Unit.FromMillimeter(0.6);
        table.BottomPadding = Unit.FromMillimeter(0.6);
        foreach (var width in widthsMm)
        {
            table.AddColumn(Unit.FromMillimeter(width));
        }

        return table;
    }

    /// <summary>Phần số liệu: bảng hai cột (dòng – số), dòng tiêu đề mục in đậm.</summary>
    private static void AddFigures(Section section, NursingReportFigures figures)
    {
        var table = AddGrid(section, 125, ContentWidthMm - 125);
        foreach (var block in NursingReportForm.Sections)
        {
            var title = table.AddRow();
            title.KeepWith = Math.Min(1, block.Items.Count);
            title.Cells[0].AddParagraph().AddFormattedText(block.Title, TextFormat.Bold);
            if (block.ValueKey is { } key)
            {
                title.Cells[1].AddParagraph(Number(figures.Value(key)));
            }
            else
            {
                title.Cells[0].MergeRight = 1;
            }

            foreach (var item in block.Items)
            {
                var row = table.AddRow();
                row.Cells[0].AddParagraph(item.Label);
                row.Cells[1].AddParagraph(Number(figures.Value(item.Key)));
            }
        }
    }

    /// <summary>Phần tick: mỗi dòng hai ô "Có sự cố/ Không ổn" và "Không có sự cố/ Ổn", ô đã chọn có dấu X.</summary>
    private static void AddChecks(Section section, NursingReportFigures figures)
    {
        foreach (var check in NursingReportForm.Checks)
        {
            var line = section.AddParagraph();
            line.Format.Font.Size = TableFontSize;
            line.Format.SpaceAfter = Unit.FromMillimeter(0.8);
            line.Format.TabStops.AddTabStop(Unit.FromMillimeter(68));
            line.Format.TabStops.AddTabStop(Unit.FromMillimeter(122));
            line.AddText(check.Label);
            line.AddTab();
            AddBox(line, figures.Check(check.Key) == NursingReportForm.CheckIssue);
            line.AddText(NursingReportForm.IssueLabel);
            line.AddTab();
            AddBox(line, figures.Check(check.Key) == NursingReportForm.CheckOk);
            line.AddText(NursingReportForm.OkLabel);
        }
    }

    private static void AddBox(Paragraph paragraph, bool selected)
    {
        var box = paragraph.AddFormattedText(selected ? MigraDocConsentPdfRenderer.Checked : MigraDocConsentPdfRenderer.Unchecked);
        box.FontName = EmbeddedFontResolver.BoxFamilyName;
        box.Size = 9;
        box.Bold = false;
        box.Color = Colors.Black;
    }

    /// <summary>Cột mã khoa (tên viết tắt) và cột người báo cáo của bảng A; các cột số chia đều phần còn lại (quyết định 44).</summary>
    internal const double CodeColumnMm = 16;

    internal const double ReporterColumnMm = 24;

    /// <summary>
    /// Bảng A: một dòng mỗi khoa (mã khoa), cột số theo bản giấy, cột người báo cáo cuối cùng (quyết định 44), dòng "Tổng" in
    /// đậm.
    /// </summary>
    private static void AddInpatientTable(Section section, NursingSummaryDto summary)
    {
        var numberColumns = summary.Columns.Count;
        var numberWidth = (ContentWidthMm - CodeColumnMm - ReporterColumnMm) / numberColumns;
        var widths = new List<double> { CodeColumnMm };
        widths.AddRange(Enumerable.Repeat(numberWidth, numberColumns));
        widths.Add(ReporterColumnMm);
        var table = AddGrid(section, [.. widths]);
        table.Format.Font.Size = 8;

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.VerticalAlignment = VerticalAlignment.Center;
        header.Format.Font.Bold = true;
        header.Format.Alignment = ParagraphAlignment.Center;
        header.Cells[0].AddParagraph("KHOA PHÒNG");
        for (var i = 0; i < numberColumns; i++)
        {
            var cell = header.Cells[i + 1].AddParagraph(summary.Columns[i].Header);
            cell.Format.Font.Size = 7.5;
        }

        header.Cells[numberColumns + 1].AddParagraph(NursingReportForm.ReporterHeader).Format.Font.Size = 7.5;

        foreach (var department in summary.Rows)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Cells[0].AddParagraph(department.DepartmentCode);
            for (var i = 0; i < numberColumns; i++)
            {
                var cell = row.Cells[i + 1].AddParagraph(Number(department.Cells[summary.Columns[i].Key]));
                cell.Format.Alignment = ParagraphAlignment.Right;
            }

            row.Cells[numberColumns + 1].AddParagraph(department.SubmittedBy ?? string.Empty).Format.Font.Size = 7.5;
        }

        var total = table.AddRow();
        total.Format.Font.Bold = true;
        total.Cells[0].AddParagraph("Tổng");
        for (var i = 0; i < numberColumns; i++)
        {
            var cell = total.Cells[i + 1].AddParagraph(Number(summary.Totals[summary.Columns[i].Key]));
            cell.Format.Alignment = ParagraphAlignment.Right;
        }
    }

    /// <summary>Bảng hai cột "nội dung – số liệu/báo cáo" của mục B–F, dòng tiêu đề in đậm, canh giữa.</summary>
    /// <summary>
    /// Bảng hai cột của mục B–F. <paramref name="keepTogether"/>: cả bảng in trên một trang (dòng tiêu đề giữ với mọi dòng sau).
    /// </summary>
    private static void AddLineTable(
        Section section,
        string firstHeader,
        string secondHeader,
        IEnumerable<(string Label, string Value)> lines,
        bool centerFirstColumn = false,
        bool keepTogether = false)
    {
        var table = AddGrid(section, 63, ContentWidthMm - 63);
        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Format.Alignment = ParagraphAlignment.Center;
        header.Cells[0].AddParagraph(firstHeader);
        header.Cells[1].AddParagraph(secondHeader);
        foreach (var (label, value) in lines)
        {
            var row = table.AddRow();
            if (centerFirstColumn)
            {
                row.Cells[0].VerticalAlignment = VerticalAlignment.Center;
            }

            row.Cells[0].AddParagraph(label);
            AddMultiline(row.Cells[1].AddParagraph(), value);
        }

        if (keepTogether)
        {
            header.KeepWith = table.Rows.Count - 1;
        }
    }

    /// <summary>Giữ xuống dòng của chữ người dùng nhập.</summary>
    private static void AddMultiline(Paragraph paragraph, string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.AddLineBreak();
            }

            if (lines[i].Length > 0)
            {
                paragraph.AddText(lines[i]);
            }
        }
    }

    private static string Number(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}
