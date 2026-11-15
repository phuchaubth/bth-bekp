using System.Buffers.Binary;
using System.Globalization;
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
using PdfSharp.Fonts;

namespace Consent.Infrastructure.Pdf;

/// <summary>
/// Dựng PDF phiếu bằng MigraDoc (MIT) theo bố cục văn bản hành chính: cơ quan chủ quản và tên bệnh viện bên trái,
/// quốc hiệu – tiêu ngữ bên phải, tên phiếu, thông tin người bệnh, nội dung theo mẫu, ngày tháng và khối chữ ký.
/// Phông Tinos được nhúng vào file nên hiển thị đúng tiếng Việt trên mọi máy.
/// </summary>
internal sealed class MigraDocConsentPdfRenderer : IConsentPdfRenderer
{
    private const string Checked = "■";
    private const string Unchecked = "□";

    private static string Box(bool selected) => selected ? Checked : Unchecked;
    private const string Blank = "……………………";

    /// <summary>Khoảng cách giữa các phần in chung một dòng (lựa chọn, trường "sameLine"); khoảng trắng không ngắt để không dính nhau.</summary>
    private const string InlineGap = "\u00A0\u00A0\u00A0 ";
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);

    /// <summary>Màu phần tiếng Anh của mẫu song ngữ (in nghiêng; tiếng Việt giữ màu đen, chữ đứng).</summary>
    internal static readonly Color English = new(0x00, 0x70, 0xC0);

    /// <summary>Màu lời xác nhận của người ký ("Tôi đã nghe, hiểu và đồng ý"): đỏ, chữ đậm nghiêng.</summary>
    internal static readonly Color Statement = new(0xC0, 0x00, 0x00);
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
        foreach (var block in document.Definition.Sections)
        {
            AddSection(section, block, document);
        }

        AddSignatures(section, document);
        AddFooter(section, document);
        return pdf;
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
        row.VerticalAlignment = VerticalAlignment.Center;
        var textColumn = 0;
        if (logo is not null)
        {
            AddLogo(row.Cells[0], logo);
            textColumn = 1;
        }

        // Có logo thì cột chữ hẹp hơn: chữ nhỏ hơn một cỡ để tên đơn vị và quốc hiệu vẫn nằm trên một dòng.
        var headSize = logo is null ? 11 : 10;
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

    /// <summary>
    /// Đầu phiếu như bản giấy có mã tài liệu (<c>documentControl</c>): logo bên trái, quốc hiệu ở giữa, mã số – lần ban hành –
    /// ngày hiệu lực bên phải. Không in dòng khoa và dòng "MS … Số phiếu" (số phiếu in ở chân trang). Chưa tải logo thì ô
    /// trái in tên đơn vị để phiếu vẫn có tên bệnh viện.
    /// </summary>
    private static void AddControlledDocumentHead(Section section, ConsentPdfDocument document, TemplateDocumentControl control)
    {
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(ControlLogoColumnMm));
        table.AddColumn(Unit.FromMillimeter(167 - ControlLogoColumnMm - ControlBoxColumnMm));
        table.AddColumn(Unit.FromMillimeter(ControlBoxColumnMm));
        var row = table.AddRow();
        row.VerticalAlignment = VerticalAlignment.Center;

        if (document.Logo is { } logo && CanDecode(logo))
        {
            AddLogo(row.Cells[0], logo, maxWidthMm: ControlLogoColumnMm - 2, maxHeightMm: 20);
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

        AddNationalMotto(row.Cells[1], 11);

        var box = row.Cells[2].AddParagraph();
        box.Format.Font.Size = 7.5;
        AddBilingual(box, $"Mã số (Code No): {control.Code}");
        box.AddLineBreak();
        AddBilingual(box, $"Lần ban hành (Issue No): {control.IssueNo}");
        box.AddLineBreak();
        AddBilingual(box, $"Ngày hiệu lực (Effective Date): {control.EffectiveDate}");
    }

    /// <summary>Dòng trong ngoặc của tiêu đề, ví dụ "(Về chế độ thanh toán …)": dòng phụ, không viết hoa.</summary>
    private static bool IsTitleSubLine(string line) => line.Length > 2 && line[0] == '(' && line[^1] == ')';

    private static void AddTitle(Section section, ConsentPdfDocument document)
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

        if (document.Definition.Subtitle is { } subtitle)
        {
            var sub = section.AddParagraph();
            AddBilingual(sub, subtitle, wholeLines: true);
            sub.Format.Alignment = ParagraphAlignment.Center;
            sub.Format.Font.Italic = true;
            sub.Format.SpaceAfter = Unit.FromMillimeter(4);
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

    /// <summary>Mục thứ hai của dòng thông tin người bệnh bắt đầu gần giữa trang (như bản giấy).</summary>
    private const double PatientLineSecondItemMm = 84;

    /// <summary>Dòng thông tin người bệnh theo bản giấy của mẫu (<c>patientLines</c>), thay cho mục "I. Thông tin người bệnh".</summary>
    private static void AddPatientLines(Section section, ConsentPdfDocument document, IReadOnlyList<IReadOnlyList<PatientLineItem>> lines)
    {
        foreach (var items in lines)
        {
            var line = section.AddParagraph();
            line.Format.SpaceAfter = Unit.FromPoint(4);
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
            AddLabelValue(line, $"{item.Label ?? block.Label}: ", FormatValue(block.Type, document.Data[key]));
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
                // Như bản giấy: "Giới tính/ Gender: ■ Nam/Male □ Nữ/Female".
                AddBilingual(line, label);
                line.AddText($"{Box(patient.Gender == Gender.Male)} ");
                AddBilingual(line, male);
                line.AddText($"{InlineGap}{Box(patient.Gender == Gender.Female)} ");
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

    /// <summary>Trường của phiếu đã in ở dòng thông tin người bệnh, không in lại ở thân phiếu.</summary>
    private static HashSet<string> FieldsInPatientLines(TemplateDefinition definition) =>
        definition.PatientLines?.SelectMany(line => line).Select(item => item.Field).OfType<string>().ToHashSet(StringComparer.Ordinal)
        ?? [];

    private static void AddSection(Section section, TemplateSection templateSection, ConsentPdfDocument document)
    {
        if (!string.IsNullOrWhiteSpace(templateSection.Title))
        {
            AddSectionTitle(section, templateSection.Title);
        }

        var printedAbove = FieldsInPatientLines(document.Definition);

        // Dòng "nhãn: giá trị" vừa in; trường có "sameLine" in tiếp trên dòng này thay vì xuống dòng.
        Paragraph? valueLine = null;
        foreach (var block in templateSection.Blocks)
        {
            switch (block.Type)
            {
                case BlockTypes.Paragraph:
                    var paragraph = section.AddParagraph();
                    AddBilingual(paragraph, block.Text ?? string.Empty);
                    paragraph.Format.Alignment = ParagraphAlignment.Justify;
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
                AddChoices(section, block, value);
                return null;
            case BlockTypes.Confirm:
                var confirm = section.AddParagraph();
                confirm.Format.LeftIndent = Unit.FromMillimeter(6);
                confirm.Format.FirstLineIndent = Unit.FromMillimeter(-6);
                confirm.AddText($"{(value is JsonValue v && v.TryGetValue<bool>(out var flag) && flag ? Checked : Unchecked)}\t");
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

    private static void AddChoices(Section section, TemplateBlock block, JsonNode? value)
    {
        var selected = value switch
        {
            JsonArray array => array.Select(v => v!.GetValue<string>()).ToHashSet(StringComparer.Ordinal),
            JsonValue single when single.TryGetValue<string>(out var text) => [text],
            _ => new HashSet<string>(StringComparer.Ordinal),
        };
        var options = block.Options ?? [];
        void Mark(Paragraph paragraph, TemplateOption option)
        {
            paragraph.AddText($"{(selected.Contains(option.Value) ? Checked : Unchecked)} ");
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

            return;
        }

        if (block.Layout == "inline")
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

            return;
        }

        AddBilingual(section.AddParagraph(), $"{block.Label}:");
        foreach (var option in options)
        {
            var item = section.AddParagraph();
            Mark(item, option);
            item.Format.LeftIndent = Unit.FromMillimeter(10);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-4);
        }
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
        var items = block.Items ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var marker = block.Layout == "ordered" ? $"{i + 1}." : "–";
            var item = section.AddParagraph();
            item.Format.LeftIndent = Unit.FromMillimeter(7);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-7);
            item.Format.TabStops.AddTabStop(Unit.FromMillimeter(7));
            item.Format.Alignment = ParagraphAlignment.Justify;
            item.AddText($"{marker}\t");
            AddBilingual(item, items[i]);
        }
    }

    /// <summary>Một ô chữ ký: vai trò, ghi chú, ảnh chữ ký tay (nếu có), họ tên ("tên|phần thêm") và các dòng nhỏ.</summary>
    private sealed record SignatureSlot(string Role, string Hint, byte[]? ImagePng, string? Name, IReadOnlyList<string> Lines)
    {
        /// <summary>Lời xác nhận người ký ghi khi ký (ví dụ "Tôi đã nghe, hiểu và đồng ý"), in ngay trên chữ ký.</summary>
        public string? Statement { get; init; }
    }

    /// <summary>
    /// Dòng "địa danh, ngày … tháng … năm …" (chữ nghiêng). Mẫu song ngữ ghi kèm tiếng Anh như bản giấy: "ngày (day) …
    /// tháng (month) … năm (year) …"; các chữ này ngắn nên tô màu trực tiếp, không qua nhận diện tiếng Anh.
    /// </summary>
    private static void AddDateLine(Paragraph paragraph, string place, DateOnly date, bool bilingual)
    {
        AddBilingual(paragraph, $"{place}, ", TextFormat.Italic);
        string[] parts = ["ngày", $" {date.Day:00} tháng", $" {date.Month:00} năm", $" {date.Year}"];
        string[] english = ["(day)", "(month)", "(year)"];
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

    /// <summary>Ghi chú dưới tên ô ký; mẫu song ngữ thêm dòng tiếng Anh như bản giấy ("(Signature and full name)").</summary>
    private static string Hint(string vietnamese, string english, bool bilingual) => bilingual ? $"{vietnamese}\n{english}" : vietnamese;

    private static SignatureSlot ConfirmationSlot(string role, string? hint, ConsentPdfConfirmation? confirmation, bool bilingual)
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

        lines.Add($"Xác nhận lúc {confirmation.ConfirmedAt:HH:mm}, {confirmation.ConfirmedAt:dd/MM/yyyy}");
        // Quyết định 17: nhân viên ký tay khi xác nhận; phiếu hoàn tất trước đó không có ảnh.
        var signedHint = confirmation.ImagePng is null
            ? Hint("(Đã xác nhận bằng tài khoản)", "(Confirmed with staff account)", bilingual)
            : Hint("(Đã ký, xác nhận bằng tài khoản)", "(Signed, confirmed with staff account)", bilingual);
        return new SignatureSlot(role, signedHint, confirmation.ImagePng, confirmation.Name, lines);
    }

    private static void AddSignatures(Section section, ConsentPdfDocument document)
    {
        var labels = document.Definition.Signatures;
        var bilingual = document.Definition.Bilingual;
        var isRepresentative = document.Representative is not null;
        var patientRole = isRepresentative
            ? labels?.Representative ?? "Người đại diện hợp pháp"
            : labels?.Patient ?? "Người bệnh";

        // Quyết định ghi nhận khi ký (bản xem trước để trống cả hai ô) và ngày tháng trên cùng một dòng. Mẫu chỉ ký xác
        // nhận (không hỏi Đồng ý/Từ chối) chỉ in ngày tháng.
        var decision = section.AddParagraph();
        decision.Format.SpaceBefore = Unit.FromMillimeter(1);
        decision.Format.KeepWithNext = true;
        decision.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
        if (labels?.RequiresDecision ?? true)
        {
            var question = labels?.DecisionLabel ?? $"Ý kiến của {(isRepresentative ? "người đại diện" : "người bệnh")}";
            // Câu hỏi đã có dấu "?" (hoặc kết thúc bằng ngoặc sau dấu "?") thì không thêm dấu ":".
            var trimmed = question.TrimEnd(':', ' ');
            var isQuestion = trimmed.EndsWith('?') || trimmed.EndsWith("?)", StringComparison.Ordinal);
            AddBilingual(decision, isQuestion ? $"{trimmed} " : $"{trimmed}: ", TextFormat.Bold);
            if (labels?.DecisionLabel is not null)
            {
                // Câu hỏi riêng của mẫu thường dài: đưa lựa chọn và ngày tháng xuống dòng dưới.
                decision = section.AddParagraph();
                decision.Format.KeepWithNext = true;
                decision.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
            }

            decision.AddText($"{Box(document.Decision == ConsentDecision.Agreed)} ");
            AddBilingual(decision, labels?.AgreeLabel ?? "Đồng ý");
            decision.AddText($"    {Box(document.Decision == ConsentDecision.Refused)} ");
            AddBilingual(decision, labels?.RefuseLabel ?? "Từ chối");
        }

        if (bilingual && (labels?.RequiresDecision ?? true))
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

        AddDateLine(decision, document.Hospital.Place, document.DocumentDate, bilingual);

        // Ô chữ ký theo thứ tự: người bệnh/người đại diện, các bước xác nhận trung gian (nếu mẫu có), xác nhận cuối của
        // bệnh viện; xếp hai ô mỗi hàng.
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

        var patientHint = Hint("(Ký, ghi rõ họ tên)", "(Signature and full name)", bilingual);
        var slots = new List<SignatureSlot>
        {
            new(patientRole, patientHint, document.PatientSignature?.ImagePng, patientName, patientLines)
            {
                Statement = document.PatientSignature?.Statement,
            },
        };
        var steps = labels?.Steps ?? [];
        for (var i = 0; i < steps.Count; i++)
        {
            var done = document.StepConfirmations is { } confirmations && i < confirmations.Count ? confirmations[i] : null;
            slots.Add(ConfirmationSlot(steps[i].Label, steps[i].Hint, done, bilingual));
        }

        slots.Add(ConfirmationSlot(labels?.Hospital ?? "Nhân viên y tế", labels?.HospitalHint, document.HospitalConfirmation, bilingual));
        if (labels?.HospitalFirst == true)
        {
            // Như bản giấy: các ô ký phía bệnh viện trước (bên trái), người bệnh/người đại diện sau cùng.
            slots.Add(slots[0]);
            slots.RemoveAt(0);
        }

        var plainTitles = labels?.PlainTitles == true;

        // Quyết định 26: có khung chữ ký của bệnh viện (ảnh PNG quản trị tải lên) thì mọi ô ký in khung: đã ký thì chữ ký nằm
        // giữa khung, chưa ký thì khung trống. Dòng ký luôn cao theo khung nên PDF trước và sau khi ký cùng bố cục.
        var frame = document.SignatureFrame is { } frameImage ? SignatureFramePrint.Prepare(frameImage.Content) : null;

        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(83));
        table.AddColumn(Unit.FromMillimeter(84));
        for (var start = 0; start < slots.Count; start += 2)
        {
            var pair = slots.Skip(start).Take(2).ToList();
            var roles = table.AddRow();
            roles.KeepWith = 2; // giữ dòng vai trò, khoảng ký và họ tên trên cùng một trang
            if (start > 0)
            {
                roles.TopPadding = Unit.FromMillimeter(4);
            }

            var space = table.AddRow();
            space.Height = Unit.FromMillimeter(frame is not null ? (FramedSignatureWidthMm * frame.Height / frame.Width) + 2 : 14);
            space.VerticalAlignment = VerticalAlignment.Center;
            var names = table.AddRow();
            for (var c = 0; c < pair.Count; c++)
            {
                var slot = pair[c];
                AddSignatureCell(roles.Cells[c], slot.Role, slot.Hint, slot.Statement, plainTitles);
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
    }

    private const double LogoColumnMm = 20;

    /// <summary>
    /// Logo chỉ được kiểm tra phần đầu file lúc tải lên; thử giải mã trước khi in để một file hỏng không làm hỏng việc in
    /// mọi phiếu (khi đó phiếu in không có logo).
    /// </summary>
    private static bool CanDecode(LogoImage logo)
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

    /// <summary>Logo bệnh viện, thu vừa ô (mặc định 18 × 18 mm), giữ đúng tỉ lệ.</summary>
    private static void AddLogo(Cell cell, LogoImage logo, double maxWidthMm = 18, double maxHeightMm = 18)
    {
        var scale = Math.Min(maxWidthMm / logo.Width, maxHeightMm / logo.Height);
        var paragraph = cell.AddParagraph();
        paragraph.Format.Alignment = ParagraphAlignment.Left;
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
    private static void AddSignatureCell(Cell cell, string role, string hint, string? statement, bool plainTitle)
    {
        var title = cell.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        if (plainTitle)
        {
            AddBilingual(title, role);
        }
        else
        {
            AddBilingual(title, role, TextFormat.Bold, upper: true);
        }
        var help = cell.AddParagraph();
        AddBilingual(help, hint);
        help.Format.Alignment = ParagraphAlignment.Center;
        help.Format.Font.Italic = true;
        help.Format.Font.Size = 10;
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

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        (isBold, isItalic) switch
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
