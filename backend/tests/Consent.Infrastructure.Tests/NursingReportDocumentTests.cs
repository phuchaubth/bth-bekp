using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Consent.Application.NursingReports;
using Consent.Domain.NursingReports;
using Consent.Infrastructure.Exports;
using Consent.Infrastructure.Pdf;
using Mdom = MigraDoc.DocumentObjectModel;

namespace Consent.Infrastructure.Tests;

/// <summary>PDF báo cáo khoa, PDF và Excel bảng tổng hợp điều dưỡng theo bố cục bản giấy (quyết định 31).</summary>
public sealed class NursingReportDocumentTests
{
    private const string Hospital = "Bệnh viện Giả Lập";
    private static readonly DateOnly Day = new(2026, 11, 16);
    private static readonly Guid Ward = Guid.NewGuid();
    private static readonly Guid Surgery = Guid.NewGuid();

    private static NursingReportPdfDocument Report(string? notes = "Người bệnh A (giả lập): sốt về đêm\nĐã báo bác sĩ trực") => new(
        Hospital,
        "Khoa Nội A (giả lập)",
        Day,
        new NursingReportFigures(
            new Dictionary<string, int> { ["nurses"] = 18, ["orderlies"] = 1, ["adr"] = 2, ["currentPatients"] = 47 },
            NursingReportForm.Checks.ToDictionary(
                c => c.Key, c => c.Key == "security" ? NursingReportForm.CheckIssue : NursingReportForm.CheckOk, StringComparer.Ordinal)),
        notes,
        "Đã gửi lúc 07:45 16/11/2026 bởi Điều dưỡng Demo");

    private static NursingSummaryDocument Summary(bool withIncidents = true)
    {
        var departments = new List<NursingSummaryDepartment>
        {
            new(Ward, "Khoa Nội A (giả lập)", IsReporting: true, BedCount: 48),
            new(Surgery, "Khoa Ngoại B (giả lập)", IsReporting: true, BedCount: 25),
        };
        var nurse = Guid.NewGuid();
        var report = NursingDailyReport.Create(
            Surgery,
            Day,
            new NursingReportFigures(
                new Dictionary<string, int> { ["currentPatients"] = 20, ["boneMarrow"] = 3, ["outsideExams"] = 1 },
                NursingReportForm.Checks.ToDictionary(c => c.Key, _ => NursingReportForm.CheckOk, StringComparer.Ordinal)).ToJson(),
            withIncidents ? "Người bệnh B (giả lập) chuyển khoa\nTheo dõi tiếp" : null,
            nurse,
            DateTimeOffset.UtcNow);
        report.Submit(nurse, DateTimeOffset.UtcNow);
        var summary = NursingSummaryBuilder.Build(
            Day,
            departments,
            [report],
            new Dictionary<Guid, string>(),
            new NursingStatisticsValues(
                new Dictionary<string, int> { [NursingHisIndicators.OutpatientVisits] = 686 },
                new Dictionary<Guid, int> { [Ward] = 2 }),
            new NursingStatisticsInfoDto(true, true, NursingStatisticsSource.His, DateTimeOffset.UtcNow, "Phòng Điều dưỡng Demo"));
        return new NursingSummaryDocument(Hospital, summary, "In lúc 08:00 16/11/2026 · Khoa đã gửi: 1/2");
    }

    private static List<Mdom.DocumentObject> Body(Mdom.Document document) =>
        [.. document.Sections[0].Elements.Cast<Mdom.DocumentObject>()];

    private static string TextOf(Mdom.Paragraph paragraph) => TextOf(paragraph.Elements);

    private static string TextOf(Mdom.ParagraphElements elements) =>
        string.Concat(elements.Cast<Mdom.DocumentObject>().Select(element => element switch
        {
            Mdom.Text text => text.Content,
            Mdom.FormattedText formatted => TextOf(formatted.Elements),
            Mdom.Character { SymbolName: Mdom.SymbolName.Tab } => "\t",
            Mdom.Character { SymbolName: Mdom.SymbolName.LineBreak } => "\n",
            _ => string.Empty,
        }));

    private static IEnumerable<string> Paragraphs(Mdom.Document document) =>
        Body(document).OfType<Mdom.Paragraph>().Select(TextOf);

    private static List<string[]> Rows(Mdom.Tables.Table table) =>
        [.. Enumerable.Range(0, table.Rows.Count).Select(r => Enumerable.Range(0, table.Columns.Count)
            .Select(c => string.Join("\n", table.Rows[r].Cells[c].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().Select(TextOf)))
            .ToArray())];

    [Fact]
    public void Report_FollowsThePaperForm()
    {
        var document = MigraDocNursingReportPdfRenderer.BuildReport(Report());
        var tables = Body(document).OfType<Mdom.Tables.Table>().ToList();

        // Đầu báo cáo: tên bệnh viện viết hoa, tên khoa bên dưới; "Khoa Lâm sàng" bên phải.
        Assert.Equal(["BỆNH VIỆN GIẢ LẬP\nKhoa Nội A (giả lập)", "Khoa Lâm sàng"], Rows(tables[0])[0]);
        Assert.Equal(
            ["BÁO CÁO CÔNG TÁC ĐIỀU DƯỠNG", "Ngày 16 tháng 11 năm 2026", "PHẦN SỐ LIỆU", ],
            Paragraphs(document).Take(3));

        var figures = Rows(tables[1]);
        Assert.Equal(1 + 4 + 1 + 8 + 1 + 30 + 1 + 1 + 4 + 1 + 8, figures.Count);
        Assert.Equal(["I. NHÂN SỰ", ""], figures[0]);
        Assert.Equal(["Điều dưỡng", "18"], figures[1]);
        Assert.Equal(["KTV", ""], figures[2]);
        Assert.Equal(["HL - YC", "1"], figures[4]);
        Assert.Contains(["IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)", "2"], figures);
        Assert.Contains(["Số NB hiện tại", "47"], figures);
        Assert.Contains(["* Chưa có giường", ""], figures);
        // Dòng tiêu đề mục gộp hai cột, trừ mục IV có ô nhập số.
        Assert.Equal(1, tables[1].Rows[0].Cells[0].MergeRight);

        var ticks = Paragraphs(document).SkipWhile(p => p != "PHẦN TICK").Skip(1).Take(8).ToList();
        Assert.Equal("Vấn đề chuyên môn\t☐Có sự cố/ Không ổn\t☒Không có sự cố/ Ổn", ticks[0]);
        Assert.Equal("An ninh trật tự\t☒Có sự cố/ Không ổn\t☐Không có sự cố/ Ổn", ticks[5]);
        Assert.Equal(
            "Báo cáo các vấn đề bất thường (nếu có): Người bệnh A (giả lập): sốt về đêm\nĐã báo bác sĩ trực",
            Paragraphs(document).Last());

        var footer = TextOf((Mdom.Paragraph)document.Sections[0].Footers.Primary.Elements[0]!);
        Assert.StartsWith("Đã gửi lúc 07:45 16/11/2026 bởi Điều dưỡng Demo\tTrang ", footer, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_RendersAPdfWithTheEmbeddedFonts()
    {
        var pdf = new MigraDocNursingReportPdfRenderer().RenderReport(Report(notes: null));
        var text = Encoding.Latin1.GetString(pdf);

        Assert.StartsWith("%PDF", text, StringComparison.Ordinal);
        Assert.Contains("+Tinos", text, StringComparison.Ordinal);
        Assert.Contains("+DejaVu", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_FollowsThePaperForm()
    {
        var document = MigraDocNursingReportPdfRenderer.BuildSummary(Summary());
        var tables = Body(document).OfType<Mdom.Tables.Table>().ToList();
        var paragraphs = Paragraphs(document).ToList();

        Assert.Equal(["BỆNH VIỆN GIẢ LẬP\nPhòng Điều dưỡng", ""], Rows(tables[0])[0]);
        Assert.Equal(
            ["TỔNG KẾT BÁO CÁO PHÒNG ĐIỀU DƯỠNG HẰNG NGÀY", "Ngày 16 tháng 11 năm 2026", "A. Số liệu người bệnh nội trú",
             "B. Số liệu khoa khám bệnh", "C. Số liệu PTTT", "D. Số liệu CĐHA", "E. Tình hình trong ngày",
             "F. Báo cáo cụ thể sự cố (nếu có)"],
            paragraphs);

        var inpatients = Rows(tables[1]);
        Assert.Equal(12, inpatients[0].Length);
        Assert.Equal("KHOA PHÒNG", inpatients[0][0]);
        Assert.Equal("Số giường thực kê (Không tính băng ca)", inpatients[0][1]);
        Assert.Equal("Số ca đi ngoại viện", inpatients[0][11]);
        Assert.True(tables[1].Rows[0].HeadingFormat);
        // Khoa sắp theo tên; khoa chưa gửi chỉ có số giường và số HIS.
        Assert.Equal(["Khoa Ngoại B (giả lập)", "25", "20", "0", "0", "0", "0", "0", "", "0", "0", "1"], inpatients[1]);
        Assert.Equal(["Khoa Nội A (giả lập)", "48", "", "", "", "", "", "", "2", "", "", ""], inpatients[2]);
        Assert.Equal(["Tổng", "73", "20", "0", "0", "0", "0", "0", "2", "0", "0", "1"], inpatients[3]);

        Assert.Equal([["NỘI DUNG", "SỐ LIỆU"], ["TSNB đến khám", "686"]], Rows(tables[2]));
        Assert.Equal(["Tủy xương", "3"], Rows(tables[3])[1]);
        Assert.Equal(10, Rows(tables[3]).Count);
        Assert.Equal(["TS MRI", ""], Rows(tables[4])[^1]);
        Assert.Equal(["NỘI DUNG", "PHẦN BÁO CÁO"], Rows(tables[5])[0]);
        Assert.Equal(["Vấn đề chuyên môn", "Ổn"], Rows(tables[5])[1]);
        Assert.Equal(
            [["KHOA", "NỘI DUNG BÁO CÁO SỰ CỐ"], ["Khoa Ngoại B (giả lập)", "Người bệnh B (giả lập) chuyển khoa\nTheo dõi tiếp"]],
            Rows(tables[6]));
    }

    [Fact]
    public void Summary_WithoutIncidents_SaysSo_AndRendersAPdf()
    {
        var input = Summary(withIncidents: false);
        var document = MigraDocNursingReportPdfRenderer.BuildSummary(input);

        Assert.Equal(["", "Không có."], Rows(Body(document).OfType<Mdom.Tables.Table>().Last())[1]);
        Assert.StartsWith("%PDF", Encoding.Latin1.GetString(new MigraDocNursingReportPdfRenderer().RenderSummary(input)), StringComparison.Ordinal);
    }

    [Fact]
    public void Workbook_LaysOutSectionsAToF_WithNumbersAsNumbers()
    {
        var (rows, merges) = XlsxNursingSummaryWriter.Layout(Summary());
        string?[] Texts(int row) => [.. rows[row].Cells.Select(c => c.Value?.ToString())];

        Assert.Equal("BỆNH VIỆN GIẢ LẬP", Texts(0)[0]);
        Assert.Equal("Phòng Điều dưỡng", Texts(1)[0]);
        Assert.Equal(NursingReportForm.SummaryTitle, Texts(3)[0]);
        Assert.Equal("Ngày 16 tháng 11 năm 2026", Texts(4)[0]);
        Assert.Contains("A4:L4", merges);
        Assert.Contains("A5:L5", merges);
        Assert.Equal("A. Số liệu người bệnh nội trú", Texts(6)[0]);
        Assert.Equal("KHOA PHÒNG", Texts(7)[0]);
        Assert.Equal(12, rows[7].Cells.Count);
        Assert.Equal(20, rows[8].Cells[2].Value);
        Assert.Null(rows[9].Cells[2].Value);
        Assert.Equal("Tổng", Texts(10)[0]);
        Assert.Equal(73, rows[10].Cells[1].Value);

        var headings = rows.Select(r => r.Cells.FirstOrDefault()?.Value as string).Where(t => t is not null && t.Length > 2 && t[1] == '.').ToList();
        Assert.Equal(
            ["A. Số liệu người bệnh nội trú", "B. Số liệu khoa khám bệnh", "C. Số liệu PTTT", "D. Số liệu CĐHA",
             "E. Tình hình trong ngày", "F. Báo cáo cụ thể sự cố (nếu có)"],
            headings);
        var visits = rows.Single(r => r.Cells.FirstOrDefault()?.Value as string == "TSNB đến khám");
        Assert.Equal(686, visits.Cells[1].Value);
        var incident = rows.Single(r => r.Cells.FirstOrDefault()?.Value as string == "Khoa Ngoại B (giả lập)" && r.Cells[1].Value is string);
        Assert.Equal("Người bệnh B (giả lập) chuyển khoa\nTheo dõi tiếp", incident.Cells[1].Value);
        Assert.NotNull(incident.Height);
    }

    [Fact]
    public void Workbook_IsAValidXlsxPackage()
    {
        var bytes = new XlsxNursingSummaryWriter().Write(Summary());

        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal(
            ["[Content_Types].xml", "_rels/.rels", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/workbook.xml", "xl/worksheets/sheet1.xml"],
            zip.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal));
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var stream = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var sheet = XDocument.Load(stream);
        var cells = sheet.Descendants(main + "c").ToList();
        Assert.Contains(cells, c => (string?)c.Attribute("r") == "A9" && c.Value == "Khoa Ngoại B (giả lập)");
        Assert.Contains(cells, c => (string?)c.Attribute("r") == "C9" && c.Attribute("t") is null && c.Value == "20");
        Assert.Equal(
            sheet.Descendants(main + "mergeCell").Count(),
            (int)sheet.Descendants(main + "mergeCells").Single().Attribute("count")!);
        using var styles = zip.GetEntry("xl/styles.xml")!.Open();
        var cellStyles = XDocument.Load(styles).Descendants(main + "cellXfs").Single();
        Assert.Equal(cellStyles.Elements(main + "xf").Count(), (int)cellStyles.Attribute("count")!);
        Assert.Equal(Enum.GetValues<XlsxNursingSummaryWriter.Style>().Length, cellStyles.Elements(main + "xf").Count());
    }
}
