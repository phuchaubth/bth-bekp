using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Consent.Application.Abstractions;
using Consent.Infrastructure.Exports;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;

namespace Consent.Infrastructure.Tests;

public sealed class TabularFileWriterTests
{
    private static readonly TabularColumn[] Columns =
    [
        new("Số phiếu"),
        new("Họ tên người bệnh", TabularCellKind.Text, 26),
        new("Tổng điểm", TabularCellKind.Number, 10),
        new("Ngày sinh", TabularCellKind.Date, 11),
        new("Thời điểm lập", TabularCellKind.DateTime, 17),
    ];

    private static readonly KeyValuePair<string, string>[] Properties =
    [
        new("Người xuất", "Báo cáo Demo"),
        new("Dữ liệu nhạy cảm", "Đã che một phần"),
    ];

    private static async IAsyncEnumerable<IReadOnlyList<object?>> AsAsync(
        IEnumerable<IReadOnlyList<object?>> rows, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var row in rows)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            yield return row;
        }
    }

    /// <summary>Dòng mẫu có ký tự đặc biệt của XML, chuỗi giống công thức, ký tự điều khiển, emoji và ô trống.</summary>
    private static readonly IReadOnlyList<object?>[] SampleRows =
    [
        ["PDT-2026-000001", "Phạm Thị Dung (giả lập) <&> \"x\" 'y'", 40, new DateOnly(1988, 4, 5), new DateTime(2026, 11, 12, 8, 30, 0)],
        ["PDT-2026-000002", "=HYPERLINK(\"http://evil.example\",\"Bấm vào\")", null, null, new DateTime(2000, 1, 1, 12, 0, 0)],
        ["PDT-2026-000003", "Lỗi\u0001ký tự điều khiển, emoji 👍, xuống dòng\nhai", 0, new DateOnly(2000, 1, 1), null],
    ];

    private static TabularData Data(IEnumerable<IReadOnlyList<object?>> rows) =>
        new("Phiếu đồng thuận", Columns, AsAsync(rows), Properties);

    [Fact]
    public async Task Xlsx_IsAValidOfficeOpenXmlWorkbook_WithTypedCellsFrozenHeaderFilterAndInfoSheet()
    {
        using var stream = new MemoryStream();

        var rowCount = await new XlsxTabularFileWriter().WriteAsync(Data(SampleRows), stream, CancellationToken.None);

        Assert.Equal(3, rowCount);
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2016).Validate(document)
            .Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")
            .ToList();
        Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));

        var workbook = document.WorkbookPart!;
        var sheets = workbook.Workbook!.Sheets!.Elements<Sheet>().ToList();
        Assert.Equal(["Phiếu đồng thuận", "Thông tin xuất"], sheets.Select(s => s.Name!.Value));
        var data = ((WorksheetPart)workbook.GetPartById(sheets[0].Id!)).Worksheet!;
        var rows = data.Descendants<Row>().ToList();
        Assert.Equal(4, rows.Count);

        Assert.Equal(["Số phiếu", "Họ tên người bệnh", "Tổng điểm", "Ngày sinh", "Thời điểm lập"], rows[0].Elements<Cell>().Select(Text));
        Assert.All(rows[0].Elements<Cell>(), header => Assert.Equal(1u, header.StyleIndex!.Value));
        Assert.Equal("Phạm Thị Dung (giả lập) <&> \"x\" 'y'", Text(Cell(rows, "B2")));
        Assert.Equal("40", Cell(rows, "C2").CellValue!.Text);
        Assert.Null(Cell(rows, "C2").DataType);
        // Ngày là số sê-ri của Excel kèm định dạng dd/mm/yyyy (kiểu 2) hoặc dd/mm/yyyy hh:mm (kiểu 3).
        Assert.Equal("32238", Cell(rows, "D2").CellValue!.Text);
        Assert.Equal(2u, Cell(rows, "D2").StyleIndex!.Value);
        Assert.Equal(new DateTime(2026, 11, 12, 8, 30, 0), DateTime.FromOADate(double.Parse(Cell(rows, "E2").CellValue!.Text, CultureInfo.InvariantCulture)));
        Assert.Equal(3u, Cell(rows, "E2").StyleIndex!.Value);
        Assert.Equal("36526.5", Cell(rows, "E3").CellValue!.Text);
        // Chuỗi giống công thức vẫn là chữ: không có phần tử công thức nào trong file.
        Assert.Equal(CellValues.InlineString, Cell(rows, "B3").DataType!.Value);
        Assert.Empty(data.Descendants<CellFormula>());
        Assert.StartsWith("=HYPERLINK", Text(Cell(rows, "B3")), StringComparison.Ordinal);
        Assert.DoesNotContain(rows[2].Elements<Cell>(), c => c.CellReference == "C3");
        // Ký tự điều khiển bị bỏ; emoji và xuống dòng được giữ.
        Assert.Equal("Lỗiký tự điều khiển, emoji 👍, xuống dòng\nhai", Text(Cell(rows, "B4")));
        Assert.Equal("0", Cell(rows, "C4").CellValue!.Text);

        var pane = data.Descendants<Pane>().Single();
        Assert.Equal(PaneStateValues.Frozen, pane.State!.Value);
        Assert.Equal(1d, pane.VerticalSplit!.Value);
        Assert.Equal("A1:E4", data.Descendants<AutoFilter>().Single().Reference!.Value);
        Assert.Equal("'Phiếu đồng thuận'!$A$1:$E$4", workbook.Workbook!.DefinedNames!.Elements<DefinedName>().Single().Text);

        var info = ((WorksheetPart)workbook.GetPartById(sheets[1].Id!)).Worksheet!.Descendants<Row>().ToList();
        Assert.Equal(["Người xuất", "Báo cáo Demo"], info[0].Elements<Cell>().Select(Text));
        Assert.Equal(["Dữ liệu nhạy cảm", "Đã che một phần"], info[1].Elements<Cell>().Select(Text));
    }

    [Fact]
    public async Task Xlsx_WithoutRows_StillHasTheHeader_AndCutsCellsLongerThanExcelAllows()
    {
        using var empty = new MemoryStream();
        var none = await new XlsxTabularFileWriter().WriteAsync(Data([]), empty, CancellationToken.None);

        using var longText = new MemoryStream();
        await new XlsxTabularFileWriter().WriteAsync(Data([["PDT-1", new string('x', 40_000), null, null, null]]), longText, CancellationToken.None);

        Assert.Equal(0, none);
        empty.Position = 0;
        using (var document = SpreadsheetDocument.Open(empty, isEditable: false))
        {
            Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2016).Validate(document));
            var worksheet = DataSheet(document);
            Assert.Single(worksheet.Descendants<Row>());
            Assert.Equal("A1:E1", worksheet.Descendants<AutoFilter>().Single().Reference!.Value);
        }

        longText.Position = 0;
        using (var document = SpreadsheetDocument.Open(longText, isEditable: false))
        {
            var worksheet = DataSheet(document);
            Assert.Equal(32_767, Text(worksheet.Descendants<Cell>().Single(c => c.CellReference == "B2")).Length);
        }
    }

    [Fact]
    public async Task Xlsx_TenThousandRows_AreWrittenQuickly()
    {
        var rows = Enumerable.Range(1, 10_000).Select(i => (IReadOnlyList<object?>)
            [$"PDT-2026-{i:000000}", $"Người bệnh giả lập {i}", i % 125, new DateOnly(1950, 1, 1).AddDays(i), new DateTime(2026, 11, 1).AddMinutes(i)]);
        using var stream = new MemoryStream();
        var timer = Stopwatch.StartNew();

        var count = await new XlsxTabularFileWriter().WriteAsync(Data(rows), stream, CancellationToken.None);

        Assert.Equal(10_000, count);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Ghi 10.000 dòng mất {timer.Elapsed}.");
        stream.Position = 0;
        using var document = SpreadsheetDocument.Open(stream, isEditable: false);
        var worksheet = DataSheet(document);
        Assert.Equal(10_001, worksheet.Descendants<Row>().Count());
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    public void ColumnNames_FollowExcelLetters(int index, string name) =>
        Assert.Equal(name, XlsxTabularFileWriter.ColumnName(index));

    [Fact]
    public async Task Csv_IsUtf8WithBom_QuotesAsRfc4180_AndNeutralizesFormulas()
    {
        var rows = new IReadOnlyList<object?>[]
        {
            ["PDT-1", "Nguyễn Văn An, \"An\"", 40, new DateOnly(1988, 4, 5), new DateTime(2026, 11, 12, 8, 30, 0)],
            ["PDT-2", "=HYPERLINK(\"http://evil.example\")", null, null, null],
            ["PDT-3", "+84900000000", 0, null, null],
            ["PDT-4", "-5", null, null, null],
            ["PDT-5", "@SUM(A1)", null, null, null],
            ["PDT-6", "dòng 1\ndòng 2", null, null, null],
        };
        using var stream = new MemoryStream();

        var count = await new CsvTabularFileWriter().WriteAsync(Data(rows), stream, CancellationToken.None);

        Assert.Equal(6, count);
        var bytes = stream.ToArray();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Equal(
            "Số phiếu,Họ tên người bệnh,Tổng điểm,Ngày sinh,Thời điểm lập\r\n" +
            "PDT-1,\"Nguyễn Văn An, \"\"An\"\"\",40,05/04/1988,12/11/2026 08:30\r\n" +
            "PDT-2,\"'=HYPERLINK(\"\"http://evil.example\"\")\",,,\r\n" +
            "PDT-3,'+84900000000,0,,\r\n" +
            "PDT-4,'-5,,,\r\n" +
            "PDT-5,'@SUM(A1),,,\r\n" +
            "PDT-6,\"dòng 1\ndòng 2\",,,\r\n",
            text);
    }

    [Fact]
    public async Task Csv_HeadersFromTemplateLabels_AreNeutralizedToo()
    {
        var data = new TabularData("x", [new("=cmd|' /C calc'!A0")], AsAsync([["a"]]), []);
        using var stream = new MemoryStream();

        await new CsvTabularFileWriter().WriteAsync(data, stream, CancellationToken.None);

        Assert.StartsWith("'=cmd|", Encoding.UTF8.GetString(stream.ToArray(), 3, (int)stream.Length - 3), StringComparison.Ordinal);
    }

    private static Worksheet DataSheet(SpreadsheetDocument document) =>
        document.WorkbookPart!.WorksheetParts.Select(p => p.Worksheet!).First(w => w.Descendants<AutoFilter>().Any());

    private static Cell Cell(List<Row> rows, string reference) =>
        rows.SelectMany(r => r.Elements<Cell>()).Single(c => c.CellReference == reference);

    private static string Text(Cell cell) => cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty;
}
