using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using Consent.Application.Abstractions;
using Consent.Domain.Exports;

namespace Consent.Infrastructure.Exports;

/// <summary>
/// Ghi file Excel (.xlsx, Office Open XML) bằng thư viện chuẩn của .NET, không cần thư viện ngoài. Các dòng được ghi lần
/// lượt vào file nén nên bộ nhớ dùng ít kể cả khi xuất hàng chục nghìn phiếu.
/// Trang 1: dữ liệu (dòng tiêu đề in đậm, cố định, có bộ lọc; ngày giờ là ô ngày giờ của Excel). Trang 2: thông tin lần
/// xuất. Chuỗi được ghi dạng văn bản (inline string) nên Excel không bao giờ coi là công thức.
/// </summary>
internal sealed class XlsxTabularFileWriter : ITabularFileWriter
{
    public const string InfoSheetName = "Thông tin xuất";

    /// <summary>Excel giới hạn 32.767 ký tự mỗi ô.</summary>
    private const int MaxCellLength = 32_767;

    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string WorksheetType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";

    // Chỉ số kiểu ô trong styles.xml.
    private const int HeaderStyle = 1;
    private const int DateStyle = 2;
    private const int DateTimeStyle = 3;
    private const int LabelStyle = 4;

    private static readonly XmlWriterSettings XmlSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        CloseOutput = false,
    };

    /// <summary>Ngày 0 của hệ ngày 1900 trong Excel (đã tính lỗi năm nhuận 1900 của Excel cho các ngày từ 01/03/1900).</summary>
    private static readonly DateTime ExcelEpoch = new(1899, 12, 30, 0, 0, 0, DateTimeKind.Unspecified);

    private static readonly int ExcelEpochDayNumber = DateOnly.FromDateTime(ExcelEpoch).DayNumber;

    public ExportFormat Format => ExportFormat.Xlsx;

    public string ContentType => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public string FileExtension => "xlsx";

    public async Task<int> WriteAsync(TabularData data, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(output);
        var sheetName = SheetName(data.SheetName);

        int rowCount;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8))
        {
            WritePart(zip, "[Content_Types].xml", WriteContentTypes);
            WritePart(zip, "_rels/.rels", w => WriteRelationships(w,
                ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml")));
            WritePart(zip, "xl/_rels/workbook.xml.rels", w => WriteRelationships(w,
                ("rId1", WorksheetType, "worksheets/sheet1.xml"),
                ("rId2", WorksheetType, "worksheets/sheet2.xml"),
                ("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml")));
            WritePart(zip, "xl/styles.xml", WriteStyles);

            var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
            using (var stream = entry.Open())
            using (var writer = XmlWriter.Create(stream, XmlSettings))
            {
                rowCount = await WriteDataSheetAsync(writer, data, cancellationToken).ConfigureAwait(false);
            }

            WritePart(zip, "xl/worksheets/sheet2.xml", w => WriteInfoSheet(w, data.Properties));
            WritePart(zip, "xl/workbook.xml", w => WriteWorkbook(w, sheetName, data.Columns.Count, rowCount));
        }

        return rowCount;
    }

    private static async Task<int> WriteDataSheetAsync(XmlWriter w, TabularData data, CancellationToken cancellationToken)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("worksheet", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        // Cố định dòng tiêu đề khi cuộn.
        w.WriteStartElement("sheetViews");
        w.WriteStartElement("sheetView");
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteStartElement("pane");
        w.WriteAttributeString("ySplit", "1");
        w.WriteAttributeString("topLeftCell", "A2");
        w.WriteAttributeString("activePane", "bottomLeft");
        w.WriteAttributeString("state", "frozen");
        w.WriteEndElement();
        w.WriteStartElement("selection");
        w.WriteAttributeString("pane", "bottomLeft");
        w.WriteAttributeString("activeCell", "A2");
        w.WriteAttributeString("sqref", "A2");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();

        WriteColumnWidths(w, data.Columns.Select(c => c.Width).ToList());

        w.WriteStartElement("sheetData");
        w.WriteStartElement("row");
        w.WriteAttributeString("r", "1");
        for (var c = 0; c < data.Columns.Count; c++)
        {
            WriteText(w, CellReference(c, 1), data.Columns[c].Header, HeaderStyle);
        }

        w.WriteEndElement();

        var rowNumber = 1;
        await foreach (var row in data.Rows.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            rowNumber++;
            w.WriteStartElement("row");
            w.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
            for (var c = 0; c < row.Count && c < data.Columns.Count; c++)
            {
                WriteCell(w, CellReference(c, rowNumber), row[c]);
            }

            w.WriteEndElement();
        }

        w.WriteEndElement(); // sheetData

        w.WriteStartElement("autoFilter");
        w.WriteAttributeString("ref", FilterRange(data.Columns.Count, rowNumber));
        w.WriteEndElement();

        WritePageMargins(w);
        w.WriteEndElement(); // worksheet
        w.WriteEndDocument();
        return rowNumber - 1;
    }

    private static void WriteInfoSheet(XmlWriter w, IReadOnlyList<KeyValuePair<string, string>> properties)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("worksheet", MainNs);
        WriteColumnWidths(w, [24, 90]);
        w.WriteStartElement("sheetData");
        for (var i = 0; i < properties.Count; i++)
        {
            var rowNumber = i + 1;
            w.WriteStartElement("row");
            w.WriteAttributeString("r", rowNumber.ToString(CultureInfo.InvariantCulture));
            WriteText(w, CellReference(0, rowNumber), properties[i].Key, LabelStyle);
            WriteText(w, CellReference(1, rowNumber), properties[i].Value, 0);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        WritePageMargins(w);
        w.WriteEndElement();
        w.WriteEndDocument();
    }

    private static void WriteWorkbook(XmlWriter w, string sheetName, int columnCount, int rowCount)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("workbook", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);
        w.WriteStartElement("bookViews");
        w.WriteStartElement("workbookView");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("sheets");
        WriteSheet(w, sheetName, 1);
        WriteSheet(w, InfoSheetName, 2);
        w.WriteEndElement();
        // Vùng lọc của trang dữ liệu (Excel tự tạo tên này khi bật bộ lọc).
        w.WriteStartElement("definedNames");
        w.WriteStartElement("definedName");
        w.WriteAttributeString("name", "_xlnm._FilterDatabase");
        w.WriteAttributeString("localSheetId", "0");
        w.WriteAttributeString("hidden", "1");
        w.WriteString($"'{sheetName.Replace("'", "''", StringComparison.Ordinal)}'!{AbsoluteRange(columnCount, rowCount + 1)}");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndDocument();
    }

    private static void WriteSheet(XmlWriter w, string name, int id)
    {
        w.WriteStartElement("sheet");
        w.WriteAttributeString("name", name);
        w.WriteAttributeString("sheetId", id.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("id", RelNs, $"rId{id.ToString(CultureInfo.InvariantCulture)}");
        w.WriteEndElement();
    }

    private static void WriteContentTypes(XmlWriter w)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("Types", ns);
        WriteDefault(w, ns, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        WriteDefault(w, ns, "xml", "application/xml");
        WriteOverride(w, ns, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        WriteOverride(w, ns, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        WriteOverride(w, ns, "/xl/worksheets/sheet2.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        WriteOverride(w, ns, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        w.WriteEndElement();
        w.WriteEndDocument();

        static void WriteDefault(XmlWriter w, string ns, string extension, string contentType)
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", extension);
            w.WriteAttributeString("ContentType", contentType);
            w.WriteEndElement();
        }

        static void WriteOverride(XmlWriter w, string ns, string partName, string contentType)
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", partName);
            w.WriteAttributeString("ContentType", contentType);
            w.WriteEndElement();
        }
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

    /// <summary>Kiểu ô: 0 mặc định, 1 tiêu đề (đậm, nền xanh nhạt), 2 ngày, 3 ngày giờ, 4 nhãn (đậm).</summary>
    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartDocument(standalone: true);
        w.WriteStartElement("styleSheet", MainNs);

        w.WriteStartElement("numFmts");
        w.WriteAttributeString("count", "2");
        WriteNumberFormat(w, 164, "dd/mm/yyyy");
        WriteNumberFormat(w, 165, "dd/mm/yyyy hh:mm");
        w.WriteEndElement();

        w.WriteStartElement("fonts");
        w.WriteAttributeString("count", "2");
        WriteFont(w, bold: false);
        WriteFont(w, bold: true);
        w.WriteEndElement();

        w.WriteStartElement("fills");
        w.WriteAttributeString("count", "3");
        WritePatternFill(w, "none", null);
        WritePatternFill(w, "gray125", null);
        WritePatternFill(w, "solid", "FFDCEBF7");
        w.WriteEndElement();

        w.WriteStartElement("borders");
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("border");
        foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" })
        {
            w.WriteStartElement(side);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs");
        w.WriteAttributeString("count", "1");
        WriteXf(w, numFmtId: 0, fontId: 0, fillId: 0, withXfId: false);
        w.WriteEndElement();

        w.WriteStartElement("cellXfs");
        w.WriteAttributeString("count", "5");
        WriteXf(w, numFmtId: 0, fontId: 0, fillId: 0);
        WriteXf(w, numFmtId: 0, fontId: 1, fillId: 2);
        WriteXf(w, numFmtId: 164, fontId: 0, fillId: 0);
        WriteXf(w, numFmtId: 165, fontId: 0, fillId: 0);
        WriteXf(w, numFmtId: 0, fontId: 1, fillId: 0);
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

        static void WriteNumberFormat(XmlWriter w, int id, string code)
        {
            w.WriteStartElement("numFmt");
            w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("formatCode", code);
            w.WriteEndElement();
        }

        static void WriteFont(XmlWriter w, bool bold)
        {
            w.WriteStartElement("font");
            if (bold)
            {
                w.WriteStartElement("b");
                w.WriteEndElement();
            }

            w.WriteStartElement("sz");
            w.WriteAttributeString("val", "11");
            w.WriteEndElement();
            w.WriteStartElement("name");
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteStartElement("family");
            w.WriteAttributeString("val", "2");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void WritePatternFill(XmlWriter w, string pattern, string? rgb)
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

        static void WriteXf(XmlWriter w, int numFmtId, int fontId, int fillId, bool withXfId = true)
        {
            w.WriteStartElement("xf");
            w.WriteAttributeString("numFmtId", numFmtId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId", fontId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fillId.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", "0");
            if (withXfId)
            {
                w.WriteAttributeString("xfId", "0");
                if (numFmtId != 0)
                {
                    w.WriteAttributeString("applyNumberFormat", "1");
                }

                if (fontId != 0)
                {
                    w.WriteAttributeString("applyFont", "1");
                }

                if (fillId != 0)
                {
                    w.WriteAttributeString("applyFill", "1");
                }
            }

            w.WriteEndElement();
        }
    }

    private static void WriteColumnWidths(XmlWriter w, IReadOnlyList<double> widths)
    {
        if (widths.Count == 0)
        {
            return;
        }

        w.WriteStartElement("cols");
        for (var i = 0; i < widths.Count; i++)
        {
            var index = (i + 1).ToString(CultureInfo.InvariantCulture);
            w.WriteStartElement("col");
            w.WriteAttributeString("min", index);
            w.WriteAttributeString("max", index);
            w.WriteAttributeString("width", Math.Clamp(widths[i], 4, 120).ToString("0.##", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customWidth", "1");
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void WritePageMargins(XmlWriter w)
    {
        w.WriteStartElement("pageMargins");
        w.WriteAttributeString("left", "0.5");
        w.WriteAttributeString("right", "0.5");
        w.WriteAttributeString("top", "0.75");
        w.WriteAttributeString("bottom", "0.75");
        w.WriteAttributeString("header", "0.3");
        w.WriteAttributeString("footer", "0.3");
        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, string reference, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case string text:
                if (text.Length > 0)
                {
                    WriteText(w, reference, text, 0);
                }

                return;
            case int or long or short or byte or decimal:
                WriteNumber(w, reference, Convert.ToString(value, CultureInfo.InvariantCulture)!, 0);
                return;
            case DateOnly date when date >= new DateOnly(1900, 3, 1):
                WriteNumber(w, reference, (date.DayNumber - ExcelEpochDayNumber).ToString(CultureInfo.InvariantCulture), DateStyle);
                return;
            case DateOnly date:
                WriteText(w, reference, date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), 0);
                return;
            case DateTime time when time >= new DateTime(1900, 3, 1, 0, 0, 0, DateTimeKind.Unspecified):
                // Làm tròn đến giây để tránh sai số dấu phẩy động khi Excel hiển thị.
                var serial = Math.Round((time - ExcelEpoch).TotalSeconds) / 86_400d;
                WriteNumber(w, reference, serial.ToString("0.##########", CultureInfo.InvariantCulture), DateTimeStyle);
                return;
            case DateTime time:
                WriteText(w, reference, time.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), 0);
                return;
            case bool flag:
                WriteText(w, reference, flag ? "Có" : "Không", 0);
                return;
            default:
                WriteText(w, reference, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, 0);
                return;
        }
    }

    private static void WriteText(XmlWriter w, string reference, string text, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        if (style != 0)
        {
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        }

        w.WriteAttributeString("t", "inlineStr");
        w.WriteStartElement("is");
        w.WriteStartElement("t");
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(CleanText(text));
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteNumber(XmlWriter w, string reference, string number, int style)
    {
        w.WriteStartElement("c");
        w.WriteAttributeString("r", reference);
        if (style != 0)
        {
            w.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
        }

        w.WriteStartElement("v");
        w.WriteString(number);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WritePart(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, XmlSettings);
        write(writer);
    }

    /// <summary>Bỏ ký tự không hợp lệ trong XML (ký tự điều khiển, nửa cặp surrogate) và cắt theo giới hạn ô của Excel.</summary>
    internal static string CleanText(string text)
    {
        var needsCleaning = false;
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsSurrogate(text[i]) || !XmlConvert.IsXmlChar(text[i]))
            {
                needsCleaning = true;
                break;
            }
        }

        if (needsCleaning)
        {
            var builder = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length; i++)
            {
                if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], text[i]))
                {
                    builder.Append(text[i]).Append(text[i + 1]);
                    i++;
                }
                else if (!char.IsSurrogate(text[i]) && XmlConvert.IsXmlChar(text[i]))
                {
                    builder.Append(text[i]);
                }
            }

            text = builder.ToString();
        }

        if (text.Length > MaxCellLength)
        {
            // Không cắt giữa một cặp surrogate.
            var length = char.IsHighSurrogate(text[MaxCellLength - 1]) ? MaxCellLength - 1 : MaxCellLength;
            text = text[..length];
        }

        return text;
    }

    /// <summary>Tên trang tính: tối đa 31 ký tự, không chứa : \ / ? * [ ].</summary>
    private static string SheetName(string name)
    {
        var cleaned = new string([.. (name ?? string.Empty).Where(c => c is not (':' or '\\' or '/' or '?' or '*' or '[' or ']'))]).Trim('\'').Trim();
        cleaned = cleaned.Length > 31 ? cleaned[..31] : cleaned;
        return cleaned.Length == 0 || string.Equals(cleaned, InfoSheetName, StringComparison.OrdinalIgnoreCase) ? "Dữ liệu" : cleaned;
    }

    internal static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var n = index + 1; n > 0; n = (n - 1) / 26)
        {
            name = (char)('A' + ((n - 1) % 26)) + name;
        }

        return name;
    }

    private static string CellReference(int columnIndex, int rowNumber) =>
        string.Create(CultureInfo.InvariantCulture, $"{ColumnName(columnIndex)}{rowNumber}");

    private static string FilterRange(int columnCount, int lastRow) =>
        string.Create(CultureInfo.InvariantCulture, $"A1:{ColumnName(Math.Max(columnCount, 1) - 1)}{Math.Max(lastRow, 1)}");

    private static string AbsoluteRange(int columnCount, int lastRow) =>
        string.Create(CultureInfo.InvariantCulture, $"$A$1:${ColumnName(Math.Max(columnCount, 1) - 1)}${Math.Max(lastRow, 1)}");
}
