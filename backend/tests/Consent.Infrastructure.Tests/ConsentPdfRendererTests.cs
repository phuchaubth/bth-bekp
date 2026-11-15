using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Application.Settings;
using Consent.Domain.ConsentForms;
using Consent.Domain.Patients;
using Consent.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using Mdom = MigraDoc.DocumentObjectModel;
using Consent.Tests.Samples;

namespace Consent.Infrastructure.Tests;

public sealed class ConsentPdfRendererTests
{
    private static readonly ConsentPatientInfo Patient = new(
        Guid.NewGuid(), "Phạm Thị Dung (giả lập)", new DateOnly(1988, 4, 5), Gender.Female, "036188000125", "0900000125",
        "Số 9 Đường Thử Nghiệm, Phường Mẫu", "BN000125");

    private static readonly ConsentRepresentativeInfo Spouse = new(
        Guid.NewGuid(), "Phạm Văn Chồng (giả lập)", RepresentativeRelationship.Spouse, "036085000777", null);

    /// <summary>Dữ liệu điền đầy đủ cho từng mẫu đề xuất (giả lập).</summary>
    private static JsonObject SampleData(string code) => code switch
    {
        "CK-01" => new JsonObject
        {
            ["examType"] = "bhytWithService",
            ["insuranceNumber"] = "GD4790000000125",
            ["insuranceValidFrom"] = "2026-01-01",
            ["insuranceValidTo"] = "2026-12-31",
            ["primaryCareFacility"] = "Trạm Y tế Phường Mẫu",
            ["referralPaper"] = "no",
            ["requestedServices"] = new JsonArray("time"),
            ["acknowledged"] = true,
        },
        "TT-01" => new JsonObject
        {
            ["providedInformation"] = new JsonArray("rights", "rules", "process", "costs", "privacy", "explanation", "safety", "feedback"),
            ["informationSharing"] = "share",
            ["informationRecipients"] = new JsonArray(
                new JsonObject { ["fullName"] = "Phạm Văn Chồng", ["relationship"] = "Chồng", ["phoneNumber"] = "0900000777" }),
            ["consentRoutineCare"] = "agree",
            ["consentContact"] = "agree",
            ["consentResearch"] = "disagree",
        },
        _ => new JsonObject
        {
            ["bed"] = "B3 – 12",
            ["assessmentReason"] = "initial",
            ["morseFallHistory"] = "no",
            ["morseSecondaryDiagnosis"] = "yes",
            ["morseAmbulatoryAid"] = "crutches",
            ["morseIntravenous"] = "no",
            ["morseGait"] = "weak",
            ["morseMentalStatus"] = "oriented",
            ["measuresStandard"] = new JsonArray("callBell", "lowBed", "environment", "education"),
            ["measuresMedium"] = new JsonArray("marker", "assist", "reassessOnChange"),
            ["reassessAt"] = "2026-11-12T08:00",
        },
    };

    private static ConsentPdfDocument Document(string code, bool representative, bool isFinal, string? examType = null)
    {
        var definition = TemplateDefinitionValidator.Parse(SampleConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;
        var data = SampleData(code);
        if (examType is not null)
        {
            data["examType"] = examType;
        }

        var evaluation = FormDataEvaluator.Evaluate(definition, data);
        Assert.True(evaluation.IsValid, string.Join("; ", evaluation.Errors.Select(e => $"{e.Key}: {string.Join(",", e.Value)}")));
        return new ConsentPdfDocument(
            new HospitalOptions(), "Khoa Khám bệnh", code, 1, "PDT-2026-000001", definition, evaluation.Data, evaluation.Scores,
            Patient, representative ? Spouse : null, new DateOnly(2026, 11, 11), isFinal);
    }

    [Theory]
    [InlineData("CK-01", false)]
    [InlineData("TT-01", true)]
    [InlineData("TN-01", false)]
    public void Render_ProducesValidPdf_WithEmbeddedVietnameseFont(string code, bool representative)
    {
        var bytes = new MigraDocConsentPdfRenderer().Render(Document(code, representative, isFinal: false));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 1, 3);
        var raw = Encoding.Latin1.GetString(bytes);
        Assert.Contains("FontFile2", raw, StringComparison.Ordinal);
        Assert.Contains("Tinos", raw, StringComparison.Ordinal);

        // Xem bằng mắt khi cần: đặt biến môi trường CONSENT_PDF_OUT là thư mục đích.
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, $"{code}.pdf"), bytes);
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Logo_IsPrintedInTheHeader(string kind)
    {
        var logo = LogoImage.Read(Convert.FromBase64String(kind == "png" ? TestLogos.Png64 : TestLogos.Jpeg40x32))!;
        var renderer = new MigraDocConsentPdfRenderer();

        var without = renderer.Render(Document("CK-01", representative: false, isFinal: false));
        var with = renderer.Render(Document("CK-01", representative: false, isFinal: false) with { Logo = logo });

        Assert.Equal(0, ImageCount(without));
        Assert.Equal(1, ImageCount(with));
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, $"CK-01-logo-{kind}.pdf"), with);
        }
    }

    [Fact]
    public void HospitalName_IsBoldWithoutUnderline_WhileTheMottoKeepsItsUnderline()
    {
        // Có và không có logo (hai bố cục đầu phiếu khác nhau).
        foreach (var logo in new[] { null, LogoImage.Read(Convert.FromBase64String(TestLogos.Png64)) })
        {
            var document = Document("CK-01", representative: false, isFinal: true) with { Logo = logo };

            var texts = HeadTexts(MigraDocConsentPdfRenderer.Build(document));

            var hospital = Assert.Single(texts, t => Plain(t) == document.Hospital.Name.ToUpper(new System.Globalization.CultureInfo("vi-VN")));
            Assert.True(hospital.Bold);
            Assert.Equal(Mdom.Underline.None, hospital.Underline);
            Assert.Equal(
                ["Độc lập - Tự do - Hạnh phúc"],
                texts.Where(t => t.Underline != Mdom.Underline.None).Select(Plain).ToArray());
        }
    }

    /// <summary>Các đoạn chữ có định dạng ở đầu phiếu (bảng đầu tiên: tên đơn vị, quốc hiệu).</summary>
    private static List<Mdom.FormattedText> HeadTexts(Mdom.Document layout)
    {
        var head = layout.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().First();
        var row = head.Rows[0];
        return Enumerable.Range(0, row.Cells.Count)
            .SelectMany(i => row.Cells[i].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>())
            .SelectMany(p => p.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.FormattedText>())
            .ToList();
    }

    private static string Plain(Mdom.FormattedText text) =>
        string.Concat(text.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Text>().Select(t => t.Content));

    /// <summary>Phiếu theo mẫu thật của bệnh viện, dữ liệu giả lập đầy đủ, khoa "Khoa Khám bệnh".</summary>
    private static ConsentPdfDocument HospitalDocument(string code)
    {
        var definition = TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;
        var evaluation = FormDataEvaluator.Evaluate(definition, HospitalData(code));
        Assert.True(evaluation.IsValid, string.Join("; ", evaluation.Errors.Select(e => $"{e.Key}: {string.Join(",", e.Value)}")));
        return new ConsentPdfDocument(
            new HospitalOptions(), "Khoa Khám bệnh", code, 1, "PDT-2026-000001", definition, evaluation.Data, evaluation.Scores,
            Patient, null, new DateOnly(2026, 11, 11), IsFinal: true);
    }

    /// <summary>Chữ ở đầu phiếu (tên đơn vị, khoa, quốc hiệu), mỗi ô một dòng.</summary>
    private static string HeadText(Mdom.Document layout)
    {
        var row = layout.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().First().Rows[0];
        return string.Join('\n', Enumerable.Range(0, row.Cells.Count)
            .SelectMany(i => row.Cells[i].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>())
            .Select(p => TextOf(p.Elements)));
    }

    /// <summary>Các đoạn trong thân phiếu (ngoài bảng), theo thứ tự in.</summary>
    private static IEnumerable<Mdom.Paragraph> BodyParagraphs(Mdom.Document layout) =>
        layout.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>();

    /// <summary>Chữ của từng đoạn trong thân phiếu (ngoài bảng), theo thứ tự in; tab là "\t", xuống dòng là "\n".</summary>
    private static List<string> BodyLines(Mdom.Document layout) =>
        BodyParagraphs(layout).Select(p => TextOf(p.Elements)).ToList();

    private static string TextOf(Mdom.ParagraphElements elements) =>
        string.Concat(elements.Cast<Mdom.DocumentObject>().Select(element => element switch
        {
            Mdom.Text text => text.Content,
            Mdom.FormattedText formatted => TextOf(formatted.Elements),
            Mdom.Character { SymbolName: Mdom.SymbolName.Tab } => "\t",
            Mdom.Character { SymbolName: Mdom.SymbolName.LineBreak } => "\n",
            _ => string.Empty,
        }));

    /// <summary>Mọi đoạn chữ có định dạng trong thân phiếu (ngoài bảng).</summary>
    private static IEnumerable<Mdom.FormattedText> BodyFormattedTexts(Mdom.Document layout) =>
        BodyParagraphs(layout).SelectMany(p => p.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.FormattedText>());

    [Theory]
    [InlineData("QT-KNGT-BM02", false)]
    [InlineData("QT-KNGT-BM10", true)]
    [InlineData("TTNB-NGOAITRU", false)]
    [InlineData("TUVAN-CONGTT", false)]
    public void DepartmentLine_IsLeftOut_ForFormsWhosePaperVersionHasNone(string code, bool printed)
    {
        var head = HeadText(MigraDocConsentPdfRenderer.Build(HospitalDocument(code)));

        Assert.Contains("BỆNH VIỆN", head, StringComparison.Ordinal);
        Assert.Equal(printed, head.Contains("Khoa/phòng: Khoa Khám bệnh", StringComparison.Ordinal));
    }

    [Fact]
    public void BilingualForm_PrintsThePatientSectionWithEnglishLabels()
    {
        var bilingual = HospitalDocument("TUVAN-CONGTT");
        var vietnameseOnly = bilingual with { Definition = bilingual.Definition with { Bilingual = false } };

        var layout = MigraDocConsentPdfRenderer.Build(bilingual);

        // Nhãn song ngữ dài hơn: mã người bệnh sang cột trái, cột phải chỉ có mục ngắn để số không bị xuống dòng.
        Assert.Equal(
            [
                "I. Thông tin người bệnh (Patient information)",
                "Họ và tên (Full name): PHẠM THỊ DUNG (GIẢ LẬP)\tGiới tính (Gender): Nữ",
                "Ngày sinh (Date of birth): 05/04/1988 (38 tuổi)\tĐiện thoại (Phone): 0900000125",
                "Mã người bệnh (Patient ID): BN000125\tSố CCCD (ID No.): 036188000125",
                "Địa chỉ (Address): Số 9 Đường Thử Nghiệm, Phường Mẫu",
            ],
            PatientSection(layout));
        Assert.Equal(
            [
                "I. Thông tin người bệnh",
                "Họ và tên: PHẠM THỊ DUNG (GIẢ LẬP)\tGiới tính: Nữ",
                "Ngày sinh: 05/04/1988 (38 tuổi)\tMã người bệnh: BN000125",
                "Số CCCD: 036188000125\tĐiện thoại: 0900000125",
                "Địa chỉ: Số 9 Đường Thử Nghiệm, Phường Mẫu",
            ],
            PatientSection(MigraDocConsentPdfRenderer.Build(vietnameseOnly)));

        // Phần tiếng Anh in nghiêng, màu xanh như các phần song ngữ khác của phiếu.
        var english = Assert.Single(BodyFormattedTexts(layout), t => Plain(t) == "(Full name)");
        Assert.True(english.Italic);
        Assert.Equal(MigraDocConsentPdfRenderer.English, english.Color);
    }

    /// <summary>Bản giấy BM02 (mã QT-KNGT/BM02): logo bên trái, quốc hiệu ở giữa, ô mã tài liệu bên phải.</summary>
    [Fact]
    public void DocumentControlHeader_PrintsLogoMottoAndDocumentCode_LikeThePaperForm()
    {
        var logo = LogoImage.Read(Convert.FromBase64String(TestLogos.Png64))!;
        var withLogo = MigraDocConsentPdfRenderer.Build(HospitalDocument("QT-KNGT-BM02") with { Logo = logo });
        var withoutLogo = MigraDocConsentPdfRenderer.Build(HospitalDocument("QT-KNGT-BM02"));
        var standard = MigraDocConsentPdfRenderer.Build(HospitalDocument("TTNB-NGOAITRU"));

        var head = withLogo.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().First().Rows[0];
        Assert.Equal(3, head.Cells.Count);
        Assert.Single(head.Cells[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>()
            .SelectMany(p => p.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Shapes.Image>()));
        var text = HeadText(withLogo);
        Assert.Contains("CỘNG HÒA XÃ HỘI CHỦ NGHĨA VIỆT NAM\nĐộc lập - Tự do - Hạnh phúc", text, StringComparison.Ordinal);
        Assert.EndsWith(
            "Mã số (Code No): QT-KNGT/BM02\nLần ban hành (Issue No): 09\nNgày hiệu lực (Effective Date): ", text, StringComparison.Ordinal);
        // Có logo thì không in tên đơn vị (logo đã là nhận diện); chưa tải logo thì ô trái in tên đơn vị.
        Assert.DoesNotContain("BỆNH VIỆN", text, StringComparison.Ordinal);
        Assert.Contains("BỆNH VIỆN", HeadText(withoutLogo), StringComparison.Ordinal);
        // Dòng "MS … Số phiếu" dưới đầu phiếu chỉ còn ở mẫu thường (số phiếu vẫn in ở chân trang).
        Assert.DoesNotContain(BodyLines(withLogo), l => l.StartsWith("MS:", StringComparison.Ordinal));
        Assert.Contains(BodyLines(standard), l => l.StartsWith("MS: TTNB-NGOAITRU", StringComparison.Ordinal));
    }

    [Fact]
    public void Bm02_PrintsTitlePatientLinesAndSchemeLikeThePaperForm()
    {
        var layout = MigraDocConsentPdfRenderer.Build(HospitalDocument("QT-KNGT-BM02"));
        var lines = BodyLines(layout);

        // Tiêu đề 4 dòng: dòng trong ngoặc không viết hoa, nghiêng; tiếng Việt đậm, tiếng Anh xanh không đậm.
        Assert.Contains(
            "GIẤY ĐỒNG THUẬN\n(Về chế độ thanh toán và quyền lợi khám chữa bệnh Bảo hiểm y tế)\nCONSENT FORM\n"
            + "(Regarding Payment Policy and Health Insurance Benefits for Medical Examination and Treatment)",
            lines);
        var texts = BodyFormattedTexts(layout).ToList();
        var vietnameseSubLine = Assert.Single(texts, t => Plain(t).StartsWith("(Về chế độ", StringComparison.Ordinal));
        Assert.True(vietnameseSubLine.Bold);
        Assert.True(vietnameseSubLine.Italic);
        var englishSubLine = Assert.Single(texts, t => Plain(t).StartsWith("(Regarding", StringComparison.Ordinal));
        Assert.False(englishSubLine.Bold);
        Assert.Equal(MigraDocConsentPdfRenderer.English, englishSubLine.Color);

        // Thông tin người bệnh theo dòng của bản giấy; số thẻ BHYT (trường của phiếu) in ở đây, không in lại bên dưới.
        Assert.Equal(
            [
                "Họ tên người bệnh / Patient’s Full Name: PHẠM THỊ DUNG (GIẢ LẬP)",
                "Ngày sinh/ Date of birth: 05/04/1988\tGiới tính/ Gender: ☐ Nam/Male\u00A0\u00A0\u00A0 ☒ Nữ/Female",
                "Mã BN (nếu có)/ Patient Identification Number (if applicable): BN000125",
                "Số thẻ Bảo hiểm y tế/ Health Insurance Card Number: GD4790000000125",
            ],
            lines.SkipWhile(l => !l.StartsWith("Họ tên người bệnh", StringComparison.Ordinal)).Take(4));
        Assert.DoesNotContain(lines, l => l.StartsWith("I. Thông tin người bệnh", StringComparison.Ordinal));
        Assert.Single(lines, l => l.Contains("Health Insurance Card Number", StringComparison.Ordinal));

        // Câu chọn chế độ: chỉ hai ô (đoạn văn trước đã là câu hỏi), không in nhãn câu hỏi.
        Assert.Contains(
            "☒ Bảo hiểm y tế/ Health Insurance\u00A0\u00A0\u00A0 \u00A0\u00A0\u00A0 ☐ Không Bảo hiểm y tế/ Non–Health Insurance", lines);
        Assert.DoesNotContain(lines, l => l.Contains("Chế độ khám chữa bệnh", StringComparison.Ordinal));
    }

    [Fact]
    public void Bm02_StaffSignsOnTheLeft_WithTitlesAsWritten_AndABilingualDateLine()
    {
        var bm02 = MigraDocConsentPdfRenderer.Build(HospitalDocument("QT-KNGT-BM02"));
        var standard = MigraDocConsentPdfRenderer.Build(HospitalDocument("TTNB-NGOAITRU"));

        Assert.Contains(BodyLines(bm02), l => l.EndsWith(", ngày (day) 11 tháng (month) 11 năm (year) 2026", StringComparison.Ordinal));
        Assert.Contains(BodyLines(standard), l => l.EndsWith(", ngày 11 tháng 11 năm 2026", StringComparison.Ordinal));
        Assert.Equal(
            ["Nhân viên hướng dẫn/ Counseling Staff", "(Ký, ghi rõ họ tên)\n(Signature and full name)"], SignatureCell(bm02, 0).Take(2));
        Assert.Equal(["Người thỏa thuận/ Declarant", "(Ký, ghi rõ họ tên)\n(Signature and full name)"], SignatureCell(bm02, 1).Take(2));
        Assert.DoesNotContain(SignatureTitleRuns(bm02, 0), t => t.Bold);
        // Mẫu thường: người bệnh bên trái, tên ô ký viết hoa, in đậm.
        Assert.Equal("NGƯỜI KHAI (DECLARANT)", SignatureCell(standard, 0).First());
        Assert.All(SignatureTitleRuns(standard, 0), t => Assert.True(t.Bold));
    }

    /// <summary>Bệnh viện yêu cầu 15/11/2026: ô đã chọn có dấu X bên trong (☒), ô chưa chọn để trống (☐).</summary>
    [Fact]
    public void CheckedBoxes_HaveAnXInside_PrintedWithTheEmbeddedBoxFont()
    {
        var document = HospitalDocument("QT-KNGT-BM02");

        var boxes = BodyFormattedTexts(MigraDocConsentPdfRenderer.Build(document))
            .Where(t => Plain(t) is MigraDocConsentPdfRenderer.Checked or MigraDocConsentPdfRenderer.Unchecked)
            .ToList();
        var pdf = Encoding.Latin1.GetString(new MigraDocConsentPdfRenderer().Render(document));

        // Giới tính Nữ và chế độ Bảo hiểm y tế đã chọn: hai ô có dấu X, hai ô trống.
        Assert.Equal(2, boxes.Count(t => Plain(t) == "☒"));
        Assert.Equal(2, boxes.Count(t => Plain(t) == "☐"));
        // Tinos không có hai ký tự này: in bằng font DejaVu Sans nhúng sẵn, chữ thường màu đen kể cả trên dòng in đậm.
        Assert.All(boxes, t =>
        {
            Assert.Equal(EmbeddedFontResolver.BoxFamilyName, t.FontName);
            Assert.False(t.Bold);
        });
        // Font được nhúng (tập con, tên dạng "ABCDEF+DejaVu Sans") nên in đúng trên mọi máy.
        Assert.Contains("+DejaVu", pdf, StringComparison.Ordinal);
    }

    /// <summary>Ô Đồng ý/Không đồng ý: chỉ ô của ý kiến đã ghi nhận có dấu X; bản xem trước (chưa có ý kiến) để trống cả hai ô.</summary>
    [Theory]
    [InlineData(Consent.Domain.ConsentForms.ConsentDecision.Agreed, "☒", "☐")]
    [InlineData(Consent.Domain.ConsentForms.ConsentDecision.Refused, "☐", "☒")]
    [InlineData(null, "☐", "☐")]
    public void DecisionBoxes_MarkOnlyTheRecordedDecision(Consent.Domain.ConsentForms.ConsentDecision? decision, string agree, string refuse)
    {
        var layout = MigraDocConsentPdfRenderer.Build(HospitalDocument("TUVAN-CONGTT") with { Decision = decision });

        var line = Assert.Single(BodyParagraphs(layout), p => TextOf(p.Elements).EndsWith("Không đồng ý (Disagree)", StringComparison.Ordinal));
        Assert.Equal($"{agree} Đồng ý (Agree)    {refuse} Không đồng ý (Disagree)", TextOf(line.Elements));
        Assert.Equal([agree, refuse], Boxes(line));
    }

    /// <summary>Ô xác nhận ("Tôi đã được giải thích…"): có dấu X khi đã đánh dấu, để trống khi chưa.</summary>
    [Theory]
    [InlineData(true, "☒")]
    [InlineData(false, "☐")]
    public void ConfirmBox_HasAnXOnlyWhenConfirmed(bool acknowledged, string box)
    {
        const string Label = "Tôi đã được giải thích, đọc và hiểu nội dung cam kết trên.";
        var document = Document("CK-01", representative: false, isFinal: true);
        var data = document.Data.DeepClone().AsObject();
        data["acknowledged"] = acknowledged;

        var layout = MigraDocConsentPdfRenderer.Build(document with { Data = data });

        var line = Assert.Single(BodyParagraphs(layout), p => TextOf(p.Elements).EndsWith(Label, StringComparison.Ordinal));
        Assert.Equal($"{box}\t{Label}", TextOf(line.Elements));
        Assert.Equal([box], Boxes(line));
    }

    /// <summary>Các ô (☒/☐) trong đoạn, theo thứ tự in; mỗi ô phải in bằng font ô nhúng sẵn, không đậm.</summary>
    private static List<string> Boxes(Mdom.Paragraph paragraph)
    {
        var boxes = paragraph.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.FormattedText>()
            .Where(t => Plain(t) is MigraDocConsentPdfRenderer.Checked or MigraDocConsentPdfRenderer.Unchecked)
            .ToList();
        Assert.All(boxes, t =>
        {
            Assert.Equal(EmbeddedFontResolver.BoxFamilyName, t.FontName);
            Assert.False(t.Bold);
        });
        return boxes.Select(Plain).ToList();
    }

    /// <summary>Bệnh viện yêu cầu 15/11/2026: logo nhỏ lại, cạnh trên logo thẳng cạnh trên chữ quốc hiệu (cả hai kiểu đầu phiếu).</summary>
    [Theory]
    [InlineData("QT-KNGT-BM02")]
    [InlineData("TTNB-NGOAITRU")]
    public void Logo_IsAtMost15mmTall_AndTopAlignedWithTheMotto(string code)
    {
        var logo = LogoImage.Read(TestPng.Rgba(300, 300, (x, y) => true, whiteBackground: true))!;
        var head = MigraDocConsentPdfRenderer.Build(HospitalDocument(code) with { Logo = logo })
            .Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().First().Rows[0];
        var logoParagraph = head.Cells[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().Single();
        var image = logoParagraph.Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Shapes.Image>().Single();

        Assert.Equal(15, image.Height.Millimeter, 2);
        Assert.Equal(Mdom.Tables.VerticalAlignment.Top, head.VerticalAlignment);
        // Logo đẩy xuống từ đỉnh dòng đến đỉnh chữ hoa của quốc hiệu (khoảng 0,8–0,9 mm).
        Assert.InRange(logoParagraph.Format.SpaceBefore.Millimeter, 0.7, 1.0);
    }

    /// <summary>Các đoạn chữ của ô ký thứ <paramref name="column"/> ở hàng đầu bảng chữ ký: tên ô ký, ghi chú, …</summary>
    private static IEnumerable<string> SignatureCell(Mdom.Document layout, int column) =>
        SignatureRoles(layout).Cells[column].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().Select(p => TextOf(p.Elements));

    private static IEnumerable<Mdom.FormattedText> SignatureTitleRuns(Mdom.Document layout, int column) =>
        SignatureRoles(layout).Cells[column].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Paragraph>().First()
            .Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.FormattedText>();

    private static Mdom.Tables.Row SignatureRoles(Mdom.Document layout) =>
        layout.Sections[0].Elements.Cast<Mdom.DocumentObject>().OfType<Mdom.Tables.Table>().Last().Rows[0];

    /// <summary>Năm dòng của mục "I. Thông tin người bệnh": tiêu đề, ba dòng hai cột, địa chỉ.</summary>
    private static IEnumerable<string> PatientSection(Mdom.Document layout) =>
        BodyLines(layout).SkipWhile(l => !l.StartsWith("I. Thông tin người bệnh", StringComparison.Ordinal)).Take(5);

    [Fact]
    public void OutpatientInformation_PrintsOccupationBesideEducation_AndBothEmergencyAnswersOnOneLine()
    {
        var lines = BodyLines(MigraDocConsentPdfRenderer.Build(HospitalDocument("TTNB-NGOAITRU")));

        var education = Assert.Single(lines, l => l.StartsWith("Trình độ văn hóa (Education Level): 12/12", StringComparison.Ordinal));
        Assert.EndsWith("Nghề nghiệp (Occupation): Nhân viên văn phòng", education, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, l => l.StartsWith("Nghề nghiệp", StringComparison.Ordinal));
        var emergency = Assert.Single(lines, l => l.StartsWith("Đây có phải là thông tin của người liên hệ khẩn cấp không?", StringComparison.Ordinal));
        Assert.Contains("☐ Có (Yes)", emergency, StringComparison.Ordinal);
        Assert.Contains("☒ Không, điền thêm thông tin dưới đây", emergency, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("no", new[] { "A: 1\u00A0\u00A0\u00A0 C: 3", "Đoạn văn", "D: 11/11/2026" })]
    [InlineData("yes", new[] { "A: 1", "B: 2\u00A0\u00A0\u00A0 C: 3", "Đoạn văn", "D: 11/11/2026" })]
    public void SameLineField_ContinuesTheLineOfThePreviousPrintedField(string showB, string[] expected)
    {
        // B chỉ in khi chọn "Có": ẩn B thì C in tiếp dòng của A. D đứng sau đoạn văn nên xuống dòng mới.
        var definition = TemplateDefinitionValidator.Parse("""
            {"title":"T","sections":[{"blocks":[
              {"type":"choice","key":"showB","label":"Hiện B","layout":"inline","options":[{"value":"yes","label":"Có"},{"value":"no","label":"Không"}]},
              {"type":"text","key":"a","label":"A"},
              {"type":"text","key":"b","label":"B","visibleWhen":{"field":"showB","in":["yes"]}},
              {"type":"text","key":"c","label":"C","sameLine":true},
              {"type":"paragraph","text":"Đoạn văn"},
              {"type":"date","key":"d","label":"D","sameLine":true}]}]}
            """).Definition!;
        var evaluation = FormDataEvaluator.Evaluate(
            definition, new JsonObject { ["showB"] = showB, ["a"] = "1", ["b"] = "2", ["c"] = "3", ["d"] = "2026-11-11" });
        var document = new ConsentPdfDocument(
            new HospitalOptions(), "Khoa Khám bệnh", "T-01", 1, "PDT-2026-000001", definition, evaluation.Data, evaluation.Scores,
            Patient, null, new DateOnly(2026, 11, 11), IsFinal: true);

        var lines = BodyLines(MigraDocConsentPdfRenderer.Build(document));

        Assert.Equal(expected, lines.SkipWhile(l => !l.StartsWith("Hiện B", StringComparison.Ordinal)).Skip(1).Take(expected.Length));
    }

    [Fact]
    public void Logo_ThatCannotBeDecoded_IsSkipped_AndThePdfStillRenders()
    {
        // Phần đầu PNG hợp lệ (qua được kiểm tra lúc tải lên) nhưng dữ liệu ảnh hỏng.
        var bytes = Convert.FromBase64String(TestLogos.Png64);
        Array.Fill(bytes, (byte)0x5A, 33, bytes.Length - 33);
        var logo = LogoImage.Read(bytes)!;

        var pdf = new MigraDocConsentPdfRenderer().Render(Document("CK-01", representative: false, isFinal: false) with { Logo = logo });

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.Equal(0, ImageCount(pdf));
    }

    [Fact]
    public void SignatureFrame_PrintsEachSignatureInsideTheFrame_AndAJpegFrameFallsBackToThePlainSignature()
    {
        var signature = SignatureImage.Normalize(TestPng.Signature()).Png!;
        var at = new DateTimeOffset(2026, 11, 11, 9, 30, 0, TimeSpan.FromHours(7));
        var signed = Document("CK-01", representative: false, isFinal: true, "bhyt") with
        {
            Decision = Consent.Domain.ConsentForms.ConsentDecision.Agreed,
            PatientSignature = new ConsentPdfSignature(signature, at),
        };

        // Khung giả lập 400 × 300 (ô "logo" ở góc trên bên trái), không phải khung thật của bệnh viện.
        var frame = LogoImage.Read(TestPng.Rgba(400, 300, (x, y) => x < 100 && y < 60, whiteBackground: true))!;
        var jpegFrame = LogoImage.Read(Convert.FromBase64String(TestLogos.Jpeg40x32))!;
        var renderer = new MigraDocConsentPdfRenderer();

        var plain = renderer.Render(signed);
        var framed = renderer.Render(signed with { SignatureFrame = frame });
        var jpeg = renderer.Render(signed with { SignatureFrame = jpegFrame });

        // Mỗi ô ký một ảnh rộng bằng ảnh khung: ô người bệnh là khung đã ghép chữ ký, ô nhân viên chưa ký là khung trống.
        Assert.Equal(2, ImageCount(framed));
        Assert.Equal(2, FrameImageCount(framed));
        Assert.DoesNotMatch(@"/Width\s+400\b", Encoding.Latin1.GetString(plain));
        Assert.Equal(ImageCount(plain), ImageCount(jpeg));
        Assert.DoesNotMatch(@"/Width\s+400\b", Encoding.Latin1.GetString(jpeg));
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, "CK-01-signed-framed.pdf"), framed);

            // Xem thử với một ảnh khung khác (đường dẫn PNG trong CONSENT_PDF_FRAME), có cả chữ ký mẫu của nhân viên.
            if (Environment.GetEnvironmentVariable("CONSENT_PDF_FRAME") is { Length: > 0 } framePath)
            {
                var sample = signed with
                {
                    SignatureFrame = LogoImage.Read(File.ReadAllBytes(framePath)),
                    HospitalConfirmation = new ConsentPdfConfirmation("Tiếp đón Demo", "Nhân viên tiếp đón", at.AddMinutes(2), signature),
                };
                File.WriteAllBytes(Path.Combine(output, "CK-01-signed-framed-sample.pdf"), renderer.Render(sample));
            }
        }
    }

    [Fact]
    public void SignatureFrame_IsPrintedEmptyWhereNobodyHasSignedYet_SoThePreviewShowsIt()
    {
        var frame = LogoImage.Read(TestPng.Rgba(400, 300, (x, y) => x < 100 && y < 60, whiteBackground: true))!;
        var preview = Document("CK-01", representative: false, isFinal: false, "bhyt");
        var renderer = new MigraDocConsentPdfRenderer();

        var withoutFrame = renderer.Render(preview);
        var withFrame = renderer.Render(preview with { SignatureFrame = frame });

        Assert.Equal(0, ImageCount(withoutFrame));
        // Ô người bệnh và ô nhân viên đều có khung trống: cùng một ảnh, lưu một lần trong PDF, vẽ ở hai chỗ.
        Assert.Equal(1, FrameImageCount(withFrame));
        Assert.Equal(2, ImagePlacements(withFrame));
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, "CK-01-preview-framed.pdf"), withFrame);
        }
    }

    private static int ImageCount(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Subtype\s*/Image").Count;

    /// <summary>Số ảnh rộng 400 điểm ảnh (bằng khung giả lập 400 × 300 trong các test).</summary>
    private static int FrameImageCount(byte[] pdf) => Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Width\s+400\b").Count;

    /// <summary>Số lần vẽ ảnh (toán tử <c>Do</c>) trên mọi trang.</summary>
    private static int ImagePlacements(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.Pages.Cast<PdfSharp.Pdf.PdfPage>().Sum(page => CountDo(PdfSharp.Pdf.Content.ContentReader.ReadContent(page)));

        static int CountDo(PdfSharp.Pdf.Content.Objects.CSequence sequence) => sequence.Sum(item => item switch
        {
            PdfSharp.Pdf.Content.Objects.COperator { OpCode.Name: "Do" } => 1,
            PdfSharp.Pdf.Content.Objects.CSequence inner => CountDo(inner),
            _ => 0,
        });
    }

    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        return document.PageCount;
    }

    /// <summary>
    /// <paramref name="maxPages"/>: cam kết khám BHYT thông thường (phổ biến nhất) gọn trong một trang; trường hợp nhiều
    /// nội dung nhất có thể sang trang sau nhưng khối chữ ký luôn nằm trọn trên một trang.
    /// </summary>
    [Theory]
    [InlineData("CK-01", false, "bhyt", 1)]
    [InlineData("CK-01", true, "bhyt", 1)]
    [InlineData("CK-01", true, null, 2)]
    [InlineData("TN-01", true, null, 3)]
    public void SignedForm_EmbedsTheSignatureImage_AndHospitalConfirmation(string code, bool representative, string? examType, int maxPages)
    {
        var signature = SignatureImage.Normalize(TestPng.Signature());
        Assert.True(signature.IsValid, signature.Error);
        var vietnam = TimeSpan.FromHours(7);
        var document = Document(code, representative, isFinal: true, examType) with
        {
            Decision = Consent.Domain.ConsentForms.ConsentDecision.Agreed,
            PatientSignature = new ConsentPdfSignature(signature.Png!, new DateTimeOffset(2026, 11, 11, 9, 30, 0, vietnam)),
            HospitalConfirmation = new ConsentPdfConfirmation("Tiếp đón Demo", "Nhân viên tiếp đón", new DateTimeOffset(2026, 11, 11, 9, 32, 0, vietnam)),
        };

        var bytes = new MigraDocConsentPdfRenderer().Render(document);
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(
                Path.Combine(output, $"{code}-signed{(representative ? "-representative" : string.Empty)}-{examType ?? "full"}.pdf"), bytes);
        }

        using (var stream = new MemoryStream(bytes))
        using (var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import))
        {
            Assert.InRange(pdf.PageCount, 1, maxPages);
        }

        // Ảnh chữ ký được nhúng (PDFsharp ghi "/Subtype/Image").
        Assert.Matches(@"/Subtype\s*/Image", Encoding.Latin1.GetString(bytes));
    }

    /// <summary>Dữ liệu giả lập đầy đủ cho bốn mẫu thật của bệnh viện.</summary>
    private static JsonObject HospitalData(string code) => code switch
    {
        "QT-KNGT-BM02" => new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" },
        "TTNB-NGOAITRU" => new JsonObject
        {
            ["ethnicity"] = "Kinh",
            ["religion"] = "other",
            ["religionOther"] = "Không theo tôn giáo",
            ["educationLevel"] = "12/12",
            ["occupation"] = "Nhân viên văn phòng",
            ["permanentAddress"] = "Số 9 Đường Thử Nghiệm, Phường Mẫu",
            ["currentAddress"] = "Số 9 Đường Thử Nghiệm, Phường Mẫu",
            ["familyName"] = "Phạm Văn Chồng (giả lập)",
            ["familyRelationship"] = "Chồng",
            ["familyPhone"] = "0900000777",
            ["familyIsEmergencyContact"] = "no",
            ["emergencyName"] = "Phạm Thị Em (giả lập)",
            ["emergencyRelationship"] = "Em gái",
            ["emergencyPhone"] = "0900000888",
        },
        "QT-KNGT-BM10" => new JsonObject
        {
            ["consultingRoom"] = "Phòng 05",
            ["selfFallHistory"] = "no",
            ["selfMobilityAid"] = "yes",
            ["infectionSymptoms"] = "yes",
            ["epidemiologicalExposure"] = "no",
            ["closeContact"] = "no",
            ["staffMobilityAid"] = "yes",
            ["staffUnsteady"] = "no",
            ["fallInterventions"] = new JsonArray("mobilityAid"),
            ["infectionAction"] = "maskContinue",
            ["pulse"] = "82",
            ["temperature"] = "37,8",
            ["bloodPressure"] = "125/80",
            ["respiratoryRate"] = "18",
            ["height"] = "158",
            ["weight"] = "45",
            ["bmi"] = "18,0",
            ["weight3MonthsAgo"] = "49",
            ["weightLossPercent"] = "8,9",
            ["emergencySigns"] = "no",
            ["visitReason"] = "newPatient",
            ["visitReasonDetail"] = "Mệt mỏi, xanh xao kéo dài",
            ["allergies"] = "yes",
            ["allergiesDetail"] = "Penicillin",
            ["mentalStatusAppropriate"] = "no",
            ["barrierLanguage"] = "no",
            ["barrierCognitive"] = "no",
            ["barrierSensory"] = "yes",
            ["barrierSensoryDetail"] = "Nghe kém tai trái",
            ["barrierReligious"] = "no",
            ["barrierCultural"] = "no",
            ["barrierMobility"] = "no",
            ["livingStatus"] = "withFamily",
            ["currentPain"] = "yes",
            ["painOnset"] = "Hai ngày trước",
            ["painLocation"] = "Thắt lưng",
            ["painScore"] = "4",
            ["painScale"] = "nrs",
            ["nutritionGroup"] = "adult",
            ["adultBmiScore"] = "below18_5",
            ["adultWeightLossScore"] = "from5to10",
            ["adultAcuteIllnessScore"] = "no",
            ["highFallRisk"] = "yes",
            ["nurseEduFall"] = "provided",
            ["nurseEduWound"] = "notNeeded",
            ["adultNutritionRisk"] = "high",
            ["dietCounseling"] = "required",
            ["dietRegimens"] = new JsonArray("ironDeficiency", "other"),
            ["dietRegimenOther"] = "Tăng đạm",
            ["physicianEduFall"] = "provided",
            ["physicianEduWound"] = "notRequired",
            ["physicianEduPain"] = "provided",
            ["physicianEduNewPatient"] = "provided",
            ["physicianEduOther"] = "notRequired",
        },
        _ => [],
    };

    /// <summary>
    /// Bốn mẫu thật: in được, chữ ký nằm trọn một trang. <paramref name="maxPages"/> là giới hạn để phát hiện bố cục
    /// bị vỡ (phiếu nhận định BM10 có nhiều mục nhất).
    /// </summary>
    [Theory]
    [InlineData("QT-KNGT-BM02", null, 2)]
    [InlineData("TTNB-NGOAITRU", null, 2)]
    [InlineData("QT-KNGT-BM10", null, 6)]
    [InlineData("TUVAN-CONGTT", Consent.Domain.ConsentForms.ConsentDecision.Agreed, 2)]
    public void HospitalTemplates_RenderSignedPdf(string code, Consent.Domain.ConsentForms.ConsentDecision? decision, int maxPages)
    {
        var definition = TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;
        var evaluation = FormDataEvaluator.Evaluate(definition, HospitalData(code));
        Assert.True(evaluation.IsValid, string.Join("; ", evaluation.Errors.Select(e => $"{e.Key}: {string.Join(",", e.Value)}")));
        var signature = SignatureImage.Normalize(TestPng.Signature());
        var vietnam = TimeSpan.FromHours(7);
        var document = new ConsentPdfDocument(
            new HospitalOptions(), "Khoa Khám bệnh", code, 1, "PDT-2026-000001", definition, evaluation.Data, evaluation.Scores,
            Patient, null, new DateOnly(2026, 11, 11), IsFinal: true)
        {
            Decision = decision,
            PatientSignature = new ConsentPdfSignature(
                signature.Png!, new DateTimeOffset(2026, 11, 11, 9, 30, 0, vietnam), "Tôi đã nghe, hiểu và đồng ý"),
            HospitalConfirmation = new ConsentPdfConfirmation("Nhân viên giả lập", "Nhân viên", new DateTimeOffset(2026, 11, 11, 9, 32, 0, vietnam)),
        };

        var bytes = new MigraDocConsentPdfRenderer().Render(document);

        // Có khung chữ ký (quyết định 26): khung giả lập 4:3, hoặc ảnh khung trong CONSENT_PDF_FRAME khi xem thử.
        var framePng = Environment.GetEnvironmentVariable("CONSENT_PDF_FRAME") is { Length: > 0 } framePath
            ? File.ReadAllBytes(framePath)
            : TestPng.Rgba(400, 300, (x, y) => x < 100 && y < 60, whiteBackground: true);
        var framed = new MigraDocConsentPdfRenderer().Render(document with
        {
            SignatureFrame = LogoImage.Read(framePng),
            HospitalConfirmation = document.HospitalConfirmation! with { ImagePng = signature.Png },
        });
        // Bản xem trước (chưa ai ký) in khung trống ở các ô ký.
        var preview = new MigraDocConsentPdfRenderer().Render(document with
        {
            IsFinal = false, PatientSignature = null, HospitalConfirmation = null, SignatureFrame = LogoImage.Read(framePng),
        });
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, $"{code}-signed.pdf"), bytes);
            File.WriteAllBytes(Path.Combine(output, $"{code}-signed-framed.pdf"), framed);
            File.WriteAllBytes(Path.Combine(output, $"{code}-preview-framed.pdf"), preview);
        }

        Assert.Matches(@"/Subtype\s*/Image", Encoding.Latin1.GetString(preview));
        Assert.Equal(PageCount(framed), PageCount(preview));

        foreach (var rendered in new[] { bytes, framed })
        {
            using var stream = new MemoryStream(rendered);
            using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            Assert.InRange(pdf.PageCount, 1, maxPages);
            Assert.Matches(@"/Subtype\s*/Image", Encoding.Latin1.GetString(rendered));
        }
    }

    [Fact]
    public void Bm10_PrintsOneSignatureBoxPerConfirmationStep()
    {
        var definition = TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == "QT-KNGT-BM10").ContentJson).Definition!;
        var evaluation = FormDataEvaluator.Evaluate(definition, HospitalData("QT-KNGT-BM10"));
        var vietnam = TimeSpan.FromHours(7);
        var at = new DateTimeOffset(2026, 11, 11, 9, 40, 0, vietnam);
        var document = new ConsentPdfDocument(
            new HospitalOptions(), "Khoa Khám bệnh", "QT-KNGT-BM10", 1, "PDT-2026-000002", definition, evaluation.Data, evaluation.Scores,
            Patient, null, new DateOnly(2026, 11, 11), IsFinal: true)
        {
            PatientSignature = new ConsentPdfSignature(SignatureImage.Normalize(TestPng.Signature()).Png!, at),
            StepConfirmations =
            [
                new ConsentPdfConfirmation("Nhân Viên Hướng Dẫn (giả lập)", "Tiếp đón", at),
                new ConsentPdfConfirmation("Điều Dưỡng Thử Nghiệm (giả lập)", "Điều dưỡng", at),
            ],
            HospitalConfirmation = new ConsentPdfConfirmation("Bác Sĩ Thử Nghiệm (giả lập)", "Bác sĩ", at),
        };

        var bytes = new MigraDocConsentPdfRenderer().Render(document);
        if (Environment.GetEnvironmentVariable("CONSENT_PDF_OUT") is { Length: > 0 } output)
        {
            File.WriteAllBytes(Path.Combine(output, "QT-KNGT-BM10-steps.pdf"), bytes);
        }

        // Bản xem trước (chưa ai xác nhận) cũng in đủ bốn ô ký.
        var preview = new MigraDocConsentPdfRenderer().Render(document with
        {
            IsFinal = false, PatientSignature = null, StepConfirmations = null, HospitalConfirmation = null,
        });
        using var stream = new MemoryStream(bytes);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 1, 6);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(preview, 0, 4));
    }

    [Fact]
    public void BundleSignature_PrintsTheSignedTogetherLine()
    {
        var at = new DateTimeOffset(2026, 11, 15, 9, 12, 0, TimeSpan.FromHours(7));
        var signature = new ConsentPdfSignature(SignatureImage.Normalize(TestPng.Signature()).Png!, at, "Tôi đã nghe, hiểu và đồng ý");
        var alone = Document("CK-01", representative: false, isFinal: true) with { PatientSignature = signature };
        var together = alone with
        {
            PatientSignature = signature with { SignedTogether = "Ký chung một lần cho 4 phiếu: PDT-2026-000101, PDT-2026-000102, PDT-2026-000103, PDT-2026-000104" },
        };
        var renderer = new MigraDocConsentPdfRenderer();

        var withLine = renderer.Render(together);
        var withoutLine = renderer.Render(alone);

        using var stream = new MemoryStream(withLine);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 1, 3);
        Assert.True(withLine.Length > withoutLine.Length);
    }

    [Fact]
    public void Encounter_PrintsTheReconciliationLineInTheFooter()
    {
        var plain = Document("CK-01", representative: false, isFinal: true);
        var linked = plain with
        {
            Encounter = new ConsentEncounter(
                "TN-2026-0001", new DateTimeOffset(2026, 11, 11, 7, 30, 0, TimeSpan.FromHours(7)), "Phòng khám 1", "Người tiếp đón giả lập"),
        };
        var renderer = new MigraDocConsentPdfRenderer();

        var withLine = renderer.Render(linked);
        var withoutLine = renderer.Render(plain);

        using var stream = new MemoryStream(withLine);
        using var pdf = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.InRange(pdf.PageCount, 1, 3);
        Assert.True(withLine.Length > withoutLine.Length);
    }

    [Fact]
    public void FinalAndPreview_DifferOnlyByPreviewNotice()
    {
        var renderer = new MigraDocConsentPdfRenderer();

        var preview = renderer.Render(Document("CK-01", representative: false, isFinal: false));
        var final = renderer.Render(Document("CK-01", representative: false, isFinal: true));

        Assert.True(preview.Length > final.Length);
    }
}
