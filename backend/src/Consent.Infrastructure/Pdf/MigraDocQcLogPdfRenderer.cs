using System.Globalization;
using System.Text;
using Consent.Application.QualityControl;
using Consent.Domain.QualityControl;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Consent.Infrastructure.Pdf;

/// <summary>
/// In PDF "Sổ theo dõi kết quả nội kiểm đường huyết tại giường" theo bố cục sổ giấy bệnh viện gửi (mẫu CT-ĐHTG/BM01, quyết định
/// 57): A4 ngang; đầu trang có logo, tên đơn vị, mã số mẫu; phần đầu sổ ghi khoa, tháng, máy, que thử, dung dịch chứng; bảng mỗi
/// ngày một dòng như sổ giấy. Kết quả đã hủy (ghi điều chỉnh) và lần xem xét đã trả lại vẫn in, chữ xám gạch ngang, kèm lý do.
/// </summary>
internal sealed class MigraDocQcLogPdfRenderer : IQcLogPdfRenderer
{
    /// <summary>Bề rộng vùng in: A4 ngang (297 mm) trừ lề trái, phải 12 mm.</summary>
    internal const double ContentWidthMm = 273;

    /// <summary>
    /// Bề rộng các cột của bảng ngày (mm), theo tỉ lệ cột của sổ giấy: ngày, kết quả hai mức, đánh giá, nguyên nhân, kết quả chạy
    /// lại hai mức, đánh giá lại, nhân viên thực hiện, nhân viên Khoa HSH xem xét.
    /// </summary>
    internal static readonly double[] DayColumnsMm = [18, 17, 17, 24, 52, 16, 16, 24, 32, 57];

    /// <summary>Bề rộng các cột của bảng que thử, dung dịch chứng (mm).</summary>
    internal static readonly double[] MaterialColumnsMm = [40, 22, 60, 50, 55, 46];

    /// <summary>Gạch ngang chữ: ký tự kết hợp U+0336 sau mỗi ký tự (phông Tinos có sẵn, bề rộng 0, đè lên ký tự trước).</summary>
    internal const char StrikeMark = '̶';

    internal const string LateMark = "*";

    /// <summary>Dòng ngày cao ít nhất bấy nhiêu mm, như dòng của sổ giấy.</summary>
    internal const double DayRowMinHeightMm = 6.5;

    private const double TableFontSize = 8.5;
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);
    private static readonly Color Cancelled = new(0x80, 0x80, 0x80);
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock FontGate = new();

    public MigraDocQcLogPdfRenderer()
    {
        lock (FontGate)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }
        }
    }

    public byte[] Render(QcLogPdfDocument document)
    {
        var renderer = new PdfDocumentRenderer { Document = Build(document) };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>Bố cục sổ (MigraDoc) trước khi in; test xem được từng phần.</summary>
    internal static Document Build(QcLogPdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var book = document.Book;
        var pdf = new Document();
        pdf.Info.Title = string.Create(CultureInfo.InvariantCulture, $"{QcLogForm.Title} – {book.DepartmentName} – {book.Month:00}/{book.Year}");
        var normal = pdf.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = 0;
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Single;

        var section = AddSection(pdf, document.PrintedLine);
        AddHead(section, document);
        AddTitle(section);
        AddInfo(section, book);
        AddMaterials(section, book.Header);
        AddDays(section, book);
        return pdf;
    }

    /// <summary>Chữ gạch ngang (giữ nguyên chữ, thêm dấu gạch sau mỗi ký tự không phải khoảng trắng).</summary>
    internal static string Struck(string text)
    {
        var builder = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            builder.Append(c);
            if (!char.IsWhiteSpace(c))
            {
                builder.Append(StrikeMark);
            }
        }

        return builder.ToString();
    }

    internal static string Range(decimal? low, decimal? high, QcUnit? unit) =>
        low is { } from && high is { } to && unit is { } u ? $"{QcLot.Format(from)}-{QcLot.Format(to)} {QcLot.UnitLabel(u)}" : string.Empty;

    private static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static Section AddSection(Document pdf, string footerLine)
    {
        var section = pdf.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Landscape;
        section.PageSetup.TopMargin = Unit.FromMillimeter(10);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(14);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(12);
        section.PageSetup.RightMargin = Unit.FromMillimeter(12);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(6);

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

    /// <summary>Đầu trang như sổ giấy: logo, cơ quan chủ quản và tên bệnh viện ở giữa, mã số – lần ban hành – ngày hiệu lực bên phải.</summary>
    private static void AddHead(Section section, QcLogPdfDocument document)
    {
        const double LogoMm = 26;
        const double CodeMm = 70;
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
        unit.AddText(document.Hospital.ParentAgency.ToUpper(Vietnamese));
        unit.AddLineBreak();
        unit.AddFormattedText(document.Hospital.Name.ToUpper(Vietnamese), TextFormat.Bold);

        var code = row.Cells[2].AddParagraph();
        code.AddText($"Mã số: {QcLogForm.Code}");
        code.AddLineBreak();
        code.AddText($"Lần ban hành: {QcLogForm.Issue}");
        code.AddLineBreak();
        code.AddText($"Ngày hiệu lực: {QcLogForm.EffectiveDate}");
    }

    private static void AddTitle(Section section)
    {
        var title = section.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.SpaceBefore = Unit.FromMillimeter(4);
        title.Format.SpaceAfter = Unit.FromMillimeter(3);
        title.Format.Font.Bold = true;
        title.Format.Font.Size = 16;
        title.AddText(QcLogForm.Title);
        title.AddLineBreak();
        title.AddFormattedText(QcLogForm.TitleEnglish).Size = 13;
    }

    /// <summary>Khoa thực hiện; tháng và máy (loại máy, số seri) trên một dòng như sổ giấy.</summary>
    private static void AddInfo(Section section, QcBookDto book)
    {
        var department = section.AddParagraph();
        department.Format.Font.Bold = true;
        department.AddText($"KHOA/ PHÒNG THỰC HIỆN/ DEPARTMENT/UNIT: {book.DepartmentName.ToUpper(Vietnamese)}");

        var line = section.AddParagraph();
        line.Format.SpaceBefore = Unit.FromMillimeter(1.5);
        line.Format.SpaceAfter = Unit.FromMillimeter(2);
        line.Format.TabStops.AddTabStop(Unit.FromMillimeter(100));
        line.AddFormattedText("Tháng/ Month: ", TextFormat.Bold);
        line.AddText(string.Create(CultureInfo.InvariantCulture, $"{book.Month:00}/{book.Year}"));
        line.AddTab();
        line.AddFormattedText("Seri máy/ Serial Number: ", TextFormat.Bold);
        line.AddText(book.MeterLabel);
    }

    private static Table AddGrid(Section section, IEnumerable<double> widthsMm)
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

    private static void Text(Cell cell, string text, bool bold = false, ParagraphAlignment alignment = ParagraphAlignment.Center)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = alignment;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.AddLineBreak();
            }

            if (bold)
            {
                paragraph.AddFormattedText(lines[i], TextFormat.Bold);
            }
            else
            {
                paragraph.AddText(lines[i]);
            }
        }
    }

    private static string Joined(IEnumerable<string> values) => string.Join(" - ", values);

    private static string Levels(IReadOnlyList<QcLotUsageDto> usages) =>
        usages.Count == 0
            ? "Level"
            : "Level " + string.Join("/", usages.Select(u => u.Level).Distinct().Select(l => l?.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Que thử và dung dịch chứng hai mức như phần đầu sổ giấy: mỗi ô ghi các lô đã dùng trong tháng, nối bằng " - "; dung dịch
    /// chứng có thêm ngưỡng chấp nhận.
    /// </summary>
    private static void AddMaterials(Section section, QcBookHeaderDto header)
    {
        var table = AddGrid(section, MaterialColumnsMm);
        table.Rows.VerticalAlignment = VerticalAlignment.Center;
        table.Format.Font.Size = 9;

        var stripHead = table.AddRow();
        stripHead.KeepWith = 1;
        Text(stripHead.Cells[0], "Que thử/\nTest Strips", bold: true, ParagraphAlignment.Left);
        stripHead.Cells[0].MergeRight = 1;
        stripHead.Cells[0].MergeDown = 1;
        Text(stripHead.Cells[2], "Số lô/ Lot Number", bold: true);
        Text(stripHead.Cells[3], "Ngày hết hạn/ Expiration Date", bold: true);
        Text(stripHead.Cells[4], "Ngày mở hộp/ Date Opened", bold: true);
        stripHead.Cells[4].MergeRight = 1;
        var strips = table.AddRow();
        Text(strips.Cells[2], Joined(header.Strips.Select(s => s.LotNumber)));
        Text(strips.Cells[3], Joined(header.Strips.Select(s => Date(s.ExpiryDate))));
        Text(strips.Cells[4], Joined(header.Strips.Select(s => Date(s.OpenedOn))));
        strips.Cells[4].MergeRight = 1;

        var first = true;
        foreach (var usages in new[] { header.Controls1, header.Controls2 })
        {
            var head = table.AddRow();
            head.KeepWith = 1;
            if (first)
            {
                Text(head.Cells[0], "Dung dịch chứng/\nControl Solution", bold: true, ParagraphAlignment.Left);
                head.Cells[0].MergeDown = 3;
                head.KeepWith = 3;
                first = false;
            }

            Text(head.Cells[1], Levels(usages), bold: true);
            head.Cells[1].MergeDown = 1;
            Text(head.Cells[2], "Số lô/ Lot Number", bold: true);
            Text(head.Cells[3], "Ngày hết hạn/ Expiration Date", bold: true);
            Text(head.Cells[4], "Ngày mở hộp/ Date Opened", bold: true);
            Text(head.Cells[5], "Ngưỡng chấp nhận/ Acceptable Range", bold: true);
            var values = table.AddRow();
            Text(values.Cells[2], Joined(usages.Select(u => u.LotNumber)));
            Text(values.Cells[3], Joined(usages.Select(u => Date(u.ExpiryDate))));
            Text(values.Cells[4], Joined(usages.Select(u => Date(u.OpenedOn))));
            Text(values.Cells[5], Joined(usages.Select(u => Range(u.Low, u.High, u.Unit)).Distinct()), bold: true);
        }

        var space = section.AddParagraph();
        space.Format.SpaceAfter = Unit.FromMillimeter(3);
    }

    /// <summary>
    /// Bảng mỗi ngày một dòng như sổ giấy; hai dòng tiêu đề lặp lại ở mỗi trang. Kết quả đã hủy in thêm một dòng chữ xám gạch
    /// ngang ngay trước kết quả đang có hiệu lực của ngày, kèm lý do, người hủy. Kết quả ghi sau ngày thực hiện có dấu
    /// <see cref="LateMark"/> ở cột ngày, chú thích dưới bảng.
    /// </summary>
    private static void AddDays(Section section, QcBookDto book)
    {
        var table = AddGrid(section, DayColumnsMm);
        table.Rows.VerticalAlignment = VerticalAlignment.Center;
        var level1 = Levels(book.Header.Controls1);
        var level2 = Levels(book.Header.Controls2);

        var head = table.AddRow();
        head.HeadingFormat = true;
        Text(head.Cells[0], "Ngày thực hiện/\nDate", bold: true);
        head.Cells[0].MergeDown = 1;
        Text(head.Cells[1], "Kết quả thực hiện/\nResults", bold: true);
        head.Cells[1].MergeRight = 1;
        Text(head.Cells[3], "Đánh giá kết quả/\nResult Evaluation", bold: true);
        head.Cells[3].MergeDown = 1;
        Text(head.Cells[4], "Nguyên nhân không đạt/\nReason for Failure", bold: true);
        head.Cells[4].MergeDown = 1;
        Text(head.Cells[5], "Kết quả thực hiện lại/\nRepeat Run Results", bold: true);
        head.Cells[5].MergeRight = 1;
        Text(head.Cells[7], "Đánh giá lại kết quả/\nRepeat Result Evaluation", bold: true);
        head.Cells[7].MergeDown = 1;
        Text(head.Cells[8], "Nhân viên thực hiện/\nPerforming Staff", bold: true);
        head.Cells[8].MergeDown = 1;
        Text(head.Cells[9], "Nhân viên Khoa HSH xem xét/\nReviewed by Hematology & Biochemistry Staff", bold: true);
        head.Cells[9].MergeDown = 1;
        var levels = table.AddRow();
        levels.HeadingFormat = true;
        Text(levels.Cells[1], level1, bold: true);
        Text(levels.Cells[2], level2, bold: true);
        Text(levels.Cells[5], level1, bold: true);
        Text(levels.Cells[6], level2, bold: true);

        var anyLate = false;
        Row DayRow()
        {
            var row = table.AddRow();
            row.Height = Unit.FromMillimeter(DayRowMinHeightMm);
            row.HeightRule = RowHeightRule.AtLeast;
            return row;
        }

        foreach (var day in book.Days)
        {
            foreach (var cancelled in day.CancelledRuns)
            {
                AddRun(DayRow(), day.Date, cancelled, cancelledRow: true);
            }

            var row = DayRow();
            if (day.Run is { } run)
            {
                AddRun(row, day.Date, run, cancelledRow: false);
                anyLate |= run.Late;
            }
            else
            {
                Text(row.Cells[0], Date(day.Date));
            }

            AddReview(row.Cells[9], day);
        }

        if (anyLate)
        {
            var note = section.AddParagraph($"{LateMark} Kết quả ghi sau ngày thực hiện (ghi bù).");
            note.Format.Font.Size = 8;
            note.Format.Font.Italic = true;
            note.Format.SpaceBefore = Unit.FromMillimeter(1);
        }
    }

    private static void AddRun(Row row, DateOnly date, QcRunDto run, bool cancelledRow)
    {
        string Shown(string text) => cancelledRow ? Struck(text) : text;
        if (cancelledRow)
        {
            row.Format.Font.Color = Cancelled;
        }

        Text(row.Cells[0], Date(date) + (run.Late && !cancelledRow ? LateMark : string.Empty));
        Text(row.Cells[1], Shown(QcLot.Format(run.Control1.Value)));
        Text(row.Cells[2], Shown(QcLot.Format(run.Control2.Value)));
        Text(row.Cells[3], Shown(QcLogForm.Evaluation(run.Passed)));
        Text(row.Cells[4], Shown(run.FailureReason ?? string.Empty), alignment: ParagraphAlignment.Left);
        if (cancelledRow)
        {
            var note = row.Cells[4].AddParagraph();
            note.Format.Font.Italic = true;
            note.AddText($"Đã hủy: {run.CancelReason}");
            if (run.CancelledBy is { } by)
            {
                note.AddText($" ({by})");
            }
        }

        if (run.HasRepeat)
        {
            Text(row.Cells[5], Shown(QcLot.Format(run.Control1.RepeatValue!.Value)));
            Text(row.Cells[6], Shown(QcLot.Format(run.Control2.RepeatValue!.Value)));
            Text(row.Cells[7], Shown(QcLogForm.Evaluation(run.RepeatPassed == true)));
        }

        Text(row.Cells[8], run.PerformedBy + (run.FollowUpBy is { } followUp && followUp != run.PerformedBy ? $"\n(chạy lại: {followUp})" : string.Empty));
    }

    /// <summary>Lần xem xét đã trả lại (gạch ngang, kèm lý do) rồi lần xem xét đang có hiệu lực và người xem xét.</summary>
    private static void AddReview(Cell cell, QcDayDto day)
    {
        foreach (var returned in day.ReturnedReviews)
        {
            var old = cell.AddParagraph();
            old.Format.Alignment = ParagraphAlignment.Left;
            old.Format.Font.Color = Cancelled;
            old.AddText(Struck(QcLogForm.OutcomeLabel(returned.Outcome)));
            old.AddLineBreak();
            old.AddFormattedText($"Trả lại: {returned.ReturnReason}", TextFormat.Italic);
        }

        if (day.Review is not { } review)
        {
            return;
        }

        var current = cell.AddParagraph();
        current.Format.Alignment = ParagraphAlignment.Left;
        current.AddText(QcLogForm.OutcomeLabel(review.Outcome));
        current.AddLineBreak();
        var by = current.AddFormattedText(review.ReviewedBy, TextFormat.Italic);
        by.Size = 7.5;
        if (review.Note is { } note)
        {
            current.AddLineBreak();
            current.AddFormattedText($"Ghi chú: {note}", TextFormat.Italic).Size = 7.5;
        }
    }
}
