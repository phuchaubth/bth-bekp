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

    private const string SurgeryNurse = "Điều dưỡng Ngoại B (giả lập)";

    private static NursingSummaryDocument Summary(bool withIncidents = true)
    {
        var departments = new List<NursingSummaryDepartment>
        {
            new(Ward, "NOIA", "Khoa Nội A (giả lập)", IsReporting: true, BedCount: 48),
            new(Surgery, "NGOAIB", "Khoa Ngoại B (giả lập)", IsReporting: true, BedCount: 25),
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
            new Dictionary<Guid, string> { [nurse] = SurgeryNurse },
            new NursingStatisticsValues(
                new Dictionary<string, int> { [NursingHisIndicators.OutpatientVisits] = 686 },
                new Dictionary<Guid, int> { [Ward] = 2 }),
            new NursingStatisticsInfoDto(true, true, NursingStatisticsSource.His, DateTimeOffset.UtcNow, "Phòng Điều dưỡng Demo", [], false));
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
        Assert.Equal(1 + 4 + 1 + 8 + 1 + 30 + 1 + 1 + 4 + 1 + 11, figures.Count);
        Assert.Equal(["I. NHÂN SỰ", ""], figures[0]);
        Assert.Equal(["Điều dưỡng", "18"], figures[1]);
        Assert.Equal(["KTV", ""], figures[2]);
        Assert.Equal(["HL - YC", "1"], figures[4]);
        Assert.Contains(["IV. PHẢN ỨNG CÓ HẠI THUỐC (ADR)", "2"], figures);
        Assert.Contains(["Số NB hiện tại", "47"], figures);
        Assert.Contains(["* Chưa có giường", ""], figures);
        // "Số ca nhập viện ngày hôm trước" tách thành nhập khoa từ PK và do CK (quyết định 38).
        var admissions = figures.FindIndex(r => r[0] == "Số ca nhập khoa từ PK ngày hôm trước");
        Assert.Equal(["Số ca nhập khoa do CK ngày hôm trước", ""], figures[admissions + 1]);
        Assert.DoesNotContain(figures, r => r[0] == "Số ca nhập viện ngày hôm trước");
        // Hai dòng thêm ở mục VI (quyết định 44).
        var current = figures.FindIndex(r => r[0] == "Số NB hiện tại");
        Assert.Equal(["Số NB người nước ngoài", ""], figures[current + 1]);
        var discharges = figures.FindIndex(r => r[0] == "Số ca xuất viện ngày hôm trước");
        Assert.Equal(["Số ca chuyển khoa ngày hôm trước", ""], figures[discharges + 1]);
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
        // Mỗi mục B–F có dòng người báo cáo (quyết định 44).
        Assert.Equal(
            ["TỔNG KẾT BÁO CÁO PHÒNG ĐIỀU DƯỠNG HẰNG NGÀY", "Ngày 16 tháng 11 năm 2026", "A. Số liệu người bệnh nội trú",
             "B. Số liệu khoa khám bệnh", "Người báo cáo: Phòng Điều dưỡng Demo (lấy từ HIS)",
             "C. Số liệu PTTT", $"Người báo cáo: {SurgeryNurse} (NGOAIB)",
             "D. Số liệu CĐHA", "Người báo cáo: chưa có",
             "E. Tình hình trong ngày", $"Người báo cáo: {NursingReportForm.AllSubmittedReporters}",
             "F. Báo cáo cụ thể sự cố (nếu có)", $"Người báo cáo: {SurgeryNurse} (NGOAIB)"],
            paragraphs);

        // Bảng A: mã khoa (tên viết tắt), 14 cột số gồm "Người nước ngoài" và "Chuyển khoa", cột người báo cáo cuối cùng.
        var inpatients = Rows(tables[1]);
        Assert.Equal(16, inpatients[0].Length);
        Assert.Equal("KHOA PHÒNG", inpatients[0][0]);
        Assert.Equal("Số giường thực kê (Không tính băng ca)", inpatients[0][1]);
        Assert.Equal(["Số NB hiện tại", "Người nước ngoài", "Nhập khoa từ PK", "Nhập khoa do CK"], inpatients[0][2..6]);
        Assert.Equal(["Số ca Xuất viện ngày hôm trước", "Chuyển khoa"], inpatients[0][6..8]);
        Assert.Equal("Số ca đi ngoại viện", inpatients[0][14]);
        Assert.Equal("Người báo cáo", inpatients[0][15]);
        Assert.True(tables[1].Rows[0].HeadingFormat);
        // Khoa sắp theo mã; khoa chưa gửi chỉ có số giường và số HIS, không có người báo cáo.
        Assert.Equal(
            ["NGOAIB", "25", "20", "0", "0", "0", "0", "0", "0", "0", "0", "", "0", "0", "1", SurgeryNurse], inpatients[1]);
        Assert.Equal(["NOIA", "48", "", "", "", "", "", "", "", "", "", "2", "", "", "", ""], inpatients[2]);
        Assert.Equal(["Tổng", "73", "20", "0", "0", "0", "0", "0", "0", "0", "0", "2", "0", "0", "1", ""], inpatients[3]);
        // Vừa bề rộng trang: cột mã khoa, cột người báo cáo và 14 cột số bằng nhau.
        var widths = Enumerable.Range(0, tables[1].Columns.Count).Select(c => tables[1].Columns[c].Width.Millimeter).ToList();
        Assert.Equal(MigraDocNursingReportPdfRenderer.ContentWidthMm, widths.Sum(), 1);
        Assert.Equal(MigraDocNursingReportPdfRenderer.CodeColumnMm, widths[0], 1);
        Assert.Equal(MigraDocNursingReportPdfRenderer.ReporterColumnMm, widths[^1], 1);
        Assert.Single(widths.Skip(1).SkipLast(1).Select(w => Math.Round(w, 2)).Distinct());

        Assert.Equal([["NỘI DUNG", "SỐ LIỆU"], ["TSNB đến khám", "686"]], Rows(tables[2]));
        Assert.Equal(["Tủy xương", "3"], Rows(tables[3])[1]);
        Assert.Equal(10, Rows(tables[3]).Count);
        Assert.Equal(["TS MRI", ""], Rows(tables[4])[^1]);
        Assert.Equal(["NỘI DUNG", "PHẦN BÁO CÁO"], Rows(tables[5])[0]);
        Assert.Equal(["Vấn đề chuyên môn", "Ổn"], Rows(tables[5])[1]);
        Assert.Equal(
            [["KHOA", "NỘI DUNG BÁO CÁO SỰ CỐ"], ["NGOAIB", "Người bệnh B (giả lập) chuyển khoa\nTheo dõi tiếp"]],
            Rows(tables[6]));
        // Bảng B–E ngắn in liền một trang; bảng F (dài tùy phần nhập thêm) được tách trang.
        Assert.All(tables[2..6], t => Assert.Equal(t.Rows.Count - 1, t.Rows[0].KeepWith));
        Assert.Equal(0, tables[6].Rows[0].KeepWith);

        // Cuối báo cáo: chỗ Phòng Điều dưỡng ký ở nửa phải trang, chừa chỗ ký (quyết định 44).
        var signature = tables[7];
        Assert.Same(signature, Body(document)[^1]);
        Assert.Equal([["", "PHÒNG ĐIỀU DƯỠNG\n(Ký, ghi rõ họ tên)"]], Rows(signature));
        var signParagraphs = signature.Rows[0].Cells[1].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().ToList();
        Assert.All(signParagraphs, p => Assert.Equal(Mdom.ParagraphAlignment.Center, p.Format.Alignment));
        Assert.True(Assert.Single(signParagraphs[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.FormattedText>()).Bold);
        Assert.Equal(MigraDocNursingReportPdfRenderer.SignatureSpaceMm, signParagraphs[1].Format.SpaceAfter.Millimeter, 1);
    }

    [Fact]
    public void Summary_WithoutIncidents_SaysSo_AndRendersAPdf()
    {
        var input = Summary(withIncidents: false);
        var document = MigraDocNursingReportPdfRenderer.BuildSummary(input);

        var tables = Body(document).OfType<Mdom.Tables.Table>().ToList();
        Assert.Equal(["", "Không có."], Rows(tables[6])[1]);
        // Không khoa nào báo cáo sự cố: mục F tổng hợp từ mọi khoa đã gửi.
        Assert.Equal($"Người báo cáo: {NursingReportForm.AllSubmittedReporters}", Paragraphs(document).Last());
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
        // Cột khoa phòng (mã khoa), 14 cột số và cột người báo cáo của bảng A (A–P).
        Assert.Contains("A4:P4", merges);
        Assert.Contains("A5:P5", merges);
        Assert.Equal("A. Số liệu người bệnh nội trú", Texts(6)[0]);
        Assert.Equal("KHOA PHÒNG", Texts(7)[0]);
        Assert.Equal(16, rows[7].Cells.Count);
        Assert.Equal("Người nước ngoài|Nhập khoa từ PK|Nhập khoa do CK", string.Join('|', Texts(7)[3..6]));
        Assert.Equal("Chuyển khoa", Texts(7)[7]);
        Assert.Equal("Người báo cáo", Texts(7)[15]);
        Assert.Equal("NGOAIB", Texts(8)[0]);
        Assert.Equal(20, rows[8].Cells[2].Value);
        Assert.Equal(0, rows[8].Cells[3].Value);
        Assert.Equal(SurgeryNurse, Texts(8)[15]);
        Assert.Equal("NOIA", Texts(9)[0]);
        Assert.Null(rows[9].Cells[2].Value);
        Assert.Null(rows[9].Cells[15].Value);
        Assert.Equal("Tổng", Texts(10)[0]);
        Assert.Equal(73, rows[10].Cells[1].Value);

        var headings = rows.Select(r => r.Cells.FirstOrDefault()?.Value as string).Where(t => t is not null && t.Length > 2 && t[1] == '.').ToList();
        Assert.Equal(
            ["A. Số liệu người bệnh nội trú", "B. Số liệu khoa khám bệnh", "C. Số liệu PTTT", "D. Số liệu CĐHA",
             "E. Tình hình trong ngày", "F. Báo cáo cụ thể sự cố (nếu có)"],
            headings);
        // Mục B–F: nhãn gộp cột A–D (cột A hẹp, chỉ ghi mã khoa), số liệu và phần báo cáo gộp E–P.
        object? Value(XlsxNursingSummaryWriter.Row row) => row.Cells.Single(c => c.Column == XlsxNursingSummaryWriter.LabelLastColumn + 1).Value;
        var visitsIndex = rows.ToList().FindIndex(r => r.Cells.FirstOrDefault()?.Value as string == "TSNB đến khám");
        Assert.Equal(686, Value(rows[visitsIndex]));
        Assert.Equal(XlsxNursingSummaryWriter.LabelLastColumn + 1 + 12, rows[visitsIndex].Cells.Count);
        Assert.Contains($"A{visitsIndex + 1}:D{visitsIndex + 1}", merges);
        Assert.Contains($"E{visitsIndex + 1}:P{visitsIndex + 1}", merges);
        Assert.Contains($"A{visitsIndex}:D{visitsIndex}", merges);
        var incident = rows.Single(r => r.Cells.FirstOrDefault()?.Value as string == "NGOAIB" && Value(r) is string);
        Assert.Equal("Người bệnh B (giả lập) chuyển khoa\nTheo dõi tiếp", Value(incident));
        Assert.NotNull(incident.Height);

        // Mỗi mục B–F có dòng người báo cáo, gộp cả bề rộng trang (quyết định 44).
        var reporters = rows.Select((r, i) => (Row: r, Number: i + 1))
            .Where(r => r.Row.Cells.FirstOrDefault()?.Value is string text && text.StartsWith("Người báo cáo: ", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(
            ["Người báo cáo: Phòng Điều dưỡng Demo (lấy từ HIS)", $"Người báo cáo: {SurgeryNurse} (NGOAIB)", "Người báo cáo: chưa có",
             $"Người báo cáo: {NursingReportForm.AllSubmittedReporters}", $"Người báo cáo: {SurgeryNurse} (NGOAIB)"],
            reporters.Select(r => r.Row.Cells[0].Value));
        Assert.All(reporters, r =>
        {
            Assert.Equal(XlsxNursingSummaryWriter.Style.Note, r.Row.Cells[0].Style);
            Assert.Contains($"A{r.Number}:P{r.Number}", merges);
        });

        // Chỗ Phòng Điều dưỡng ký ở góc phải cuối trang (cột J–P), vài dòng trống để ký, rồi dòng in lúc.
        var sign = rows.ToList().FindIndex(r => r.Cells.FirstOrDefault()?.Value as string == NursingReportForm.SignatureTitle);
        Assert.Equal(9, rows[sign].Cells[0].Column);
        Assert.Equal(XlsxNursingSummaryWriter.Style.CenterBold, rows[sign].Cells[0].Style);
        Assert.Equal(NursingReportForm.SignatureHint, rows[sign + 1].Cells[0].Value);
        Assert.Contains($"J{sign + 1}:P{sign + 1}", merges);
        Assert.Contains($"J{sign + 2}:P{sign + 2}", merges);
        Assert.All(rows.Skip(sign + 2).Take(XlsxNursingSummaryWriter.SignatureBlankRows), r => Assert.Empty(r.Cells));
        Assert.Equal("In lúc 08:00 16/11/2026 · Khoa đã gửi: 1/2", Texts(rows.Count - 1)[0]);
        Assert.Equal(sign + 2 + XlsxNursingSummaryWriter.SignatureBlankRows, rows.Count - 1);
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
        Assert.Contains(cells, c => (string?)c.Attribute("r") == "A9" && c.Value == "NGOAIB");
        Assert.Contains(cells, c => (string?)c.Attribute("r") == "P9" && c.Value == SurgeryNurse);
        Assert.Contains(cells, c => (string?)c.Attribute("r") == "C9" && c.Attribute("t") is null && c.Value == "20");
        Assert.Equal(
            sheet.Descendants(main + "mergeCell").Count(),
            (int)sheet.Descendants(main + "mergeCells").Single().Attribute("count")!);
        // Cột mã khoa hẹp, 14 cột số, cột người báo cáo rộng đủ ghi họ tên (quyết định 44).
        Assert.Equal(
            ["1-1:12", "2-15:10.5", "16-16:22"],
            sheet.Descendants(main + "col").Select(c => $"{c.Attribute("min")!.Value}-{c.Attribute("max")!.Value}:{c.Attribute("width")!.Value}"));
        using var styles = zip.GetEntry("xl/styles.xml")!.Open();
        var cellStyles = XDocument.Load(styles).Descendants(main + "cellXfs").Single();
        Assert.Equal(cellStyles.Elements(main + "xf").Count(), (int)cellStyles.Attribute("count")!);
        Assert.Equal(Enum.GetValues<XlsxNursingSummaryWriter.Style>().Length, cellStyles.Elements(main + "xf").Count());
        // Dòng người báo cáo xuống dòng trong ô (danh sách người báo cáo có thể dài), chỗ ký canh giữa, chữ đậm.
        var note = cellStyles.Elements(main + "xf").ElementAt((int)XlsxNursingSummaryWriter.Style.Note).Element(main + "alignment");
        Assert.Equal("1", (string?)note?.Attribute("wrapText"));
        Assert.Equal("top", (string?)note?.Attribute("vertical"));
        var sign = cellStyles.Elements(main + "xf").ElementAt((int)XlsxNursingSummaryWriter.Style.CenterBold);
        Assert.Equal("center", (string?)sign.Element(main + "alignment")?.Attribute("horizontal"));
        Assert.Equal("1", (string?)sign.Attribute("fontId"));
    }
}
