using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Consent.Application.NursingReports;

namespace Consent.Infrastructure.Exports;

/// <summary>
/// Xuất bảng tổng hợp báo cáo điều dưỡng hằng ngày ra Excel (.xlsx) theo bố cục bản giấy (quyết định 31): đầu trang, mục A–F,
/// bảng kẻ ô. Ghi bằng thư viện chuẩn của .NET như file xuất dữ liệu phiếu; chữ ghi dạng văn bản nên Excel không coi là công
/// thức, số ghi dạng số để Phòng Điều dưỡng tính tiếp được.
/// </summary>
internal sealed class XlsxNursingSummaryWriter : INursingSummaryWorkbookWriter
{
    public const string SheetName = "Tổng hợp";

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>Số cột của trang: tên khoa và 11 cột số của bảng A.</summary>
    private const int LastColumn = 11;

    private static readonly XmlWriterSettings XmlSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        CloseOutput = false,
    };

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Kiểu ô trong styles.xml.</summary>
    internal enum Style
    {
        Normal = 0,
        Bold = 1,
        Title = 2,
        Center = 3,
        Header = 4,
        Text = 5,
        Number = 6,
        TotalText = 7,
        TotalNumber = 8,
        Heading = 9,

        /// <summary>Số ở mục B–D: canh trái như bản giấy (cột "SỐ LIỆU" rộng).</summary>
        NumberLeft = 10,
    }

    internal sealed record Cell(int Column, object? Value, Style Style);

    internal sealed record Row(IReadOnlyList<Cell> Cells, double? Height = null);

    public byte[] Write(NursingSummaryDocument document)
    {
        var (rows, merges) = Layout(document);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WritePart(zip, "[Content_Types].xml", WriteContentTypes);
            WritePart(zip, "_rels/.rels", w => WriteRelationships(w,
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")));
            WritePart(zip, "xl/_rels/workbook.xml.rels", w => WriteRelationships(w,
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
                ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml")));
            WritePart(zip, "xl/workbook.xml", WriteWorkbook);
            WritePart(zip, "xl/styles.xml", WriteStyles);
            WritePart(zip, "xl/worksheets/sheet1.xml", w => WriteSheet(w, rows, merges));
        }

        return output.ToArray();
    }

    /// <summary>Các dòng của trang tính và vùng gộp ô; tách riêng để test xem được nội dung.</summary>
    internal static (IReadOnlyList<Row> Rows, IReadOnlyList<string> Merges) Layout(NursingSummaryDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var summary = document.Summary;
        var rows = new List<Row>();
        var merges = new List<string>();

        void AddRow(params Cell[] cells) => rows.Add(new Row(cells));

        void AddMergedRow(object? value, Style style, double? height = null)
        {
            rows.Add(new Row([new Cell(0, value, style)], height));
            merges.Add(Range(0, LastColumn, rows.Count));
        }

        AddRow(new Cell(0, document.HospitalName.ToUpper(Vietnamese), Style.Bold));
        AddRow(new Cell(0, NursingReportForm.SummaryUnit, Style.Bold));
        AddRow();
        AddMergedRow(NursingReportForm.SummaryTitle, Style.Title, 22);
        AddMergedRow(
            string.Create(CultureInfo.InvariantCulture, $"Ngày {summary.ReportDate.Day:00} tháng {summary.ReportDate.Month:00} năm {summary.ReportDate.Year}"),
            Style.Center);

        AddRow();
        AddRow(new Cell(0, "A. Số liệu người bệnh nội trú", Style.Heading));
        var header = new List<Cell> { new(0, "KHOA PHÒNG", Style.Header) };
        header.AddRange(summary.Columns.Select((c, i) => new Cell(i + 1, c.Header, Style.Header)));
        rows.Add(new Row(header, 66));
        foreach (var department in summary.Rows)
        {
            var cells = new List<Cell> { new(0, department.DepartmentName, Style.Text) };
            cells.AddRange(summary.Columns.Select((c, i) => new Cell(i + 1, department.Cells[c.Key], Style.Number)));
            rows.Add(new Row(cells));
        }

        var total = new List<Cell> { new(0, "Tổng", Style.TotalText) };
        total.AddRange(summary.Columns.Select((c, i) => new Cell(i + 1, summary.Totals[c.Key], Style.TotalNumber)));
        rows.Add(new Row(total));

        void AddTable(string heading, string firstHeader, string secondHeader, IEnumerable<(string Label, object? Value)> lines, bool numbers)
        {
            AddRow();
            AddRow(new Cell(0, heading, Style.Heading));
            rows.Add(new Row([new Cell(0, firstHeader, Style.Header), .. Spanned(1, secondHeader, Style.Header)]));
            merges.Add(Range(1, LastColumn, rows.Count));
            foreach (var (label, value) in lines)
            {
                var text = value as string;
                rows.Add(new Row(
                    [new Cell(0, label, Style.Text), .. Spanned(1, value, numbers ? Style.NumberLeft : Style.Text)],
                    text is null ? null : EstimateHeight(text)));
                merges.Add(Range(1, LastColumn, rows.Count));
            }
        }

        AddTable("B. Số liệu khoa khám bệnh", "NỘI DUNG", "SỐ LIỆU", summary.Outpatient.Select(l => (l.Label, (object?)l.Value)), numbers: true);
        AddTable("C. Số liệu PTTT", "NỘI DUNG", "SỐ LIỆU", summary.Procedures.Select(l => (l.Label, (object?)l.Value)), numbers: true);
        AddTable("D. Số liệu CĐHA", "NỘI DUNG", "SỐ LIỆU", summary.Imaging.Select(l => (l.Label, (object?)l.Value)), numbers: true);
        AddTable("E. Tình hình trong ngày", "NỘI DUNG", "PHẦN BÁO CÁO", summary.Situation.Select(c => (c.Label, (object?)(c.Text ?? string.Empty))), numbers: false);
        AddTable(
            "F. Báo cáo cụ thể sự cố (nếu có)",
            "KHOA",
            "NỘI DUNG BÁO CÁO SỰ CỐ",
            summary.Incidents.Count == 0
                ? [(string.Empty, "Không có.")]
                : summary.Incidents.Select(i => (i.DepartmentName, (object?)i.Text)),
            numbers: false);

        AddRow();
        AddRow(new Cell(0, document.PrintedLine, Style.Normal));
        return (rows, merges);
    }

    /// <summary>Ô đầu vùng gộp có giá trị, các ô còn lại trống nhưng cùng kiểu (để kẻ đủ viền).</summary>
    private static IEnumerable<Cell> Spanned(int from, object? value, Style style) =>
        Enumerable.Range(from, LastColumn - from + 1).Select(c => new Cell(c, c == from ? value : null, style));

    /// <summary>Ô gộp không tự giãn chiều cao: ước lượng theo số dòng (khoảng 110 ký tự mỗi dòng trên 11 cột gộp).</summary>
    private static double? EstimateHeight(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / 110d)));
        return lines <= 1 ? null : Math.Min(409, 13.5 * lines);
    }

    private static string Range(int fromColumn, int toColumn, int row) => string.Create(
        CultureInfo.InvariantCulture,
        $"{XlsxTabularFileWriter.ColumnName(fromColumn)}{row}:{XlsxTabularFileWriter.ColumnName(toColumn)}{row}");

    private static void WriteSheet(XmlWriter w, IReadOnlyList<Row> rows, IReadOnlyList<string> merges)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("worksheet", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        // In vừa một trang ngang (khổ A4 dọc), chiều cao tùy nội dung.
        w.WriteStartElement("sheetPr");
        w.WriteStartElement("pageSetUpPr");
        w.WriteAttributeString("fitToPage", "1");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cols");
        WriteColumn(w, 1, 1, 36);
        WriteColumn(w, 2, LastColumn + 1, 10.5);
        w.WriteEndElement();

        w.WriteStartElement("sheetData");
        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 1;
            w.WriteStartElement("row");
            w.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
            if (rows[i].Height is { } height)
            {
                w.WriteAttributeString("ht", height.ToString("0.#", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customHeight", "1");
            }

            foreach (var cell in rows[i].Cells)
            {
                WriteCell(w, string.Create(CultureInfo.InvariantCulture, $"{XlsxTabularFileWriter.ColumnName(cell.Column)}{rowNumber}"), cell);
            }

            w.WriteEndElement();
        }

        w.WriteEndElement(); // sheetData

        if (merges.Count > 0)
        {
            w.WriteStartElement("mergeCells");
            w.WriteAttributeString("count", merges.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var range in merges)
            {
                w.WriteStartElement("mergeCell");
                w.WriteAttributeString("ref", range);
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteStartElement("pageMargins");
        foreach (var (name, value) in new[] { ("left", "0.4"), ("right", "0.4"), ("top", "0.5"), ("bottom", "0.5"), ("header", "0.3"), ("footer", "0.3") })
        {
            w.WriteAttributeString(name, value);
        }

        w.WriteEndElement();
        w.WriteStartElement("pageSetup");
        w.WriteAttributeString("paperSize", "9");
        w.WriteAttributeString("orientation", "portrait");
        w.WriteAttributeString("fitToWidth", "1");
        w.WriteAttributeString("fitToHeight", "0");
        w.WriteEndElement();

        w.WriteEndElement(); // worksheet
        w.WriteEndDocument();
    }

    private static void WriteColumn(XmlWriter w, int min, int max, double width)
    {
        w.WriteStartElement("col");
        w.WriteAttributeString("min", min.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("max", max.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("width", width.ToString("0.##", CultureInfo.InvariantCulture));
        w.WriteAttributeString("customWidth", "1");
        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, string reference, Cell cell)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        if (cell.Style != Style.Normal)
        {
            w.WriteAttributeString("s", ((int)cell.Style).ToString(CultureInfo.InvariantCulture));
        }

        switch (cell.Value)
        {
            case int number:
                w.WriteStartElement("v");
                w.WriteString(number.ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
                break;
            case string { Length: > 0 } text:
                w.WriteAttributeString("t", "inlineStr");
                w.WriteStartElement("is");
                w.WriteStartElement("t");
                w.WriteAttributeString("xml", "space", null, "preserve");
                w.WriteString(XlsxTabularFileWriter.CleanText(text));
                w.WriteEndElement();
                w.WriteEndElement();
                break;
        }

        w.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter w)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("workbook", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);
        w.WriteStartElement("sheets");
        w.WriteStartElement("sheet");
        w.WriteAttributeString("name", SheetName);
        w.WriteAttributeString("sheetId", "1");
        w.WriteAttributeString("id", RelNs, "rId1");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndDocument();
    }

    private static void WriteContentTypes(XmlWriter w)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("Types", ns);
        foreach (var (extension, type) in new[]
                 {
                     ("rels", "application/vnd.openxmlformats-package.relationships+xml"),
                     ("xml", "application/xml"),
                 })
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", extension);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        foreach (var (part, type) in new[]
                 {
                     ("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
                     ("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
                     ("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"),
                 })
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndDocument();
    }

    private static void WriteRelationships(XmlWriter w, params (string Id, string Type, string Target)[] relationships)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("Relationships", PackageRelNs);
        foreach (var (id, type, target) in relationships)
        {
            w.WriteStartElement("Relationship", PackageRelNs);
            w.WriteAttributeString("Id", id);
            w.WriteAttributeString("Type", type);
            w.WriteAttributeString("Target", target);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndDocument();
    }

    /// <summary>
    /// Phông 0 thường, 1 đậm, 2 đậm cỡ 14 (tiêu đề), 3 đậm cỡ 11 (mục); viền 1 kẻ mảnh bốn cạnh; nền 2 xám nhạt (dòng tiêu đề
    /// bảng). Thứ tự kiểu ô theo <see cref="Style"/>.
    /// </summary>
    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("styleSheet", MainNs);

        w.WriteStartElement("fonts");
        w.WriteAttributeString("count", "4");
        WriteFont(w, 10, bold: false);
        WriteFont(w, 10, bold: true);
        WriteFont(w, 14, bold: true);
        WriteFont(w, 11, bold: true);
        w.WriteEndElement();

        w.WriteStartElement("fills");
        w.WriteAttributeString("count", "3");
        WriteFill(w, "none", null);
        WriteFill(w, "gray125", null);
        WriteFill(w, "solid", "FFF2F2F2");
        w.WriteEndElement();

        w.WriteStartElement("borders");
        w.WriteAttributeString("count", "2");
        WriteBorder(w, thin: false);
        WriteBorder(w, thin: true);
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs");
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("xf");
        w.WriteAttributeString("numFmtId", "0");
        w.WriteAttributeString("fontId", "0");
        w.WriteAttributeString("fillId", "0");
        w.WriteAttributeString("borderId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cellXfs");
        w.WriteAttributeString("count", "11");
        WriteXf(w, font: 0, fill: 0, border: 0, horizontal: null, vertical: null, wrap: false); // Normal
        WriteXf(w, font: 1, fill: 0, border: 0, horizontal: null, vertical: null, wrap: false); // Bold
        WriteXf(w, font: 2, fill: 0, border: 0, horizontal: "center", vertical: "center", wrap: false); // Title
        WriteXf(w, font: 0, fill: 0, border: 0, horizontal: "center", vertical: null, wrap: false); // Center
        WriteXf(w, font: 1, fill: 2, border: 1, horizontal: "center", vertical: "center", wrap: true); // Header
        WriteXf(w, font: 0, fill: 0, border: 1, horizontal: "left", vertical: "top", wrap: true); // Text
        WriteXf(w, font: 0, fill: 0, border: 1, horizontal: "right", vertical: "center", wrap: false); // Number
        WriteXf(w, font: 1, fill: 0, border: 1, horizontal: "left", vertical: "center", wrap: false); // TotalText
        WriteXf(w, font: 1, fill: 0, border: 1, horizontal: "right", vertical: "center", wrap: false); // TotalNumber
        WriteXf(w, font: 3, fill: 0, border: 0, horizontal: null, vertical: null, wrap: false); // Heading
        WriteXf(w, font: 0, fill: 0, border: 1, horizontal: "left", vertical: "center", wrap: false); // NumberLeft
        w.WriteEndElement();

        w.WriteStartElement("cellStyles");
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle");
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();
        w.WriteEndDocument();

        static void WriteFont(XmlWriter w, double size, bool bold)
        {
            w.WriteStartElement("font");
            if (bold)
            {
                w.WriteStartElement("b");
                w.WriteEndElement();
            }

            w.WriteStartElement("sz");
            w.WriteAttributeString("val", size.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteStartElement("name");
            w.WriteAttributeString("val", "Arial");
            w.WriteEndElement();
            w.WriteStartElement("family");
            w.WriteAttributeString("val", "2");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteFill(XmlWriter w, string pattern, string? rgb)
        {
            w.WriteStartElement("fill");
            w.WriteStartElement("patternFill");
            w.WriteAttributeString("patternType", pattern);
            if (rgb is not null)
            {
                w.WriteStartElement("fgColor");
                w.WriteAttributeString("rgb", rgb);
                w.WriteEndElement();
                w.WriteStartElement("bgColor");
                w.WriteAttributeString("indexed", "64");
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WriteBorder(XmlWriter w, bool thin)
        {
            w.WriteStartElement("border");
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" })
            {
                w.WriteStartElement(side);
                if (thin && side != "diagonal")
                {
                    w.WriteAttributeString("style", "thin");
                    w.WriteStartElement("color");
                    w.WriteAttributeString("indexed", "64");
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        static void WriteXf(XmlWriter w, int font, int fill, int border, string? horizontal, string? vertical, bool wrap)
        {
            w.WriteStartElement("xf");
            w.WriteAttributeString("numFmtId", "0");
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", border.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("xfId", "0");
            if (font != 0)
            {
                w.WriteAttributeString("applyFont", "1");
            }

            if (fill != 0)
            {
                w.WriteAttributeString("applyFill", "1");
            }

            if (border != 0)
            {
                w.WriteAttributeString("applyBorder", "1");
            }

            if (horizontal is not null || vertical is not null || wrap)
            {
                w.WriteAttributeString("applyAlignment", "1");
                w.WriteStartElement("alignment");
                if (horizontal is not null)
                {
                    w.WriteAttributeString("horizontal", horizontal);
                }

                if (vertical is not null)
                {
                    w.WriteAttributeString("vertical", vertical);
                }

                if (wrap)
                {
                    w.WriteAttributeString("wrapText", "1");
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
        }
    }

    private static void WritePart(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, XmlSettings);
        write(writer);
    }
}
