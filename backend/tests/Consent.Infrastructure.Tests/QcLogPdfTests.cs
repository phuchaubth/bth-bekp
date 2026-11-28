using Consent.Application.ConsentForms;
using Consent.Application.QualityControl;
using Consent.Domain.QualityControl;
using Consent.Infrastructure.Pdf;
using MigraDoc.Rendering;
using Mdom = MigraDoc.DocumentObjectModel;

namespace Consent.Infrastructure.Tests;

/// <summary>PDF sổ theo dõi kết quả nội kiểm đường huyết theo bố cục sổ giấy (mẫu CT-ĐHTG/BM01, quyết định 57). Dữ liệu giả lập.</summary>
public sealed class QcLogPdfTests
{
    private static readonly DateOnly Opened = new(2026, 10, 20);
    private static readonly DateOnly Expiry = new(2027, 6, 30);

    private static DateOnly Day(int day) => new(2026, 11, day);

    private static QcControlDto Control(int level, string lot, decimal low, decimal high, decimal value, decimal? repeat = null) => new(
        Guid.NewGuid(), level, lot, Expiry, Opened, low, high, QcUnit.MgPerDl, value, QcLot.InRange(value, low, high),
        repeat, repeat is { } r ? QcLot.InRange(r, low, high) : null);

    private static QcRunDto Run(
        int day,
        decimal value1,
        decimal value2,
        string strip = "QT-01",
        string? reason = null,
        decimal? repeat1 = null,
        decimal? repeat2 = null,
        bool late = false,
        string? cancelReason = null,
        string performer = "Điều dưỡng A (giả lập)",
        string? followUpBy = null)
    {
        var control1 = Control(1, "DC-01", 40, 70, value1, repeat1);
        var control2 = Control(2, "DC-02", 100, 140, value2, repeat2);
        var hasRepeat = repeat1 is not null;
        return new QcRunDto(
            Guid.NewGuid(),
            Day(day),
            new QcStripDto(Guid.NewGuid(), strip, Expiry, Opened),
            control1,
            control2,
            control1.Passed && control2.Passed,
            reason,
            hasRepeat,
            hasRepeat ? control1.RepeatPassed == true && control2.RepeatPassed == true : null,
            followUpBy,
            followUpBy is null ? null : DateTimeOffset.UtcNow,
            performer,
            DateTimeOffset.UtcNow,
            late,
            null,
            cancelReason,
            cancelReason is null ? null : performer,
            cancelReason is null ? null : DateTimeOffset.UtcNow,
            false,
            false);
    }

    private static QcReviewDto Review(QcReviewOutcome outcome, string? note = null, string? returnReason = null) => new(
        Guid.NewGuid(), outcome, note, "Kỹ thuật viên HSH (giả lập)", DateTimeOffset.UtcNow, returnReason,
        returnReason is null ? null : "Kỹ thuật viên HSH (giả lập)", returnReason is null ? null : DateTimeOffset.UtcNow);

    private static QcDayDto DayOf(
        int day, QcRunDto? run = null, QcReviewDto? review = null, QcRunDto[]? cancelled = null, QcReviewDto[]? returned = null) =>
        new(Day(day), run, cancelled ?? [], review, returned ?? [], QcReviewOutcome.NoQc, [], false, false, false);

    private static QcLotUsageDto Usage(string lot, int? level = null, decimal? low = null, decimal? high = null) =>
        new(lot, Expiry, Opened, level, low, high, level is null ? null : QcUnit.MgPerDl);

    /// <summary>Sổ tháng 11/2026: ngày 1 đạt, ngày 2 không đạt rồi chạy lại đạt, ngày 3 ghi điều chỉnh, ngày 4 ghi muộn; ngày 5 bị trả lại rồi xem xét lại.</summary>
    private static QcBookDto Book(IReadOnlyList<QcDayDto>? days = null) => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Máy đo A (giả lập) GL-0001",
        Guid.NewGuid(),
        "Khoa Nội A (giả lập)",
        2026,
        11,
        QcBookStatus.Open,
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        new QcBookHeaderDto([Usage("QT-01"), Usage("QT-02")], [Usage("DC-01", 1, 40, 70)], [Usage("DC-02", 2, 100, 140)]),
        days ?? [.. Enumerable.Range(1, 30).Select(d => d switch
        {
            1 => DayOf(1, Run(1, 55, 120), Review(QcReviewOutcome.Passed)),
            2 => DayOf(2, Run(2, 80, 120, reason: "Lọ dung dịch chứng để ngoài tủ mát", repeat1: 60, repeat2: 118, followUpBy: "Điều dưỡng B (giả lập)")),
            3 => DayOf(3, Run(3, 55, 121), cancelled: [Run(3, 55, 212, cancelReason: "Ghi nhầm kết quả mức 2")]),
            4 => DayOf(4, Run(4, 56, 119, strip: "QT-02", late: true)),
            5 => DayOf(5, Run(5, 54, 118), Review(QcReviewOutcome.Passed, note: "Đã kiểm lại"), returned: [Review(QcReviewOutcome.FailedNotRepeated, returnReason: "Chọn nhầm kết quả xem xét")]),
            _ => DayOf(d, review: d < 10 ? Review(QcReviewOutcome.NoQc) : null),
        })],
        [],
        null,
        new QcBookActionsDto(false, false, false, false, false));

    private static QcLogPdfDocument Document(QcBookDto? book = null) => new(
        new HospitalOptions { ParentAgency = "Sở Y tế Giả Lập", Name = "Bệnh viện Giả Lập" },
        null,
        book ?? Book(),
        "Khoa đang ghi, chưa gửi sổ tháng (in lúc 08:00 20/11/2026)");

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

    private static string Plain(string text) => text.Replace(MigraDocQcLogPdfRenderer.StrikeMark.ToString(), string.Empty, StringComparison.Ordinal);

    [Fact]
    public void Head_Title_AndMaterials_FollowThePaperForm()
    {
        var document = MigraDocQcLogPdfRenderer.Build(Document());
        var section = document.Sections[0];
        Assert.Equal(Mdom.Orientation.Landscape, section.PageSetup.Orientation);
        var tables = Body(document).OfType<Mdom.Tables.Table>().ToList();
        Assert.Equal(3, tables.Count);

        var head = Rows(tables[0])[0];
        Assert.Equal("SỞ Y TẾ GIẢ LẬP\nBỆNH VIỆN GIẢ LẬP", head[1]);
        Assert.Equal("Mã số: CT-ĐHTG/BM01\nLần ban hành: 03\nNgày hiệu lực: 16-06-2026", head[2]);

        var paragraphs = Body(document).OfType<Mdom.Paragraph>().Select(p => TextOf(p.Elements)).ToList();
        Assert.Contains("SỔ THEO DÕI KẾT QUẢ NỘI KIỂM ĐƯỜNG HUYẾT TẠI GIƯỜNG\nPOCT INTERNAL QUALITY CONTROL LOG", paragraphs);
        Assert.Contains("KHOA/ PHÒNG THỰC HIỆN/ DEPARTMENT/UNIT: KHOA NỘI A (GIẢ LẬP)", paragraphs);
        Assert.Contains("Tháng/ Month: 11/2026\tSeri máy/ Serial Number: Máy đo A (giả lập) GL-0001", paragraphs);

        var materials = Rows(tables[1]);
        Assert.Equal(["Que thử/\nTest Strips", "", "Số lô/ Lot Number", "Ngày hết hạn/ Expiration Date", "Ngày mở hộp/ Date Opened", ""], materials[0]);
        Assert.Equal(["QT-01 - QT-02", "30/06/2027 - 30/06/2027", "20/10/2026 - 20/10/2026"], materials[1][2..5]);
        Assert.Equal(["Dung dịch chứng/\nControl Solution", "Level 1", "Số lô/ Lot Number", "Ngày hết hạn/ Expiration Date", "Ngày mở hộp/ Date Opened", "Ngưỡng chấp nhận/ Acceptable Range"], materials[2]);
        Assert.Equal(["DC-01", "30/06/2027", "20/10/2026", "40-70 mg/dL"], materials[3][2..6]);
        Assert.Equal("Level 2", materials[4][1]);
        Assert.Equal("100-140 mg/dL", materials[5][5]);
        Assert.Equal(3, tables[1].Rows[2].Cells[0].MergeDown);
    }

    [Fact]
    public void Days_OneRowPerDay_WithEvaluation_Repeat_Staff_AndReview()
    {
        var days = MigraDocQcLogPdfRenderer.Build(Document()).Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().Last();
        var rows = Rows(days);

        Assert.True(days.Rows[0].HeadingFormat && days.Rows[1].HeadingFormat);
        Assert.Equal("Nhân viên Khoa HSH xem xét/\nReviewed by Hematology & Biochemistry Staff", rows[0][9]);
        Assert.Equal(["Level 1", "Level 2", "", "", "Level 1", "Level 2"], rows[1][1..7]);
        // 30 ngày + một dòng kết quả đã hủy.
        Assert.Equal(2 + 30 + 1, rows.Count);
        Assert.Equal(MigraDocQcLogPdfRenderer.ContentWidthMm, MigraDocQcLogPdfRenderer.DayColumnsMm.Sum());
        Assert.Equal(MigraDocQcLogPdfRenderer.ContentWidthMm, MigraDocQcLogPdfRenderer.MaterialColumnsMm.Sum());

        Assert.Equal(["01/11/2026", "55", "120", "Đạt/ Pass", "", "", "", "", "Điều dưỡng A (giả lập)", "Đã kiểm - ĐẠT\nKỹ thuật viên HSH (giả lập)"], rows[2]);
        Assert.Equal(
            ["02/11/2026", "80", "120", "Không đạt/ Fail", "Lọ dung dịch chứng để ngoài tủ mát", "60", "118", "Đạt/ Pass", "Điều dưỡng A (giả lập)\n(chạy lại: Điều dưỡng B (giả lập))", ""],
            rows[3]);
        Assert.Equal(["06/11/2026", "", "", "", "", "", "", "", "", "Đã kiểm - Không có QC ngày này\nKỹ thuật viên HSH (giả lập)"], rows[8]);
        Assert.Equal("", rows[^1][9]);
    }

    [Fact]
    public void CancelledRuns_AndReturnedReviews_StayPrinted_StruckThroughInGrey_WithTheReason()
    {
        var document = MigraDocQcLogPdfRenderer.Build(Document());
        var days = document.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().Last();
        var rows = Rows(days);

        // Ngày 3: dòng kết quả đã hủy (gạch ngang, xám) đứng trước kết quả điều chỉnh.
        var cancelled = rows[4];
        Assert.Equal("03/11/2026", cancelled[0]);
        Assert.Equal(MigraDocQcLogPdfRenderer.Struck("212"), cancelled[2]);
        Assert.Equal(("55", "212", "Không đạt/ Fail"), (Plain(cancelled[1]), Plain(cancelled[2]), Plain(cancelled[3])));
        Assert.EndsWith("Đã hủy: Ghi nhầm kết quả mức 2 (Điều dưỡng A (giả lập))", cancelled[4], StringComparison.Ordinal);
        Assert.Equal(new Mdom.Color(0x80, 0x80, 0x80).RGB, days.Rows[4].Format.Font.Color.RGB);
        Assert.Equal(["03/11/2026", "55", "121", "Đạt/ Pass"], rows[5][..4]);
        Assert.DoesNotContain(MigraDocQcLogPdfRenderer.StrikeMark, string.Concat(rows[5]));

        // Ngày 5: lần xem xét đã trả lại vẫn in (gạch ngang) kèm lý do, rồi lần xem xét đang có hiệu lực và ghi chú.
        var review = rows[7][9];
        Assert.StartsWith(MigraDocQcLogPdfRenderer.Struck("Đã kiểm - KHÔNG ĐẠT, KHÔNG CHẠY LẠI QC"), review, StringComparison.Ordinal);
        Assert.Contains("Trả lại: Chọn nhầm kết quả xem xét", review, StringComparison.Ordinal);
        Assert.EndsWith("Đã kiểm - ĐẠT\nKỹ thuật viên HSH (giả lập)\nGhi chú: Đã kiểm lại", review, StringComparison.Ordinal);

        // Khoảng trắng không gạch.
        Assert.Equal("4\u03365\u0336 7\u0336", MigraDocQcLogPdfRenderer.Struck("45 7"));
    }

    [Fact]
    public void LateRuns_AreMarked_WithAFootnote()
    {
        var withLate = MigraDocQcLogPdfRenderer.Build(Document());
        var rows = Rows(withLate.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().Last());
        Assert.Equal("04/11/2026*", rows[6][0]);
        Assert.Contains("* Kết quả ghi sau ngày thực hiện (ghi bù).", Body(withLate).OfType<Mdom.Paragraph>().Select(p => TextOf(p.Elements)));

        var onTime = MigraDocQcLogPdfRenderer.Build(Document(Book([DayOf(1, Run(1, 55, 120))]))).Sections[0].Elements.Cast<Mdom.DocumentObject>();
        Assert.DoesNotContain(onTime.OfType<Mdom.Paragraph>(), p => TextOf(p.Elements).StartsWith('*'));
    }

    [Fact]
    public void FullMonth_WithLongText_StaysInsideTheLandscapePages()
    {
        var longReason = string.Join(' ', Enumerable.Repeat("Nguyên nhân dài (giả lập)", 18));
        var days = Enumerable.Range(1, 30).Select(d => DayOf(
            d,
            Run(d, 80, 120, reason: longReason, repeat1: 60, repeat2: 118),
            Review(QcReviewOutcome.FailedRepeatedAfterReminder, note: "Đã nhắc khoa chạy lại, theo dõi tiếp (giả lập)"),
            cancelled: d % 3 == 0 ? [Run(d, 81, 121, cancelReason: longReason)] : null)).ToList();
        var document = MigraDocQcLogPdfRenderer.Build(Document(Book(days)));

        _ = new MigraDocQcLogPdfRenderer(); // cài bộ phông nhúng
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var formatted = renderer.FormattedDocument;
        Assert.NotNull(formatted);
        Assert.True(formatted.PageCount >= 2);
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
    public void Render_ProducesAPdf()
    {
        var bytes = new MigraDocQcLogPdfRenderer().Render(Document());
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }
}
