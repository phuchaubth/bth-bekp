using System.Text;
using System.Text.Json.Nodes;
using Consent.Application.ConsentForms;
using Consent.Application.ConsentTemplates;
using Consent.Domain.Patients;
using Consent.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;

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
        var definition = TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;
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

    [Fact]
    public void FinalAndPreview_DifferOnlyByPreviewNotice()
    {
        var renderer = new MigraDocConsentPdfRenderer();

        var preview = renderer.Render(Document("CK-01", representative: false, isFinal: false));
        var final = renderer.Render(Document("CK-01", representative: false, isFinal: true));

        Assert.True(preview.Length > final.Length);
    }
}
