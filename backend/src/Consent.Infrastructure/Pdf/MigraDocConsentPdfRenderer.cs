using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Settings;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Events;
using PdfSharp.Fonts;

namespace Consent.Infrastructure.Pdf;

/// <summary>
/// Dựng PDF phiếu bằng MigraDoc (MIT) theo bố cục văn bản hành chính: cơ quan chủ quản và tên bệnh viện bên trái,
/// quốc hiệu – tiêu ngữ bên phải, tên phiếu, thông tin người bệnh, nội dung theo mẫu, ngày tháng và khối chữ ký.
/// Phông Tinos được nhúng vào file nên hiển thị đúng tiếng Việt trên mọi máy.
/// </summary>
internal sealed class MigraDocConsentPdfRenderer : IConsentPdfRenderer
{
    /// <summary>Ô đã chọn có dấu X bên trong, như đánh dấu trên phiếu giấy (bệnh viện yêu cầu 15/11/2026).</summary>
    internal const string Checked = "☒";
    internal const string Unchecked = "☐";

    /// <summary>
    /// Ô đánh dấu ☒/☐, in bằng font DejaVu Sans nhúng sẵn (Tinos không có hai ký tự này), không đậm, không nghiêng, màu
    /// đen; cỡ 10 cho ô cao bằng ô vuông cũ (gần bằng chữ hoa cỡ 12).
    /// </summary>
    private static void AddBox(Paragraph paragraph, bool selected)
    {
        var box = paragraph.AddFormattedText(selected ? Checked : Unchecked);
        box.FontName = EmbeddedFontResolver.BoxFamilyName;
        box.Size = 10;
        box.Bold = false;
        box.Italic = false;
        box.Color = Colors.Black;
    }

    private const string Blank = "……………………";

    /// <summary>Khoảng cách giữa các phần in chung một dòng (lựa chọn, trường "sameLine"); khoảng trắng không ngắt để không dính nhau.</summary>
    private const string InlineGap = "\u00A0\u00A0\u00A0 ";
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);

    /// <summary>Màu phần tiếng Anh của mẫu song ngữ (in nghiêng; tiếng Việt giữ màu đen, chữ đứng).</summary>
    internal static readonly Color English = new(0x00, 0x70, 0xC0);

    /// <summary>Màu lời xác nhận của người ký ("Tôi đã nghe, hiểu và đồng ý"): đỏ, chữ đậm nghiêng.</summary>
    internal static readonly Color Statement = new(0xC0, 0x00, 0x00);

    /// <summary>Màu dòng ghi chú bắt đầu bằng "!" dưới tên ô ký, như chữ đỏ "đã đối chiếu…" của bản giấy TTNB.</summary>
    internal static readonly Color Note = new(0xFF, 0x00, 0x00);
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock FontGate = new();

    public MigraDocConsentPdfRenderer() => EnsureFontResolver();

    public byte[] Render(ConsentPdfDocument document)
    {
        var renderer = new PdfDocumentRenderer { Document = Build(document) };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
    }

    /// <summary>Dựng bố cục phiếu (MigraDoc) trước khi in ra PDF; test xem được định dạng từng phần.</summary>
    internal static Document Build(ConsentPdfDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var pdf = new Document();
        pdf.Info.Title = $"{document.TemplateCode} – {document.FormNo}";
        pdf.Info.Subject = Flatten(document.Definition.Title);
        DefineStyles(pdf);

        var section = pdf.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromMillimeter(15);
        section.PageSetup.BottomMargin = Unit.FromMillimeter(18);
        section.PageSetup.LeftMargin = Unit.FromMillimeter(25);
        section.PageSetup.RightMargin = Unit.FromMillimeter(18);
        section.PageSetup.HeaderDistance = Unit.FromMillimeter(7);
        section.PageSetup.FooterDistance = Unit.FromMillimeter(8);

        AddPreviewNotice(section, document);
        AddDocumentHead(section, document);
        AddTitle(section, document);
        AddPatient(section, document);
        var signing = SigningLayout.Create(document);
        foreach (var block in document.Definition.Sections)
        {
            AddSection(section, block, document, signing);
        }

        AddSignatures(section, document, signing);
        AddFootnote(section, document.Definition);
        AddFooter(section, document);
        return pdf;
    }

    /// <summary>
    /// Cỡ chữ lớn nhất (từ <paramref name="maxSize"/>, giảm dần 0,5) để mỗi dòng nằm gọn một dòng trong ô rộng
    /// <paramref name="widthMm"/> (trừ lề trong của ô): tên phiếu, tên đơn vị ở đầu phiếu không xuống dòng giữa chừng.
    /// </summary>
    private static double FitFontSize(IReadOnlyList<(string Text, XFontStyleEx Style)> lines, double widthMm, double maxSize, double minSize)
    {
        EnsureFontResolver();
        using var measure = XGraphics.CreateMeasureContext(
            new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards, new RenderEvents());
        var available = XUnit.FromMillimeter(widthMm - 3).Point;
        for (var size = maxSize; size > minSize; size -= 0.5)
        {
            if (lines.All(line => measure.MeasureString(line.Text, new XFont(EmbeddedFontResolver.FamilyName, size, line.Style)).Width <= available))
            {
                return size;
            }
        }

        return minSize;
    }

    private static void EnsureFontResolver()
    {
        lock (FontGate)
        {
            if (GlobalFontSettings.FontResolver is not EmbeddedFontResolver)
            {
                GlobalFontSettings.FontResolver = new EmbeddedFontResolver();
            }
        }
    }

    private static void DefineStyles(Document pdf)
    {
        var normal = pdf.Styles[StyleNames.Normal]!;
        normal.Font.Name = EmbeddedFontResolver.FamilyName;
        normal.Font.Size = 12;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(2);
        normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
        normal.ParagraphFormat.LineSpacing = 1.0;
    }

    private static void AddPreviewNotice(Section section, ConsentPdfDocument document)
    {
        if (document.IsFinal)
        {
            return;
        }

        var notice = section.Headers.Primary.AddParagraph("BẢN XEM TRƯỚC – CHƯA CÓ CHỮ KÝ");
        notice.Format.Alignment = ParagraphAlignment.Right;
        notice.Format.Font.Size = 9;
        notice.Format.Font.Italic = true;
        notice.Format.Font.Color = Muted;
    }

    private static void AddDocumentHead(Section section, ConsentPdfDocument document)
    {
        if (document.Definition.DocumentControl is { } control)
        {
            AddControlledDocumentHead(section, document, control);
            return;
        }

        var table = section.AddTable();
        // Có logo: cột logo bên trái, tên đơn vị và quốc hiệu thu hẹp lại; tổng vẫn 167 mm (lề A4).
        var logo = document.Logo is { } candidate && CanDecode(candidate) ? candidate : null;
        if (logo is not null)
        {
            table.AddColumn(Unit.FromMillimeter(LogoColumnMm));
        }

        table.AddColumn(Unit.FromMillimeter(logo is null ? 72 : 60));
        table.AddColumn(Unit.FromMillimeter(logo is null ? 95 : 167 - 60 - LogoColumnMm));
        var row = table.AddRow();
        // Canh trên: tên đơn vị, quốc hiệu và logo cùng bắt đầu ở một đường.
        row.VerticalAlignment = VerticalAlignment.Top;
        // Có logo thì cột chữ hẹp hơn: chữ nhỏ hơn một cỡ để tên đơn vị và quốc hiệu vẫn nằm trên một dòng.
        var headSize = logo is null ? 11 : 10;
        var textColumn = 0;
        if (logo is not null)
        {
            AddLogo(row.Cells[0], logo, LogoColumnMm - 2, LogoMaxHeightMm, alignWithTextOfSize: headSize);
            textColumn = 1;
        }

        var left = row.Cells[textColumn].AddParagraph();
        left.Format.Alignment = ParagraphAlignment.Center;
        left.Format.Font.Size = headSize;
        left.AddText(document.Hospital.ParentAgency.ToUpper(Vietnamese));
        left.AddLineBreak();
        // Tên bệnh viện in đậm, không gạch dưới (yêu cầu của bệnh viện 15/11/2026); quốc hiệu bên phải vẫn gạch dưới.
        left.AddFormattedText(document.Hospital.Name.ToUpper(Vietnamese), TextFormat.Bold);
        // Mẫu có "hideDepartment" (bản giấy không có dòng khoa, ví dụ TTNB-NGOAITRU, TUVAN-CONGTT) thì bỏ dòng này.
        if (!document.Definition.HideDepartment && !string.IsNullOrWhiteSpace(document.DepartmentName))
        {
            left.AddLineBreak();
            left.AddText($"Khoa/phòng: {document.DepartmentName}");
        }

        AddNationalMotto(row.Cells[textColumn + 1], headSize);

        var meta = section.AddParagraph();
        meta.Format.SpaceBefore = Unit.FromMillimeter(3);
        meta.Format.Font.Size = 10;
        meta.Format.Font.Color = Muted;
        meta.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
        meta.AddText($"MS: {document.TemplateCode} (phiên bản {document.TemplateVersionNo})");
        meta.AddTab();
        meta.AddText($"Số phiếu: {document.FormNo}");
    }

    /// <summary>Quốc hiệu, tiêu ngữ: in đậm, tiêu ngữ gạch dưới theo thể thức.</summary>
    private static void AddNationalMotto(Cell cell, double size)
    {
        var motto = cell.AddParagraph();
        motto.Format.Alignment = ParagraphAlignment.Center;
        motto.Format.Font.Size = size;
        motto.AddFormattedText("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", TextFormat.Bold);
        motto.AddLineBreak();
        motto.AddFormattedText("Độc lập - Tự do - Hạnh phúc", TextFormat.Bold).Underline = Underline.Single;
    }

    // Cột giữa đủ rộng để quốc hiệu cỡ 11 nằm trên một dòng; ô mã tài liệu chữ cỡ 7,5 như bản giấy.
    private const double ControlLogoColumnMm = 38;
    private const double ControlBoxColumnMm = 43;

    // Tên phiếu ở giữa đầu phiếu (bản giấy TTNB, lề hẹp hơn lề của phiếu điện tử): cột logo hẹp, tên phiếu cỡ 12 và mã tài
    // liệu cỡ 9 để cả ba vừa một hàng trong 167 mm mà không xuống dòng (bản giấy: cỡ 14 và 10 trên trang rộng 187 mm).
    private const double TitleHeadLogoColumnMm = 21;
    private const double TitleHeadBoxColumnMm = 46;

    // Tên đơn vị trên logo (bản giấy BM10, quyết định 43): cột trái đủ rộng cho dòng "SỞ Y TẾ THÀNH PHỐ …" cỡ 9, ô mã tài liệu
    // hẹp hơn một chút; tên phiếu ở giữa còn 81 mm.
    private const double UnitHeadLogoColumnMm = 46;
    private const double UnitHeadBoxColumnMm = 40;
    private const double UnitHeadLogoMaxWidthMm = 26;

    /// <summary>
    /// Đầu phiếu như bản giấy có mã tài liệu (<c>documentControl</c>): logo bên trái, quốc hiệu ở giữa (hoặc tên phiếu, với
    /// <c>titleInHead</c>), mã số – lần ban hành – ngày hiệu lực bên phải. Không in dòng khoa và dòng "MS … Số phiếu" (số phiếu
    /// in ở chân trang). Chưa tải logo thì ô trái in tên đơn vị để phiếu vẫn có tên bệnh viện.
    /// </summary>
    private static void AddControlledDocumentHead(Section section, ConsentPdfDocument document, TemplateDocumentControl control)
    {
        var titleInHead = control.TitleInHead;
        var unitInHead = titleInHead && control.UnitInHead;
        var logoColumn = unitInHead ? UnitHeadLogoColumnMm : titleInHead ? TitleHeadLogoColumnMm : ControlLogoColumnMm;
        var boxColumn = unitInHead ? UnitHeadBoxColumnMm : titleInHead ? TitleHeadBoxColumnMm : ControlBoxColumnMm;
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(logoColumn));
        table.AddColumn(Unit.FromMillimeter(167 - logoColumn - boxColumn));
        table.AddColumn(Unit.FromMillimeter(boxColumn));
        var row = table.AddRow();
        // Canh trên: cạnh trên logo thẳng cạnh trên chữ quốc hiệu (bệnh viện yêu cầu 15/11/2026), ô mã tài liệu cũng từ đó.
        // Tên phiếu ở giữa thì logo, tên phiếu, mã tài liệu canh giữa theo chiều dọc như bản giấy TTNB.
        row.VerticalAlignment = titleInHead ? VerticalAlignment.Center : VerticalAlignment.Top;
        if (titleInHead)
        {
            // Khoảng trống giữa đầu phiếu và dòng thông tin đầu tiên (thay cho khoảng trên của tên phiếu).
            row.BottomPadding = Unit.FromMillimeter(4);
        }

        if (unitInHead)
        {
            // Như bản giấy BM10: cơ quan chủ quản và tên bệnh viện phía trên logo, canh giữa cột.
            var agency = document.Hospital.ParentAgency.ToUpper(Vietnamese);
            var hospital = document.Hospital.Name.ToUpper(Vietnamese);
            var unit = row.Cells[0].AddParagraph();
            unit.Format.Alignment = ParagraphAlignment.Center;
            unit.Format.Font.Size = FitFontSize([(agency, XFontStyleEx.Regular), (hospital, XFontStyleEx.Bold)], logoColumn, 9, 6.5);
            unit.Format.SpaceAfter = Unit.FromPoint(2);
            unit.AddText(agency);
            unit.AddLineBreak();
            unit.AddFormattedText(hospital, TextFormat.Bold);
            if (document.Logo is { } unitLogo && CanDecode(unitLogo))
            {
                AddLogo(row.Cells[0], unitLogo, UnitHeadLogoMaxWidthMm, LogoMaxHeightMm, alignWithTextOfSize: 0, center: true);
            }
        }
        else if (document.Logo is { } logo && CanDecode(logo))
        {
            AddLogo(row.Cells[0], logo, logoColumn - 2, LogoMaxHeightMm, alignWithTextOfSize: titleInHead ? 0 : 11);
        }
        else if (titleInHead)
        {
            // Cột logo hẹp: chưa tải logo thì chỉ in tên bệnh viện, chữ nhỏ.
            var name = row.Cells[0].AddParagraph();
            name.Format.Alignment = ParagraphAlignment.Center;
            name.Format.Font.Size = 7;
            name.AddFormattedText(document.Hospital.Name.ToUpper(Vietnamese), TextFormat.Bold);
        }
        else
        {
            var name = row.Cells[0].AddParagraph();
            name.Format.Alignment = ParagraphAlignment.Center;
            name.Format.Font.Size = 8;
            name.AddText(document.Hospital.ParentAgency.ToUpper(Vietnamese));
            name.AddLineBreak();
            name.AddFormattedText(document.Hospital.Name.ToUpper(Vietnamese), TextFormat.Bold);
        }

        if (titleInHead)
        {
            AddHeadTitle(row.Cells[1], document.Definition.Title, 167 - logoColumn - boxColumn);
        }
        else
        {
            AddNationalMotto(row.Cells[1], 11);
        }

        var box = row.Cells[2].AddParagraph();
        // Ô mã tài liệu hẹp (tên đơn vị trên logo) thì chữ nhỏ như bản giấy BM10.
        box.Format.Font.Size = titleInHead && !unitInHead ? 9 : 7.5;
        AddBilingual(box, $"{control.CodeLabel ?? TemplateDocumentControl.DefaultCodeLabel}: {control.Code}");
        box.AddLineBreak();
        AddBilingual(box, $"{control.IssueNoLabel ?? TemplateDocumentControl.DefaultIssueNoLabel}: {control.IssueNo}");
        box.AddLineBreak();
        AddBilingual(box, $"{control.EffectiveDateLabel ?? TemplateDocumentControl.DefaultEffectiveDateLabel}: {control.EffectiveDate}");
    }

    /// <summary>
    /// Tên phiếu ở giữa đầu phiếu (<c>titleInHead</c>, bản giấy TTNB): chữ hoa, đậm, canh giữa; dòng tiếng Anh màu xanh, chữ
    /// đứng như bản giấy.
    /// </summary>
    private static void AddHeadTitle(Cell cell, string title, double widthMm)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        var lines = title.Split('\n').Select(line => line.Trim()).ToList();
        // Cỡ 12 như bản giấy TTNB; tên phiếu dài (BM10, QĐ-VHTT-BM02) thu nhỏ vừa cột để mỗi dòng không bị ngắt.
        paragraph.Format.Font.Size = FitFontSize(
            [.. lines.Select(line => IsTitleSubLine(line) ? (line, XFontStyleEx.Italic) : (line.ToUpper(Vietnamese), XFontStyleEx.Bold))],
            widthMm,
            12,
            8);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                paragraph.AddLineBreak();
            }

            // Dòng phụ trong ngoặc, ví dụ "(Outpatient – Department of Medical Examination)" của bản giấy BM10: chữ thường, nghiêng.
            var subLine = IsTitleSubLine(lines[i]);
            foreach (var segment in BilingualText.Split(lines[i], wholeLines: true))
            {
                if (segment.Text == "\n")
                {
                    paragraph.AddLineBreak();
                    continue;
                }

                var run = subLine
                    ? paragraph.AddFormattedText(segment.Text, segment.IsEnglish ? TextFormat.Italic : TextFormat.Bold | TextFormat.Italic)
                    : paragraph.AddFormattedText(segment.Text.ToUpper(Vietnamese), TextFormat.Bold);
                if (segment.IsEnglish)
                {
                    run.Color = English;
                }
            }
        }
    }

    /// <summary>Dòng trong ngoặc của tiêu đề, ví dụ "(Về chế độ thanh toán …)": dòng phụ, không viết hoa.</summary>
    private static bool IsTitleSubLine(string line) => line.Length > 2 && line[0] == '(' && line[^1] == ')';

    private static void AddTitle(Section section, ConsentPdfDocument document)
    {
        if (document.Definition.DocumentControl?.TitleInHead != true)
        {
            AddTitleLines(section, document);
        }

        if (document.Definition.Subtitle is { } subtitle)
        {
            var sub = section.AddParagraph();
            AddBilingual(sub, subtitle, wholeLines: true);
            sub.Format.Alignment = ParagraphAlignment.Center;
            sub.Format.Font.Italic = true;
            sub.Format.SpaceAfter = Unit.FromMillimeter(4);
        }
    }

    /// <summary>Tên phiếu dưới đầu phiếu: chữ hoa, đậm, cỡ 14; dòng trong ngoặc là dòng phụ.</summary>
    private static void AddTitleLines(Section section, ConsentPdfDocument document)
    {
        var title = section.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.SpaceBefore = Unit.FromMillimeter(4);
        title.Format.SpaceAfter = Unit.FromMillimeter(document.Definition.Subtitle is null ? 4 : 1);
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        var lines = document.Definition.Title.Split('\n').Select(line => line.Trim()).ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                title.AddLineBreak();
            }

            if (IsTitleSubLine(lines[i]))
            {
                // Như bản giấy: dòng phụ in nghiêng; tiếng Việt đậm cỡ 12, tiếng Anh màu xanh không đậm cỡ 11.
                foreach (var segment in BilingualText.Split(lines[i], wholeLines: true))
                {
                    var run = title.AddFormattedText(segment.Text);
                    run.Size = segment.IsEnglish ? 11 : 12;
                    run.Italic = true;
                    run.Bold = !segment.IsEnglish;
                    if (segment.IsEnglish)
                    {
                        run.Color = English;
                    }
                }
            }
            else
            {
                AddBilingual(title, lines[i], upper: true, wholeLines: true);
            }
        }
    }

    /// <summary>Nhãn mục "I. Thông tin người bệnh": chỉ tiếng Việt, hoặc kèm tiếng Anh với mẫu song ngữ (<c>bilingual</c>).</summary>
    private sealed record PatientLabels(
        string Title,
        string FullName,
        string Gender,
        string DateOfBirth,
        string PatientCode,
        string CitizenId,
        string Phone,
        string Address,
        string Representative,
        string Relationship)
    {
        public static readonly PatientLabels VietnameseOnly = new(
            "I. Thông tin người bệnh", "Họ và tên: ", "Giới tính: ", "Ngày sinh: ", "Mã người bệnh: ", "Số CCCD: ", "Điện thoại: ",
            "Địa chỉ: ", "Người ký thay mặt người bệnh: ", "Quan hệ: ");

        // Cột phải rộng 67 mm: nhãn tiếng Anh ở cột phải viết gọn ("Phone", "ID No.") để số không bị xuống dòng.
        public static readonly PatientLabels WithEnglish = new(
            "I. Thông tin người bệnh (Patient information)",
            "Họ và tên (Full name): ",
            "Giới tính (Gender): ",
            "Ngày sinh (Date of birth): ",
            "Mã người bệnh (Patient ID): ",
            "Số CCCD (ID No.): ",
            "Điện thoại (Phone): ",
            "Địa chỉ (Address): ",
            "Người ký thay mặt người bệnh (Signing on behalf of the patient): ",
            "Quan hệ (Relationship): ");
    }

    private static void AddPatient(Section section, ConsentPdfDocument document)
    {
        var bilingual = document.Definition.Bilingual;
        var labels = bilingual ? PatientLabels.WithEnglish : PatientLabels.VietnameseOnly;
        if (document.Definition.PatientLines is { } lines)
        {
            AddPatientLines(section, document, lines);
        }
        else
        {
            AddSectionTitle(section, labels.Title);
            var patient = document.Patient;
            var age = VietnameseLabels.Age(patient.DateOfBirth, document.DocumentDate);
            var dateOfBirth = $"{patient.DateOfBirth:dd/MM/yyyy} ({age} tuổi)";

            AddTwoColumns(
                section, labels.FullName, patient.FullName.ToUpper(Vietnamese), labels.Gender, VietnameseLabels.Gender(patient.Gender), boldLeft: true);
            if (bilingual)
            {
                // Nhãn song ngữ dài hơn: mã người bệnh sang cột trái, cột phải chỉ để điện thoại và số CCCD (độ dài cố định).
                AddTwoColumns(section, labels.DateOfBirth, dateOfBirth, labels.Phone, patient.PhoneNumber);
                AddTwoColumns(section, labels.PatientCode, patient.PatientCode, labels.CitizenId, patient.CitizenId);
            }
            else
            {
                AddTwoColumns(section, labels.DateOfBirth, dateOfBirth, labels.PatientCode, patient.PatientCode);
                AddTwoColumns(section, labels.CitizenId, patient.CitizenId, labels.Phone, patient.PhoneNumber);
            }

            AddLabelValue(section.AddParagraph(), labels.Address, patient.Address);
        }

        if (document.Representative is { } representative)
        {
            var signer = section.AddParagraph();
            AddLabelValue(signer, labels.Representative, representative.FullName.ToUpper(Vietnamese), bold: true);
            AddLabelValue(signer, $" – {labels.Relationship}", VietnameseLabels.Relationship(representative.Relationship));
            AddLabelValue(signer, $" – {labels.CitizenId}", representative.CitizenId);
        }
    }

    /// <summary>
    /// Mục thứ hai của dòng thông tin người bệnh bắt đầu ở 76 mm như bản giấy BM02, đủ chỗ cho "Giới tính/ Gender: ☐ Nam/Male
    /// ☐ Nữ/Female" trên một dòng.
    /// </summary>
    private const double PatientLineSecondItemMm = 76;

    /// <summary>Cột số thứ tự của dòng thông tin người bệnh đánh số (<c>numberedPatientLines</c>), thụt treo như danh sách.</summary>
    private const double LineNumberColumnMm = 7;

    /// <summary>
    /// Dòng thông tin người bệnh theo bản giấy của mẫu (<c>patientLines</c>), thay cho mục "I. Thông tin người bệnh". Mẫu đánh
    /// số dòng (bản giấy TTNB) thì chữ mọi dòng thẳng cột sau số thứ tự; dòng phụ (<c>indent</c>) không có số.
    /// </summary>
    private static void AddPatientLines(Section section, ConsentPdfDocument document, IReadOnlyList<IReadOnlyList<PatientLineItem>> lines)
    {
        var numbered = document.Definition.NumberedPatientLines;
        var number = 0;
        foreach (var items in lines)
        {
            var line = section.AddParagraph();
            // Mẫu đánh số có nhiều dòng (TTNB 18 dòng): khoảng cách dòng gần như bản giấy để phiếu và khối chữ ký vừa một trang.
            line.Format.SpaceAfter = Unit.FromPoint(numbered ? 1 : 4);
            var indent = items[0].Indent;
            if (numbered || indent)
            {
                line.Format.LeftIndent = Unit.FromMillimeter(LineNumberColumnMm);
            }

            if (numbered && !indent)
            {
                line.Format.FirstLineIndent = Unit.FromMillimeter(-LineNumberColumnMm);
                line.Format.TabStops.AddTabStop(Unit.FromMillimeter(LineNumberColumnMm));
                line.AddText($"{++number}.");
                line.AddTab();
            }

            line.Format.TabStops.AddTabStop(Unit.FromMillimeter(PatientLineSecondItemMm));
            for (var i = 0; i < items.Count; i++)
            {
                if (i > 0)
                {
                    line.AddTab();
                }

                AddPatientLineItem(line, items[i], document);
            }
        }
    }

    private static void AddPatientLineItem(Paragraph line, PatientLineItem item, ConsentPdfDocument document)
    {
        if (item.Field is { } key)
        {
            // Trường của phiếu (ví dụ số thẻ BHYT): in ở đây thay cho chỗ của nó trong thân phiếu; đang ẩn thì để chấm trống.
            var block = document.Definition.InputBlocks.First(b => b.Key == key);
            var fieldLabel = item.Label ?? block.Label ?? string.Empty;
            if (block.Type is BlockTypes.Choice or BlockTypes.MultiChoice)
            {
                AddChoiceLine(
                    line, fieldLabel, block, document.Data[key], item.Other is { } other ? document.Data[other] : null, item.Other is not null);
            }
            else if (item.Options is { Count: > 0 } choices)
            {
                AddTextAsBoxes(line, fieldLabel, choices, FormatValue(block.Type, document.Data[key]));
            }
            else
            {
                AddLabelValue(line, $"{fieldLabel}: ", FormatValue(block.Type, document.Data[key]));
            }

            return;
        }

        var patient = document.Patient;
        var label = $"{item.Label ?? PatientLineSources.DefaultLabels[item.Patient!]}: ";
        switch (item.Patient)
        {
            case PatientLineSources.FullName:
                AddLabelValue(line, label, patient.FullName.ToUpper(Vietnamese), bold: true);
                break;
            case PatientLineSources.DateOfBirth:
                AddLabelValue(line, label, patient.DateOfBirth.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                break;
            case PatientLineSources.Gender when item.Options is [var male, var female]:
                // Như bản giấy: "Giới tính/ Gender: ☒ Nam/Male ☐ Nữ/Female".
                AddBilingual(line, label);
                AddBox(line, patient.Gender == Gender.Male);
                line.AddText(" ");
                AddBilingual(line, male);
                line.AddText(InlineGap);
                AddBox(line, patient.Gender == Gender.Female);
                line.AddText(" ");
                AddBilingual(line, female);
                break;
            case PatientLineSources.Gender:
                AddLabelValue(line, label, VietnameseLabels.Gender(patient.Gender));
                break;
            case PatientLineSources.PatientCode:
                AddLabelValue(line, label, patient.PatientCode);
                break;
            case PatientLineSources.CitizenId:
                AddLabelValue(line, label, patient.CitizenId);
                break;
            case PatientLineSources.PhoneNumber:
                AddLabelValue(line, label, patient.PhoneNumber);
                break;
            default:
                AddLabelValue(line, label, patient.Address);
                break;
        }
    }

    /// <summary>
    /// Câu hỏi chọn trên một dòng như bản giấy: "Tôn giáo (Religion): ☒ Phật giáo (Buddhism) ☐ … ☐ Khác (Other): …", với
    /// trường chữ <paramref name="other"/> in sau các ô (để chấm khi trống).
    /// </summary>
    private static void AddChoiceLine(Paragraph line, string label, TemplateBlock block, JsonNode? value, JsonNode? other, bool hasOther)
    {
        AddBilingual(line, LabelWithSeparator(label));
        var selected = SelectedValues(value);
        var options = block.Options ?? [];
        for (var i = 0; i < options.Count; i++)
        {
            if (i > 0)
            {
                line.AddText(InlineGap);
            }

            AddBox(line, selected.Contains(options[i].Value));
            line.AddText(" ");
            AddBilingual(line, options[i].Label);
        }

        if (hasOther)
        {
            AddLabelValue(line, ": ", FormatValue(BlockTypes.Text, other));
        }
    }

    /// <summary>
    /// Trường chữ in thành các ô như bản giấy (ví dụ dân tộc "☐ Kinh ☐ Khác, ghi rõ (Other, specify): …"): giá trị trùng phần
    /// tiếng Việt của một lựa chọn (trừ lựa chọn cuối) thì đánh dấu ô đó; giá trị khác thì đánh dấu ô cuối và in giá trị sau ô.
    /// </summary>
    private static void AddTextAsBoxes(Paragraph line, string label, IReadOnlyList<string> choices, string? value)
    {
        var text = value?.Trim().Normalize();
        var match = -1;
        for (var i = 0; i < choices.Count - 1 && match < 0 && !string.IsNullOrEmpty(text); i++)
        {
            if (string.Equals(VietnamesePart(choices[i]), text, StringComparison.OrdinalIgnoreCase))
            {
                match = i;
            }
        }

        var isOther = match < 0 && !string.IsNullOrEmpty(text);
        AddBilingual(line, LabelWithSeparator(label));
        for (var i = 0; i < choices.Count; i++)
        {
            if (i > 0)
            {
                line.AddText(InlineGap);
            }

            AddBox(line, i == match || (isOther && i == choices.Count - 1));
            line.AddText(" ");
            AddBilingual(line, choices[i]);
        }

        AddLabelValue(line, ": ", isOther ? text : null);
    }

    /// <summary>Phần tiếng Việt của một nhãn song ngữ ("Nam (Male)" → "Nam").</summary>
    private static string VietnamesePart(string label) =>
        string.Concat(BilingualText.Split(label).Where(s => !s.IsEnglish).Select(s => s.Text)).Trim().Normalize();

    /// <summary>Nhãn kèm dấu ":"; câu hỏi (kết thúc bằng "?" hoặc "?)") chỉ thêm khoảng trắng.</summary>
    private static string LabelWithSeparator(string label)
    {
        var trimmed = label.TrimEnd(':', ' ');
        return trimmed.EndsWith('?') || trimmed.EndsWith("?)", StringComparison.Ordinal) ? $"{trimmed} " : $"{trimmed}: ";
    }

    private static HashSet<string> SelectedValues(JsonNode? value) => value switch
    {
        JsonArray array => array.Select(v => v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
        JsonValue single when single.TryGetValue<string>(out var text) => [text],
        _ => new HashSet<string>(StringComparer.Ordinal),
    };

    /// <summary>Trường của phiếu đã in ở dòng thông tin người bệnh (kể cả trường "other"), không in lại ở thân phiếu.</summary>
    private static HashSet<string> FieldsInPatientLines(TemplateDefinition definition) =>
        definition.PatientLines?.SelectMany(line => line).SelectMany(item => new[] { item.Field, item.Other }).OfType<string>()
            .ToHashSet(StringComparer.Ordinal)
        ?? [];

    private static void AddSection(Section section, TemplateSection templateSection, ConsentPdfDocument document, SigningLayout signing)
    {
        // Bản giấy BM10 mỗi phần một trang (quyết định 43).
        if (templateSection.NewPage)
        {
            section.AddPageBreak();
        }

        if (!string.IsNullOrWhiteSpace(templateSection.Title))
        {
            if (templateSection.Banner)
            {
                AddBanner(section, templateSection.Title);
            }
            else
            {
                AddSectionTitle(section, templateSection.Title);
            }
        }

        var printedAbove = FieldsInPatientLines(document.Definition);

        // Dòng "nhãn: giá trị" vừa in; trường có "sameLine" in tiếp trên dòng này thay vì xuống dòng.
        Paragraph? valueLine = null;
        var blocks = templateSection.Blocks;
        for (var index = 0; index < blocks.Count; index++)
        {
            var block = blocks[index];
            if (block is { Type: BlockTypes.Choice, Layout: TemplateBlock.LayoutRow })
            {
                // Các câu "row" liền nhau, cùng số lựa chọn, in chung một bảng kẻ ô.
                var rows = new List<TemplateBlock> { block };
                while (index + 1 < blocks.Count
                       && blocks[index + 1] is { Type: BlockTypes.Choice, Layout: TemplateBlock.LayoutRow } next
                       && next.Options?.Count == block.Options?.Count)
                {
                    rows.Add(next);
                    index++;
                }

                AddRowTable(section, [.. rows.Where(row => IsShown(row, document) && !printedAbove.Contains(row.Key!))], document);
                valueLine = null;
                continue;
            }

            switch (block.Type)
            {
                case BlockTypes.Paragraph:
                    var paragraph = section.AddParagraph();
                    if (block.Heading)
                    {
                        // Tiêu đề mục như bản giấy BM10 (quyết định 43): đậm, canh giữa, chữ hoa, đi liền nội dung bên dưới.
                        AddBilingual(paragraph, block.Text ?? string.Empty, TextFormat.Bold, upper: true);
                        paragraph.Format.Alignment = ParagraphAlignment.Center;
                        paragraph.Format.SpaceBefore = Unit.FromMillimeter(1);
                        paragraph.Format.SpaceAfter = Unit.FromPoint(3);
                        paragraph.Format.KeepWithNext = true;
                    }
                    else
                    {
                        AddBilingual(paragraph, block.Text ?? string.Empty);
                        paragraph.Format.Alignment = ParagraphAlignment.Justify;
                    }

                    valueLine = null;
                    break;
                case BlockTypes.List:
                    AddList(section, block);
                    valueLine = null;
                    break;
                case BlockTypes.Score:
                    AddScore(section, block, document);
                    valueLine = null;
                    break;
                default:
                    // Trường không hiển thị (điều kiện chưa thỏa) hoặc đã in ở dòng thông tin người bệnh thì không in.
                    if (IsShown(block, document) && !printedAbove.Contains(block.Key!))
                    {
                        valueLine = AddField(section, block, document.Data[block.Key!], block.SameLine ? valueLine : null);
                    }

                    break;
            }
        }

        if (templateSection.Signer is { } signer)
        {
            AddSectionSigner(section, document, signing, signer);
        }
    }

    // Dải nền tên phần (bản giấy BM10, màu cam nhạt) và khung lưu ý (bản giấy QĐ-VHTT-BM02, nền xám, viền xanh đậm).
    internal static readonly Color BannerFill = new(0xFB, 0xE4, 0xD5);
    internal static readonly Color BoxFill = new(0xF2, 0xF2, 0xF2);
    private static readonly Color BoxBorder = new(0x2F, 0x54, 0x96);

    /// <summary>Đoạn trống nhỏ trước/sau bảng (bảng không có khoảng trên, khoảng dưới như đoạn văn).</summary>
    private static Paragraph AddGap(Section section, double points)
    {
        var gap = section.AddParagraph();
        gap.Format.Font.Size = points;
        gap.Format.SpaceAfter = 0;
        gap.Format.LineSpacingRule = LineSpacingRule.Exactly;
        gap.Format.LineSpacing = Unit.FromPoint(points);
        return gap;
    }

    /// <summary>
    /// Tên phần trong dải nền màu (<c>banner</c>, quyết định 43): dòng đầu chữ hoa đậm, trừ phần trong ngoặc (ví dụ "(Người bệnh
    /// thực hiện trước khi đến khám)"); các dòng sau theo kiểu song ngữ (tiếng Anh xanh, nghiêng).
    /// </summary>
    private static void AddBanner(Section section, string title)
    {
        // Khoảng cách nhỏ (phần 1 của BM10 gần kín trang: chữ ký trong khung vẫn phải nằm cùng trang).
        AddGap(section, 3).Format.KeepWithNext = true;
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(167));
        var row = table.AddRow();
        row.Shading.Color = BannerFill;
        row.TopPadding = Unit.FromPoint(1);
        row.BottomPadding = Unit.FromPoint(1);
        var paragraph = row.Cells[0].AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        paragraph.Format.SpaceAfter = 0;
        var lines = title.Split('\n').Select(line => line.Trim()).ToList();
        AddBilingual(paragraph, UpperOutsideParentheses(lines[0]), TextFormat.Bold);
        foreach (var line in lines.Skip(1))
        {
            paragraph.AddLineBreak();
            AddBilingual(paragraph, line);
        }

        AddGap(section, 3);
    }

    /// <summary>Viết hoa phần chữ ngoài ngoặc đơn.</summary>
    private static string UpperOutsideParentheses(string text)
    {
        var builder = new StringBuilder(text.Length);
        var depth = 0;
        foreach (var character in text)
        {
            if (character == '(')
            {
                depth++;
            }

            builder.Append(depth == 0 ? char.ToUpper(character, Vietnamese) : character);
            if (character == ')' && depth > 0)
            {
                depth--;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Câu hỏi chọn một in thành bảng kẻ ô (<c>layout: row</c>, quyết định 43): mỗi câu một hàng, cột đầu là câu hỏi, mỗi lựa
    /// chọn một ô có ô đánh dấu, như bảng "Có/Không" của bản giấy BM10.
    /// </summary>
    private static void AddRowTable(Section section, IReadOnlyList<TemplateBlock> rows, ConsentPdfDocument document)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var optionCount = rows[0].Options!.Count;
        var longest = rows.SelectMany(row => row.Options!).Max(option => option.Label.Length);
        // Lựa chọn ngắn ("Có (Yes)", "Không (No)"): ô hẹp như bản giấy; lựa chọn dài chia đều phần còn lại sau cột câu hỏi.
        var optionWidth = longest <= 22 ? 26d : (167d - 70d) / optionCount;
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Format.Font.Size = 11;
        table.AddColumn(Unit.FromMillimeter(167 - (optionWidth * optionCount)));
        for (var c = 0; c < optionCount; c++)
        {
            table.AddColumn(Unit.FromMillimeter(optionWidth));
        }

        foreach (var block in rows)
        {
            var selected = SelectedValues(document.Data[block.Key!]);
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.TopPadding = Unit.FromPoint(1);
            row.BottomPadding = Unit.FromPoint(1);
            var question = row.Cells[0].AddParagraph();
            question.Format.SpaceAfter = 0;
            AddBilingual(question, block.Label ?? string.Empty);
            for (var c = 0; c < optionCount; c++)
            {
                var option = block.Options![c];
                var cell = row.Cells[c + 1].AddParagraph();
                cell.Format.SpaceAfter = 0;
                AddBox(cell, selected.Contains(option.Value));
                cell.AddText(" ");
                AddBilingual(cell, option.Label);
            }
        }

        AddGap(section, 3);
    }

    /// <summary>Trường có giá trị, hoặc không có điều kiện hiển thị (in ô trống để người bệnh thấy đủ nội dung).</summary>
    private static bool IsShown(TemplateBlock block, ConsentPdfDocument document) =>
        block.VisibleWhen is null || document.Data[block.Key!] is not null;

    /// <summary>
    /// In một trường. Trường dạng "nhãn: giá trị" in tiếp trên <paramref name="sameLine"/> nếu có, và trả về dòng đó để
    /// trường sau in tiếp; các kiểu khác trả về <c>null</c>.
    /// </summary>
    private static Paragraph? AddField(Section section, TemplateBlock block, JsonNode? value, Paragraph? sameLine)
    {
        switch (block.Type)
        {
            case BlockTypes.Choice:
            case BlockTypes.MultiChoice:
                // Câu hỏi trên một dòng: trường "sameLine" đứng sau in tiếp, ví dụ "☒ Có, ghi rõ (Yes, specify): …" (bản giấy BM10).
                return AddChoices(section, block, value);
            case BlockTypes.Confirm:
                var confirm = section.AddParagraph();
                confirm.Format.LeftIndent = Unit.FromMillimeter(6);
                confirm.Format.FirstLineIndent = Unit.FromMillimeter(-6);
                AddBox(confirm, value is JsonValue v && v.TryGetValue<bool>(out var flag) && flag);
                confirm.AddTab();
                confirm.Format.TabStops.AddTabStop(Unit.FromMillimeter(6));
                AddBilingual(confirm, block.Label ?? string.Empty);
                return null;
            case BlockTypes.Repeater:
                AddRows(section, block, value as JsonArray);
                return null;
            default:
                var line = sameLine ?? section.AddParagraph();
                if (sameLine is not null)
                {
                    line.AddText(InlineGap);
                }

                AddLabelValue(line, $"{block.Label}: ", FormatValue(block.Type, value));
                return line;
        }
    }

    /// <summary>In câu hỏi chọn; trả về dòng đã in khi các lựa chọn nằm trên một dòng (<c>inline</c>), ngược lại <c>null</c>.</summary>
    private static Paragraph? AddChoices(Section section, TemplateBlock block, JsonNode? value)
    {
        var selected = SelectedValues(value);
        var options = block.Options ?? [];
        void Mark(Paragraph paragraph, TemplateOption option)
        {
            AddBox(paragraph, selected.Contains(option.Value));
            paragraph.AddText(" ");
            AddBilingual(paragraph, option.Label);
            if (option.Score is { } score)
            {
                paragraph.AddText($" ({score} điểm)");
            }
        }

        if (block.PrintOptionsOnly)
        {
            // Đoạn văn đứng trước đã là câu hỏi (như bản giấy): chỉ in các ô lựa chọn, chữ đậm, trên một dòng riêng.
            var boxes = section.AddParagraph();
            boxes.Format.LeftIndent = Unit.FromMillimeter(10);
            boxes.Format.SpaceBefore = Unit.FromPoint(3);
            boxes.Format.SpaceAfter = Unit.FromPoint(5);
            boxes.Format.Font.Bold = true;
            for (var i = 0; i < options.Count; i++)
            {
                if (i > 0)
                {
                    boxes.AddText(InlineGap + InlineGap);
                }

                Mark(boxes, options[i]);
            }

            return null;
        }

        if (block.Layout == TemplateBlock.LayoutInline)
        {
            var line = section.AddParagraph();
            AddBilingual(line, $"{block.Label}: ");
            for (var i = 0; i < options.Count; i++)
            {
                if (i > 0)
                {
                    line.AddText(InlineGap);
                }

                Mark(line, options[i]);
            }

            return line;
        }

        AddBilingual(section.AddParagraph(), $"{block.Label}:");
        foreach (var option in options)
        {
            var item = section.AddParagraph();
            Mark(item, option);
            item.Format.LeftIndent = Unit.FromMillimeter(10);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-4);
        }

        return null;
    }

    private static void AddRows(Section section, TemplateBlock block, JsonArray? rows)
    {
        AddBilingual(section.AddParagraph(), $"{block.Label}:");
        var columns = block.Columns ?? [];
        var table = section.AddTable();
        table.Borders.Width = 0.5;
        table.Format.Font.Size = 11;
        table.AddColumn(Unit.FromMillimeter(10));
        var width = (167d - 10d) / Math.Max(1, columns.Count);
        foreach (var _ in columns)
        {
            table.AddColumn(Unit.FromMillimeter(width));
        }

        var header = table.AddRow();
        header.HeadingFormat = true;
        header.Format.Font.Bold = true;
        header.Cells[0].AddParagraph("TT");
        for (var c = 0; c < columns.Count; c++)
        {
            AddBilingual(header.Cells[c + 1].AddParagraph(), columns[c].Label);
        }

        var count = Math.Max(rows?.Count ?? 0, 1);
        for (var r = 0; r < count; r++)
        {
            var row = table.AddRow();
            row.Cells[0].AddParagraph((r + 1).ToString(Vietnamese));
            var data = rows is not null && r < rows.Count ? rows[r] as JsonObject : null;
            for (var c = 0; c < columns.Count; c++)
            {
                row.Cells[c + 1].AddParagraph(data?[columns[c].Key]?.GetValue<string>() ?? string.Empty);
            }
        }

        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(2);
    }

    private static void AddScore(Section section, TemplateBlock block, ConsentPdfDocument document)
    {
        var score = document.Scores.TryGetValue(block.Key!, out var result) ? result : null;
        var line = section.AddParagraph();
        line.Format.SpaceBefore = Unit.FromPoint(3);
        AddBilingual(line, $"{block.Label}: ");
        AddBilingual(
            line, score?.Total is { } total ? $"{total} điểm – {score.LevelLabel}" : "chưa đủ câu trả lời", TextFormat.Bold);
    }

    private static void AddList(Section section, TemplateBlock block)
    {
        if (block.Box)
        {
            AddBoxedList(section, block);
            return;
        }

        var items = block.Items ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var marker = block.Layout == TemplateBlock.LayoutOrdered ? $"{i + 1}." : "–";
            var item = section.AddParagraph();
            item.Format.LeftIndent = Unit.FromMillimeter(7);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-7);
            item.Format.TabStops.AddTabStop(Unit.FromMillimeter(7));
            item.Format.Alignment = ParagraphAlignment.Justify;
            item.AddText($"{marker}\t");
            AddBilingual(item, items[i]);
        }
    }

    /// <summary>
    /// Danh sách trong khung nền xám (<c>box</c>, quyết định 43), như khung "Xin Quý người bệnh và thân nhân lưu ý" của bản giấy
    /// QĐ-VHTT-BM02: tiêu đề (nhãn) canh giữa đậm nghiêng, các mục chữ nghiêng. Mỗi mục một hàng để khung ngắt được sang trang.
    /// </summary>
    private static void AddBoxedList(Section section, TemplateBlock block)
    {
        AddGap(section, 4).Format.KeepWithNext = true;
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(167));
        table.LeftPadding = Unit.FromMillimeter(4);
        table.RightPadding = Unit.FromMillimeter(4);
        table.Borders.Left.Width = 1;
        table.Borders.Left.Color = BoxBorder;
        table.Borders.Right.Width = 1;
        table.Borders.Right.Color = BoxBorder;
        var rows = new List<Row>();
        if (!string.IsNullOrWhiteSpace(block.Label))
        {
            var titleRow = table.AddRow();
            var title = titleRow.Cells[0].AddParagraph();
            title.Format.Alignment = ParagraphAlignment.Center;
            AddBilingual(title, block.Label, TextFormat.Bold | TextFormat.Italic);
            titleRow.KeepWith = 1; // tiêu đề khung không đứng một mình cuối trang
            rows.Add(titleRow);
        }

        var items = block.Items ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var row = table.AddRow();
            var item = row.Cells[0].AddParagraph();
            item.Format.LeftIndent = Unit.FromMillimeter(6);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-6);
            item.Format.TabStops.AddTabStop(Unit.FromMillimeter(6));
            item.Format.Alignment = ParagraphAlignment.Justify;
            item.AddFormattedText(block.Layout == TemplateBlock.LayoutOrdered ? $"{i + 1}.\t" : "–\t", TextFormat.Bold | TextFormat.Italic);
            AddBilingual(item, items[i], TextFormat.Italic);
            rows.Add(row);
        }

        foreach (var row in rows)
        {
            row.Shading.Color = BoxFill;
        }

        rows[0].TopPadding = Unit.FromPoint(3);
        rows[0].Borders.Top.Width = 1;
        rows[0].Borders.Top.Color = BoxBorder;
        rows[^1].BottomPadding = Unit.FromPoint(3);
        rows[^1].Borders.Bottom.Width = 1;
        rows[^1].Borders.Bottom.Color = BoxBorder;
        AddGap(section, 4);
    }

    /// <summary>Ghi chú cuối phiếu (<c>footnote</c>, quyết định 43): chữ nhỏ nghiêng dưới đường kẻ, như "Hướng dẫn thực hiện phiếu".</summary>
    private static void AddFootnote(Section section, TemplateDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Footnote))
        {
            return;
        }

        var note = section.AddParagraph();
        note.Format.SpaceBefore = Unit.FromMillimeter(6);
        note.Format.KeepTogether = true; // ghi chú không bị cắt sang trang sau
        note.Format.Font.Size = 9;
        note.Format.Font.Italic = true;
        note.Format.Borders.Top.Width = 0.5;
        note.Format.Borders.DistanceFromTop = Unit.FromPoint(3);
        AddBilingual(note, definition.Footnote);
    }

    /// <summary>Một ô chữ ký: vai trò, ghi chú, ảnh chữ ký tay (nếu có), họ tên ("tên|phần thêm") và các dòng nhỏ.</summary>
    private sealed record SignatureSlot(string Role, string Hint, byte[]? ImagePng, string? Name, IReadOnlyList<string> Lines)
    {
        /// <summary>Lời xác nhận người ký ghi khi ký (ví dụ "Tôi đã nghe, hiểu và đồng ý"), in ngay trên chữ ký.</summary>
        public string? Statement { get; init; }
    }

    /// <summary>
    /// Dòng "địa danh, ngày … tháng … năm …" (chữ nghiêng, hoặc chữ đứng với <paramref name="upright"/> như bản giấy TTNB; phần
    /// tiếng Anh luôn nghiêng). Mẫu song ngữ ghi kèm tiếng Anh như bản giấy: "ngày (day) … tháng (month) … năm (year) …"; các
    /// chữ này ngắn nên tô màu trực tiếp, không qua nhận diện tiếng Anh.
    /// </summary>
    private static void AddDateLine(Paragraph paragraph, string place, DateOnly date, bool bilingual, bool upright = false)
    {
        var style = upright ? TextFormat.NotItalic : TextFormat.Italic;
        AddBilingual(paragraph, $"{place}, ", style);
        string[] parts = ["ngày", $" {date.Day:00} tháng", $" {date.Month:00} năm", $" {date.Year}"];
        string[] english = ["(day)", "(month)", "(year)"];
        for (var i = 0; i < parts.Length; i++)
        {
            paragraph.AddFormattedText(parts[i], style);
            if (bilingual && i < english.Length)
            {
                paragraph.AddText(" ");
                var run = paragraph.AddFormattedText(english[i], TextFormat.Italic);
                run.Color = English;
            }
        }
    }

    /// <summary>Ghi chú dưới tên ô ký; mẫu song ngữ thêm dòng tiếng Anh như bản giấy ("(Signature and full name)").</summary>
    private static string Hint(string vietnamese, string english, bool bilingual) => bilingual ? $"{vietnamese}\n{english}" : vietnamese;

    /// <param name="keepHint">
    /// Ghi chú của mẫu giữ nguyên sau khi xác nhận (<c>paperHints</c>, ví dụ "đã đối chiếu thông tin trên giấy tờ tùy thân"
    /// của bản giấy TTNB); trạng thái xác nhận in thành dòng nhỏ dưới họ tên.
    /// </param>
    private static SignatureSlot ConfirmationSlot(
        string role, string? hint, ConsentPdfConfirmation? confirmation, bool bilingual, bool keepHint = false)
    {
        if (confirmation is null)
        {
            return new SignatureSlot(role, hint ?? Hint("(Xác nhận bằng tài khoản)", "(Confirmed with staff account)", bilingual), null, null, []);
        }

        var lines = new List<string>();
        if (confirmation.Title is { Length: > 0 } title)
        {
            lines.Add(title);
        }

        var kept = keepHint && hint is not null;
        if (kept)
        {
            lines.Add(confirmation.ImagePng is null ? "Đã xác nhận bằng tài khoản" : "Đã ký, xác nhận bằng tài khoản");
        }

        lines.Add($"Xác nhận lúc {confirmation.ConfirmedAt:HH:mm}, {confirmation.ConfirmedAt:dd/MM/yyyy}");
        // Quyết định 17: nhân viên ký tay khi xác nhận; phiếu hoàn tất trước đó không có ảnh.
        var signedHint = kept
            ? hint!
            : confirmation.ImagePng is null
                ? Hint("(Đã xác nhận bằng tài khoản)", "(Confirmed with staff account)", bilingual)
                : Hint("(Đã ký, xác nhận bằng tài khoản)", "(Signed, confirmed with staff account)", bilingual);
        return new SignatureSlot(role, signedHint, confirmation.ImagePng, confirmation.Name, lines);
    }

    /// <summary>
    /// Các ô ký của phiếu theo người ký: <c>patient</c>, mã các bước xác nhận trung gian, <c>hospital</c> (thứ tự in mặc định),
    /// kèm thời điểm ký/xác nhận (ngày tháng của ô ký cuối phần, quyết định 43) và những người ký đã in ở cuối một phần.
    /// </summary>
    private sealed record SigningLayout(
        IReadOnlyList<(string Signer, SignatureSlot Slot, DateTimeOffset? At)> Slots,
        SignatureFramePrint? Frame,
        bool PlainTitles,
        bool PaperHints,
        bool BoldTitles,
        IReadOnlySet<string> PlacedInSections)
    {
        public static SigningLayout Create(ConsentPdfDocument document)
        {
            var labels = document.Definition.Signatures;
            var bilingual = document.Definition.Bilingual;
            var patientRole = document.Representative is not null
                ? labels?.Representative ?? "Người đại diện hợp pháp"
                : labels?.Patient ?? "Người bệnh";
            var patientLines = new List<string>();
            var patientName = document.Representative is { } representative
                // Số CCCD và quan hệ của người đại diện đã in ở phần thông tin người bệnh; ở đây nhắc lại quan hệ.
                ? $"{representative.FullName}|({VietnameseLabels.Relationship(representative.Relationship)})"
                : document.Patient.FullName;
            if (document.PatientSignature is { } signed)
            {
                patientLines.Add($"Ký lúc {signed.SignedAt:HH:mm}, {signed.SignedAt:dd/MM/yyyy}");
                if (signed.SignedTogether is { Length: > 0 } together)
                {
                    patientLines.Add(together);
                }
            }

            var patientHint = labels?.PatientHint ?? Hint("(Ký, ghi rõ họ tên)", "(Signature and full name)", bilingual);
            var paperHints = labels?.PaperHints == true;
            var slots = new List<(string, SignatureSlot, DateTimeOffset?)>
            {
                (TemplateSection.PatientSigner,
                    new SignatureSlot(patientRole, patientHint, document.PatientSignature?.ImagePng, patientName, patientLines)
                    {
                        Statement = document.PatientSignature?.Statement,
                    },
                    document.PatientSignature?.SignedAt),
            };
            var steps = labels?.Steps ?? [];
            for (var i = 0; i < steps.Count; i++)
            {
                var done = document.StepConfirmations is { } confirmations && i < confirmations.Count ? confirmations[i] : null;
                slots.Add((steps[i].Key, ConfirmationSlot(steps[i].Label, steps[i].Hint, done, bilingual, keepHint: paperHints), done?.ConfirmedAt));
            }

            slots.Add((TemplateSection.HospitalSigner,
                ConfirmationSlot(labels?.Hospital ?? "Nhân viên y tế", labels?.HospitalHint, document.HospitalConfirmation, bilingual, keepHint: paperHints),
                document.HospitalConfirmation?.ConfirmedAt));

            // Quyết định 26: có khung chữ ký của bệnh viện (ảnh PNG quản trị tải lên) thì mọi ô ký in khung: đã ký thì chữ ký
            // nằm giữa khung, chưa ký thì khung trống. Dòng ký luôn cao theo khung nên PDF trước và sau khi ký cùng bố cục.
            var frame = document.SignatureFrame is { } frameImage ? SignatureFramePrint.Prepare(frameImage.Content) : null;
            var placed = document.Definition.Sections.Select(section => section.Signer).OfType<string>().ToHashSet(StringComparer.Ordinal);
            return new SigningLayout(slots, frame, labels?.PlainTitles == true, paperHints, labels?.BoldTitles == true, placed);
        }
    }

    private static void AddSignatures(Section section, ConsentPdfDocument document, SigningLayout signing)
    {
        var labels = document.Definition.Signatures;
        var bilingual = document.Definition.Bilingual;
        var isRepresentative = document.Representative is not null;
        var remaining = signing.Slots.Where(slot => !signing.PlacedInSections.Contains(slot.Signer)).ToList();
        var requiresDecision = labels?.RequiresDecision ?? true;
        if (remaining.Count == 0 && !requiresDecision)
        {
            // Mọi ô ký đã in ở cuối từng phần (bản giấy BM10).
            return;
        }

        // Quyết định ghi nhận khi ký (bản xem trước để trống cả hai ô) và ngày tháng trên cùng một dòng. Mẫu chỉ ký xác
        // nhận (không hỏi Đồng ý/Từ chối) chỉ in ngày tháng.
        var decision = section.AddParagraph();
        decision.Format.SpaceBefore = Unit.FromMillimeter(1);
        decision.Format.KeepWithNext = true;
        decision.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
        if (requiresDecision)
        {
            var question = labels?.DecisionLabel ?? $"Ý kiến của {(isRepresentative ? "người đại diện" : "người bệnh")}";
            // Câu hỏi đã có dấu "?" (hoặc kết thúc bằng ngoặc sau dấu "?") thì không thêm dấu ":".
            AddBilingual(decision, LabelWithSeparator(question), TextFormat.Bold);
            if (labels?.DecisionLabel is not null)
            {
                // Câu hỏi riêng của mẫu thường dài: đưa lựa chọn và ngày tháng xuống dòng dưới.
                decision = section.AddParagraph();
                decision.Format.KeepWithNext = true;
                decision.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
            }

            AddBox(decision, document.Decision == ConsentDecision.Agreed);
            decision.AddText(" ");
            AddBilingual(decision, labels?.AgreeLabel ?? "Đồng ý");
            decision.AddText("    ");
            AddBox(decision, document.Decision == ConsentDecision.Refused);
            decision.AddText(" ");
            AddBilingual(decision, labels?.RefuseLabel ?? "Từ chối");
        }

        if (remaining.Count == 0)
        {
            return;
        }

        var centerDate = labels?.CenterDate == true;
        if (centerDate)
        {
            // Như bản giấy TTNB: dòng ngày tháng canh giữa trang, chữ đứng; có câu hỏi ý kiến thì ngày tháng in dòng riêng.
            if (requiresDecision)
            {
                decision = section.AddParagraph();
                decision.Format.KeepWithNext = true;
            }

            decision.Format.Alignment = ParagraphAlignment.Center;
        }
        else if (bilingual && requiresDecision)
        {
            // Dòng ngày tháng song ngữ dài hơn: in dòng riêng, canh phải, để không xuống dòng giữa chừng sau các ô ý kiến.
            decision = section.AddParagraph();
            decision.Format.KeepWithNext = true;
            decision.Format.Alignment = ParagraphAlignment.Right;
        }
        else
        {
            decision.AddTab();
        }

        AddDateLine(decision, document.Hospital.Place, document.DocumentDate, bilingual, upright: centerDate);

        // Ô chữ ký theo thứ tự: người bệnh/người đại diện, các bước xác nhận trung gian (nếu mẫu có), xác nhận cuối của bệnh
        // viện; xếp hai ô mỗi hàng.
        var slots = remaining.Select(slot => slot.Slot).ToList();
        var patientIndex = remaining.FindIndex(slot => slot.Signer == TemplateSection.PatientSigner);
        if (labels?.HospitalFirst == true && patientIndex >= 0)
        {
            // Như bản giấy: các ô ký phía bệnh viện trước (bên trái), người bệnh/người đại diện sau cùng.
            var patient = slots[patientIndex];
            slots.RemoveAt(patientIndex);
            slots.Add(patient);
        }

        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(83));
        table.AddColumn(Unit.FromMillimeter(84));
        for (var start = 0; start < slots.Count; start += 2)
        {
            AddSlotRows(table, [.. slots.Skip(start).Take(2)], signing, topGap: start > 0);
        }
    }

    /// <summary>
    /// Ô ký cuối một phần (<c>signer</c> của phần, quyết định 43), như bản giấy BM10: dòng "Ngày … tháng … năm …", tên ô ký,
    /// chữ ký và họ tên ở nửa phải trang.
    /// </summary>
    private static void AddSectionSigner(Section section, ConsentPdfDocument document, SigningLayout signing, string signer)
    {
        var entry = signing.Slots.FirstOrDefault(slot => slot.Signer == signer);
        if (entry.Slot is null)
        {
            return;
        }

        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(83));
        table.AddColumn(Unit.FromMillimeter(SignerColumnMm));
        var dateRow = table.AddRow();
        dateRow.TopPadding = Unit.FromMillimeter(1);
        dateRow.KeepWith = 3; // ngày tháng, tên ô ký, khoảng ký và họ tên trên cùng một trang
        var date = dateRow.Cells[1].AddParagraph();
        date.Format.Alignment = ParagraphAlignment.Center;
        AddSignerDate(date, entry.At, document.Definition.Bilingual);
        AddSlotRows(table, [null, entry.Slot], signing, topGap: false);
    }

    /// <summary>
    /// Dòng "Ngày … tháng … năm …" của ô ký cuối phần: ngày người đó ký hoặc xác nhận; chưa ký thì để chấm như bản giấy.
    /// </summary>
    private static void AddSignerDate(Paragraph paragraph, DateTimeOffset? at, bool bilingual)
    {
        string Two(int value) => value.ToString("00", CultureInfo.InvariantCulture);
        string[] blank = ["Ngày", " …… tháng", " …… năm", " 20……"];
        string[] english = ["(Day)", "(month)", "(year)"];
        string Line(string[] parts) => string.Concat(parts.Select((part, i) => bilingual && i < english.Length ? $"{part} {english[i]}" : part));
        // Dòng song ngữ ở cỡ 12 dài hơn nửa trang: thu nhỏ để không xuống dòng ở số năm. Cùng một cỡ cho dòng để chấm và dòng có
        // ngày, để bố cục (số trang) trước và sau khi ký như nhau.
        paragraph.Format.Font.Size = FitFontSize(
            [(Line(blank), XFontStyleEx.Italic), (Line(["Ngày", " 00 tháng", " 00 năm", " 0000"]), XFontStyleEx.Italic)], SignerColumnMm, 12, 9);
        string[] parts = at is { } time
            ? ["Ngày", $" {Two(time.Day)} tháng", $" {Two(time.Month)} năm", $" {time.Year.ToString(CultureInfo.InvariantCulture)}"]
            : blank;
        for (var i = 0; i < parts.Length; i++)
        {
            paragraph.AddFormattedText(parts[i], TextFormat.Italic);
            if (bilingual && i < english.Length)
            {
                paragraph.AddText(" ");
                var run = paragraph.AddFormattedText(english[i], TextFormat.Italic);
                run.Color = English;
            }
        }
    }

    /// <summary>Cột phải của ô ký cuối phần (nửa phải trang).</summary>
    private const double SignerColumnMm = 84;

    /// <summary>
    /// Ba hàng của một dòng ô ký (tên ô ký, khoảng ký, họ tên); mỗi cột một ô ký, cột <c>null</c> để trống (ô ký cuối phần
    /// nằm ở cột phải).
    /// </summary>
    private static void AddSlotRows(Table table, IReadOnlyList<SignatureSlot?> columns, SigningLayout signing, bool topGap)
    {
        var frame = signing.Frame;
        var roles = table.AddRow();
        roles.KeepWith = 2; // giữ dòng vai trò, khoảng ký và họ tên trên cùng một trang
        if (topGap)
        {
            roles.TopPadding = Unit.FromMillimeter(4);
        }

        var space = table.AddRow();
        space.Height = Unit.FromMillimeter(frame is not null ? (FramedSignatureWidthMm * frame.Height / frame.Width) + 2 : 14);
        space.VerticalAlignment = VerticalAlignment.Center;
        var names = table.AddRow();
        for (var c = 0; c < columns.Count; c++)
        {
            if (columns[c] is not { } slot)
            {
                continue;
            }

            AddSignatureCell(roles.Cells[c], slot.Role, slot.Hint, slot.Statement, signing.PlainTitles, signing.PaperHints, signing.BoldTitles);
            if (slot.ImagePng is { } image)
            {
                // Ảnh lưu là ảnh xám; in bằng màu mực xanh (cả chữ ký cũ vẽ mực đen), trong khung nếu có.
                if (frame?.Compose(image) is { } framed)
                {
                    AddFramedSignature(space.Cells[c], framed, frame);
                }
                else
                {
                    AddSignatureImage(space.Cells[c], SignatureImage.Colorize(image));
                }
            }
            else if (frame is not null)
            {
                AddFramedSignature(space.Cells[c], frame.EmptyPng, frame);
            }

            if (slot.Name is { } fullName)
            {
                var name = names.Cells[c].AddParagraph();
                name.Format.Alignment = ParagraphAlignment.Center;
                name.Format.SpaceAfter = 0;
                var parts = fullName.Split('|', 2);
                name.AddFormattedText(parts[0], TextFormat.Bold);
                if (parts.Length > 1)
                {
                    name.AddText($" {parts[1]}");
                }
            }

            foreach (var line in slot.Lines)
            {
                AddSmallLine(names.Cells[c], line);
            }
        }
    }

    private const double LogoColumnMm = 20;

    /// <summary>
    /// Logo chỉ được kiểm tra phần đầu file lúc tải lên; thử giải mã trước khi in để một file hỏng không làm hỏng việc in
    /// mọi phiếu (khi đó phiếu in không có logo).
    /// </summary>
    internal static bool CanDecode(LogoImage logo)
    {
        try
        {
            using var stream = new MemoryStream(logo.Content, writable: false);
            using var image = XImage.FromStream(stream);
            return image.PixelWidth > 0 && image.PixelHeight > 0;
        }
#pragma warning disable CA1031 // Mọi lỗi giải mã ảnh đều xử lý giống nhau: bỏ logo.
        catch (Exception)
#pragma warning restore CA1031
        {
            return false;
        }
    }

    /// <summary>Logo cao tối đa 15 mm (trước đây 18–20 mm; bệnh viện yêu cầu nhỏ lại 15/11/2026).</summary>
    private const double LogoMaxHeightMm = 15;

    /// <summary>Tinos đậm: đỉnh dòng chữ (ascent 1825) cách đỉnh chữ hoa (1356) 469/2048 cỡ chữ.</summary>
    private const double CapTopEm = 469.0 / 2048;

    /// <summary>
    /// Logo bệnh viện, thu vừa ô, giữ đúng tỉ lệ. Đẩy xuống một khoảng bằng từ đỉnh dòng đến đỉnh chữ hoa của chữ cỡ
    /// <paramref name="alignWithTextOfSize"/> bên cạnh, để cạnh trên logo thẳng cạnh trên chữ quốc hiệu.
    /// </summary>
    internal static void AddLogo(
        Cell cell, LogoImage logo, double maxWidthMm, double maxHeightMm, double alignWithTextOfSize, bool center = false)
    {
        var scale = Math.Min(maxWidthMm / logo.Width, maxHeightMm / logo.Height);
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = center ? ParagraphAlignment.Center : ParagraphAlignment.Left;
        paragraph.Format.SpaceBefore = Unit.FromPoint(alignWithTextOfSize * CapTopEm);
        var image = paragraph.AddImage("base64:" + Convert.ToBase64String(logo.Content));
        image.LockAspectRatio = false;
        image.Width = Unit.FromMillimeter(logo.Width * scale);
        image.Height = Unit.FromMillimeter(logo.Height * scale);
    }

    /// <summary>Bề rộng khung chữ ký khi in (mm); chiều cao theo tỉ lệ ảnh khung.</summary>
    private const double FramedSignatureWidthMm = 48;

    /// <summary>Ảnh khung (có hoặc chưa có chữ ký), in rộng <see cref="FramedSignatureWidthMm"/>, giữ tỉ lệ khung.</summary>
    private static void AddFramedSignature(Cell cell, byte[] png, SignatureFramePrint frame)
    {
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        var image = paragraph.AddImage("base64:" + Convert.ToBase64String(png));
        image.LockAspectRatio = false;
        image.Width = Unit.FromMillimeter(FramedSignatureWidthMm);
        image.Height = Unit.FromMillimeter(FramedSignatureWidthMm * frame.Height / frame.Width);
    }

    /// <summary>Ảnh chữ ký tay khi không có khung, thu vừa 70 × 12 mm, giữ đúng tỉ lệ.</summary>
    private static void AddSignatureImage(Cell cell, byte[] png)
    {
        const double MaxWidthMm = 70;
        const double MaxHeightMm = 12;
        var width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        var scale = Math.Min(MaxWidthMm / width, MaxHeightMm / height);
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Center;
        var image = paragraph.AddImage("base64:" + Convert.ToBase64String(png));
        image.LockAspectRatio = false;
        image.Width = Unit.FromMillimeter(width * scale);
        image.Height = Unit.FromMillimeter(height * scale);
    }

    private static void AddSmallLine(Cell cell, string text)
    {
        var line = cell.AddParagraph(text);
        line.Format.Alignment = ParagraphAlignment.Center;
        line.Format.Font.Size = 10;
        line.Format.SpaceAfter = 0;
    }

    /// <param name="plainTitle">In tên ô ký đúng như đã ghi (không viết hoa toàn bộ, không đậm), như bản giấy của mẫu.</param>
    /// <param name="paperHint">
    /// Ghi chú gần bằng cỡ chữ tên ô ký như bản giấy: cỡ 11 (bản giấy cỡ 12 trên trang rộng hơn), mặc định cỡ 10.
    /// </param>
    private static void AddSignatureCell(
        Cell cell, string role, string hint, string? statement, bool plainTitle, bool paperHint = false, bool boldTitle = false)
    {
        var title = cell.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        if (plainTitle)
        {
            AddBilingual(title, role);
        }
        else if (boldTitle)
        {
            // Như bản giấy QĐ-VHTT-BM02, BM10 (quyết định 43): đậm, giữ chữ thường.
            AddBilingual(title, role, TextFormat.Bold);
        }
        else
        {
            AddBilingual(title, role, TextFormat.Bold, upper: true);
        }
        var help = cell.AddParagraph();
        AddHint(help, hint);
        help.Format.Alignment = ParagraphAlignment.Center;
        help.Format.Font.Italic = true;
        help.Format.Font.Size = paperHint ? 11 : 10;
        if (statement is not null)
        {
            // Lời xác nhận người ký ghi khi ký, như dòng viết tay trên phiếu giấy.
            var words = cell.AddParagraph();
            words.Format.Alignment = ParagraphAlignment.Center;
            words.Format.SpaceBefore = Unit.FromPoint(2);
            var text = words.AddFormattedText($"“{statement}”", TextFormat.Bold | TextFormat.Italic);
            text.Color = Statement;
        }
    }

    /// <summary>Ghi chú dưới tên ô ký: dòng bắt đầu bằng "!" in màu đỏ, cả phần tiếng Anh (dấu "!" không in), như bản giấy TTNB.</summary>
    private static void AddHint(Paragraph paragraph, string hint)
    {
        var lines = hint.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                paragraph.AddLineBreak();
            }

            if (lines[i].StartsWith('!'))
            {
                paragraph.AddFormattedText(lines[i][1..]).Color = Note;
            }
            else
            {
                AddBilingual(paragraph, lines[i]);
            }
        }
    }

    private static void AddFooter(Section section, ConsentPdfDocument document)
    {
        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 8;
        footer.Format.Font.Color = Muted;
        footer.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
        footer.AddText($"MS: {document.TemplateCode} · Phiên bản {document.TemplateVersionNo} · Số phiếu {document.FormNo}");
        footer.AddTab();
        footer.AddText("Trang ");
        footer.AddPageField();
        footer.AddText("/");
        footer.AddNumPagesField();

        // Quyết định 25: lượt tiếp nhận (HIS) mà phiếu thuộc về, in ở chân mỗi trang để đối soát.
        if (document.Encounter is { } encounter)
        {
            var line = section.Footers.Primary.AddParagraph();
            line.Format.Font.Size = 8;
            line.Format.Font.Color = Muted;
            var parts = new List<string>
            {
                $"Số tiếp nhận: {encounter.Number}",
                encounter.ReceivedAt.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture),
            };
            if (encounter.Place is { } place)
            {
                parts.Add(place);
            }

            if (encounter.ReceivedBy is { } receivedBy)
            {
                parts.Add($"Người tiếp nhận: {receivedBy}");
            }

            line.AddText(string.Join(" · ", parts));
        }
    }

    private static void AddSectionTitle(Section section, string text)
    {
        var title = section.AddParagraph();
        AddBilingual(title, text);
        title.Format.Font.Bold = true;
        title.Format.SpaceBefore = Unit.FromMillimeter(3);
        title.Format.SpaceAfter = Unit.FromPoint(3);
        title.Format.KeepWithNext = true;
    }

    private static Paragraph TwoColumns(Section section)
    {
        var paragraph = section.AddParagraph();
        paragraph.Format.TabStops.AddTabStop(Unit.FromMillimeter(100));
        return paragraph;
    }

    /// <summary>Một dòng hai cột "nhãn: giá trị" (cột phải bắt đầu ở 100 mm).</summary>
    private static void AddTwoColumns(
        Section section, string leftLabel, string? leftValue, string rightLabel, string? rightValue, bool boldLeft = false)
    {
        var line = TwoColumns(section);
        AddLabelValue(line, leftLabel, leftValue, boldLeft);
        line.AddTab();
        AddLabelValue(line, rightLabel, rightValue);
    }

    /// <summary>Thêm chữ của mẫu song ngữ: phần tiếng Anh màu xanh, chữ nghiêng; tiếng Việt giữ màu đen; "\n" thành xuống dòng.</summary>
    private static void AddBilingual(
        Paragraph paragraph, string text, TextFormat? format = null, bool upper = false, bool wholeLines = false)
    {
        foreach (var segment in BilingualText.Split(text, wholeLines))
        {
            if (segment.Text == "\n")
            {
                paragraph.AddLineBreak();
                continue;
            }

            var content = upper ? segment.Text.ToUpper(Vietnamese) : segment.Text;
            var run = format is { } f ? paragraph.AddFormattedText(content, f) : paragraph.AddFormattedText(content);
            if (segment.IsEnglish)
            {
                run.Color = English;
                run.Italic = true;
            }
        }
    }

    private static void AddLabelValue(Paragraph paragraph, string label, string? value, bool bold = false)
    {
        AddBilingual(paragraph, label);
        if (string.IsNullOrWhiteSpace(value))
        {
            paragraph.AddText(Blank);
            return;
        }

        paragraph.AddFormattedText(value, bold ? TextFormat.Bold : TextFormat.NotBold);
    }

    private static string? FormatValue(string type, JsonNode? value)
    {
        if (value is not JsonValue v || !v.TryGetValue<string>(out var text))
        {
            return null;
        }

        return type switch
        {
            BlockTypes.Date when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) =>
                date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
            BlockTypes.DateTime when DateTime.TryParseExact(text, "yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) =>
                time.ToString("HH:mm 'ngày' dd/MM/yyyy", CultureInfo.InvariantCulture),
            _ => text,
        };
    }

    private static string Flatten(string text) => text.Replace('\n', ' ');
}

/// <summary>Cấp phông Tinos nhúng sẵn trong assembly cho PDFsharp (không phụ thuộc phông cài trên máy chủ).</summary>
internal sealed class EmbeddedFontResolver : IFontResolver
{
    public const string FamilyName = "Tinos";

    /// <summary>
    /// Font con của DejaVu Sans chỉ gồm ☐ và ☒ (Tinos không có hai ký tự này), dùng in ô đánh dấu; giấy phép ở
    /// <c>Pdf/Fonts/DejaVu-LICENSE.txt</c>.
    /// </summary>
    public const string BoxFamilyName = "DejaVu Sans";

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        string.Equals(familyName, BoxFamilyName, StringComparison.Ordinal)
            ? new FontResolverInfo("DejaVuSans-Ballot")
            : (isBold, isItalic) switch
            {
                (true, true) => new FontResolverInfo("Tinos-Bold", false, true),
                (true, false) => new FontResolverInfo("Tinos-Bold"),
                (false, true) => new FontResolverInfo("Tinos-Italic"),
                _ => new FontResolverInfo("Tinos-Regular"),
            };

    public byte[]? GetFont(string faceName)
    {
        using var stream = typeof(EmbeddedFontResolver).Assembly.GetManifestResourceStream($"Pdf.Fonts.{faceName}.ttf");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
