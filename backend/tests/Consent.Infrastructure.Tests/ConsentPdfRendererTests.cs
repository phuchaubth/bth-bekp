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
