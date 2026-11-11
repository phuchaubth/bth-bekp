using System.Globalization;
using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
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
    private const string Blank = "……………………";
    private static readonly Color Muted = new(0x55, 0x5D, 0x6B);
    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    private static readonly Lock FontGate = new();

    public MigraDocConsentPdfRenderer() => EnsureFontResolver();

    public byte[] Render(ConsentPdfDocument document)
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

        var renderer = new PdfDocumentRenderer { Document = pdf };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);
        return stream.ToArray();
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
        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(72));
        table.AddColumn(Unit.FromMillimeter(95));
        var row = table.AddRow();

        var left = row.Cells[0].AddParagraph();
        left.Format.Alignment = ParagraphAlignment.Center;
        left.Format.Font.Size = 11;
        left.AddText(document.Hospital.ParentAgency.ToUpper(Vietnamese));
        left.AddLineBreak();
        var hospital = left.AddFormattedText(document.Hospital.Name.ToUpper(Vietnamese), TextFormat.Bold);
        hospital.Underline = Underline.Single;
        if (!string.IsNullOrWhiteSpace(document.DepartmentName))
        {
            left.AddLineBreak();
            left.AddText($"Khoa/phòng: {document.DepartmentName}");
        }

        var right = row.Cells[1].AddParagraph();
        right.Format.Alignment = ParagraphAlignment.Center;
        right.Format.Font.Size = 11;
        right.AddFormattedText("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM", TextFormat.Bold);
        right.AddLineBreak();
        var motto = right.AddFormattedText("Độc lập - Tự do - Hạnh phúc", TextFormat.Bold);
        motto.Underline = Underline.Single;

        var meta = section.AddParagraph();
        meta.Format.SpaceBefore = Unit.FromMillimeter(3);
        meta.Format.Font.Size = 10;
        meta.Format.Font.Color = Muted;
        meta.Format.TabStops.AddTabStop(Unit.FromMillimeter(167), TabAlignment.Right);
        meta.AddText($"MS: {document.TemplateCode} (phiên bản {document.TemplateVersionNo})");
        meta.AddTab();
        meta.AddText($"Số phiếu: {document.FormNo}");
    }

    private static void AddTitle(Section section, ConsentPdfDocument document)
    {
        var title = section.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        title.Format.SpaceBefore = Unit.FromMillimeter(4);
        title.Format.SpaceAfter = Unit.FromMillimeter(document.Definition.Subtitle is null ? 4 : 1);
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        var lines = document.Definition.Title.ToUpper(Vietnamese).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                title.AddLineBreak();
            }

            title.AddText(lines[i].Trim());
        }

        if (document.Definition.Subtitle is { } subtitle)
        {
            var sub = section.AddParagraph(subtitle);
            sub.Format.Alignment = ParagraphAlignment.Center;
            sub.Format.Font.Italic = true;
            sub.Format.SpaceAfter = Unit.FromMillimeter(4);
        }
    }

    private static void AddPatient(Section section, ConsentPdfDocument document)
    {
        AddSectionTitle(section, "I. Thông tin người bệnh");
        var patient = document.Patient;
        var age = VietnameseLabels.Age(patient.DateOfBirth, document.DocumentDate);

        var line1 = TwoColumns(section);
        AddLabelValue(line1, "Họ và tên: ", patient.FullName.ToUpper(Vietnamese), bold: true);
        line1.AddTab();
        AddLabelValue(line1, "Giới tính: ", VietnameseLabels.Gender(patient.Gender));

        var line2 = TwoColumns(section);
        AddLabelValue(line2, "Ngày sinh: ", $"{patient.DateOfBirth:dd/MM/yyyy} ({age} tuổi)");
        line2.AddTab();
        AddLabelValue(line2, "Mã người bệnh: ", patient.PatientCode);

        var line3 = TwoColumns(section);
        AddLabelValue(line3, "Số CCCD: ", patient.CitizenId);
        line3.AddTab();
        AddLabelValue(line3, "Điện thoại: ", patient.PhoneNumber);

        AddLabelValue(section.AddParagraph(), "Địa chỉ: ", patient.Address);

        if (document.Representative is { } representative)
        {
            var signer = section.AddParagraph();
            AddLabelValue(signer, "Người ký thay mặt người bệnh: ", representative.FullName.ToUpper(Vietnamese), bold: true);
            AddLabelValue(signer, " – Quan hệ: ", VietnameseLabels.Relationship(representative.Relationship));
            AddLabelValue(signer, " – Số CCCD: ", representative.CitizenId);
        }
    }

    private static void AddSection(Section section, TemplateSection templateSection, ConsentPdfDocument document)
    {
        if (!string.IsNullOrWhiteSpace(templateSection.Title))
        {
            AddSectionTitle(section, templateSection.Title);
        }

        foreach (var block in templateSection.Blocks)
        {
            switch (block.Type)
            {
                case BlockTypes.Paragraph:
                    var paragraph = section.AddParagraph(block.Text ?? string.Empty);
                    paragraph.Format.Alignment = ParagraphAlignment.Justify;
                    break;
                case BlockTypes.List:
                    AddList(section, block);
                    break;
                case BlockTypes.Score:
                    AddScore(section, block, document);
                    break;
                default:
                    // Trường không hiển thị (điều kiện chưa thỏa) thì không in.
                    if (IsShown(block, document))
                    {
                        AddField(section, block, document.Data[block.Key!]);
                    }

                    break;
            }
        }
    }

    /// <summary>Trường có giá trị, hoặc không có điều kiện hiển thị (in ô trống để người bệnh thấy đủ nội dung).</summary>
    private static bool IsShown(TemplateBlock block, ConsentPdfDocument document) =>
        block.VisibleWhen is null || document.Data[block.Key!] is not null;

    private static void AddField(Section section, TemplateBlock block, JsonNode? value)
    {
        switch (block.Type)
        {
            case BlockTypes.Choice:
            case BlockTypes.MultiChoice:
                AddChoices(section, block, value);
                break;
            case BlockTypes.Confirm:
                var confirm = section.AddParagraph();
                confirm.Format.LeftIndent = Unit.FromMillimeter(6);
                confirm.Format.FirstLineIndent = Unit.FromMillimeter(-6);
                confirm.AddText($"{(value is JsonValue v && v.TryGetValue<bool>(out var flag) && flag ? Checked : Unchecked)}\t");
                confirm.Format.TabStops.AddTabStop(Unit.FromMillimeter(6));
                confirm.AddText(block.Label ?? string.Empty);
                break;
            case BlockTypes.Repeater:
                AddRows(section, block, value as JsonArray);
                break;
            default:
                AddLabelValue(section.AddParagraph(), $"{block.Label}: ", FormatValue(block.Type, value));
                break;
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
        string Mark(TemplateOption option) =>
            $"{(selected.Contains(option.Value) ? Checked : Unchecked)} {option.Label}{(option.Score is { } score ? $" ({score} điểm)" : string.Empty)}";

        if (block.Layout == "inline")
        {
            var line = section.AddParagraph();
            line.AddText($"{block.Label}: ");
            // Khoảng trắng không ngắt để các lựa chọn trên cùng dòng không dính vào nhau.
            line.AddText(string.Join("\u00A0\u00A0\u00A0 ", options.Select(Mark)));
            return;
        }

        section.AddParagraph($"{block.Label}:");
        foreach (var option in options)
        {
            var item = section.AddParagraph(Mark(option));
            item.Format.LeftIndent = Unit.FromMillimeter(10);
            item.Format.FirstLineIndent = Unit.FromMillimeter(-4);
        }
    }

    private static void AddRows(Section section, TemplateBlock block, JsonArray? rows)
    {
        section.AddParagraph($"{block.Label}:");
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
            header.Cells[c + 1].AddParagraph(columns[c].Label);
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
        line.AddText($"{block.Label}: ");
        line.AddFormattedText(
            score?.Total is { } total ? $"{total} điểm – {score.LevelLabel}" : "chưa đủ câu trả lời", TextFormat.Bold);
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
            item.AddText($"{marker}\t{items[i]}");
        }
    }

    private static void AddSignatures(Section section, ConsentPdfDocument document)
    {
        var date = section.AddParagraph();
        date.Format.Alignment = ParagraphAlignment.Right;
        date.Format.SpaceBefore = Unit.FromMillimeter(3);
        date.Format.Font.Italic = true;
        date.Format.KeepWithNext = true;
        date.AddText(
            $"{document.Hospital.Place}, ngày {document.DocumentDate.Day:00} tháng {document.DocumentDate.Month:00} năm {document.DocumentDate.Year}");

        var labels = document.Definition.Signatures;
        var patientRole = document.Representative is null
            ? labels?.Patient ?? "Người bệnh"
            : labels?.Representative ?? "Người đại diện hợp pháp";
        var signerName = document.Representative?.FullName ?? document.Patient.FullName;

        var table = section.AddTable();
        table.AddColumn(Unit.FromMillimeter(83));
        table.AddColumn(Unit.FromMillimeter(84));
        var roles = table.AddRow();
        roles.KeepWith = 2; // giữ dòng vai trò, khoảng ký và họ tên trên cùng một trang
        AddSignatureCell(roles.Cells[0], patientRole, "(Ký, ghi rõ họ tên)");
        AddSignatureCell(roles.Cells[1], labels?.Hospital ?? "Nhân viên y tế", labels?.HospitalHint ?? "(Ký, ghi rõ họ tên)");

        var space = table.AddRow();
        space.Height = Unit.FromMillimeter(20);

        var names = table.AddRow();
        var name = names.Cells[0].AddParagraph();
        name.Format.Alignment = ParagraphAlignment.Center;
        name.AddFormattedText(signerName, TextFormat.Bold);
        if (document.Representative is { } representative)
        {
            var relation = names.Cells[0].AddParagraph($"Quan hệ với người bệnh: {VietnameseLabels.Relationship(representative.Relationship)}");
            relation.Format.Alignment = ParagraphAlignment.Center;
            relation.Format.Font.Size = 10;
        }
    }

    private static void AddSignatureCell(Cell cell, string role, string hint)
    {
        var title = cell.AddParagraph();
        title.Format.Alignment = ParagraphAlignment.Center;
        title.AddFormattedText(role.ToUpper(Vietnamese), TextFormat.Bold);
        var help = cell.AddParagraph(hint);
        help.Format.Alignment = ParagraphAlignment.Center;
        help.Format.Font.Italic = true;
        help.Format.Font.Size = 10;
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
    }

    private static void AddSectionTitle(Section section, string text)
    {
        var title = section.AddParagraph(text);
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

    private static void AddLabelValue(Paragraph paragraph, string label, string? value, bool bold = false)
    {
        paragraph.AddText(label);
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
