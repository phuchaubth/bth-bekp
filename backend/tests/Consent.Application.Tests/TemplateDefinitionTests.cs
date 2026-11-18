using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Tests.Samples;

namespace Consent.Application.Tests;

public sealed class TemplateDefinitionTests
{
    public static TheoryData<string> SampleCodes => new() { "CK-01", "TT-01", "TN-01" };

    /// <summary>Dài hơn giới hạn 50 ký tự của mã tài liệu.</summary>
    private const string LongText = "QT-KNGT/BM02-QT-KNGT/BM02-QT-KNGT/BM02-QT-KNGT/BM02";

    private static TemplateDefinition Default(string code) =>
        TemplateDefinitionValidator.Parse(SampleConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;

    [Theory]
    [MemberData(nameof(SampleCodes))]
    public void SampleTemplates_AreValid(string code)
    {
        var (definition, errors) = TemplateDefinitionValidator.Parse(SampleConsentTemplates.All.Single(t => t.Code == code).ContentJson);

        Assert.Empty(errors);
        Assert.NotNull(definition);
    }

    [Theory]
    [InlineData("{", "không đúng cấu trúc JSON")]
    [InlineData("{\"title\":\"\",\"sections\":[{\"blocks\":[]}]}", "Thiếu tiêu đề")]
    [InlineData("{\"title\":\"T\",\"sections\":[]}", "ít nhất một phần")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"slider\",\"key\":\"a\",\"label\":\"A\"}]}]}", "kiểu khối không hợp lệ")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"},{\"type\":\"text\",\"key\":\"a\",\"label\":\"B\"}]}]}", "bị trùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"1a\",\"label\":\"A\"}]}]}", "tên trường phải bắt đầu")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"a\",\"label\":\"A\"}]}]}", "ít nhất một lựa chọn")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\",\"pattern\":\"[\"}]}]}", "biểu thức kiểm tra")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\",\"visibleWhen\":{\"field\":\"b\",\"in\":[\"x\"]}},{\"type\":\"choice\",\"key\":\"b\",\"label\":\"B\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}]}]}]}", "trường đứng trước")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"q\",\"label\":\"Q\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}]},{\"type\":\"score\",\"key\":\"s\",\"label\":\"S\",\"sumOf\":[\"q\"],\"levels\":[{\"code\":\"low\",\"label\":\"Thấp\",\"min\":0}]}]}]}", "câu hỏi tính điểm")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"q\",\"label\":\"Q\",\"options\":[{\"value\":\"x\",\"label\":\"X\",\"score\":1}]},{\"type\":\"score\",\"key\":\"s\",\"label\":\"S\",\"sumOf\":[\"q\"],\"levels\":[{\"code\":\"a\",\"label\":\"A\",\"min\":5},{\"code\":\"b\",\"label\":\"B\",\"min\":1}]}]}]}", "tăng dần")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\",\"prefill\":\"patient.secret\"}]}]}", "nguồn tự điền")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"decision\":\"maybe\"}}", "\"decision\" chỉ nhận")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"steps\":[{\"key\":\"a\",\"roleCode\":\"NURSE\",\"label\":\"A\"},{\"key\":\"a\",\"roleCode\":\"DOCTOR\",\"label\":\"B\"}]}}", "mã bước xác nhận bị trùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"steps\":[{\"key\":\"a\",\"roleCode\":\"nurse role\",\"label\":\"A\"}]}}", "mã vai trò không hợp lệ")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"steps\":[{\"key\":\"a\",\"roleCode\":\"NURSE\",\"label\":\"\"}]}}", "phải có nhãn")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"steps\":[{\"key\":\"a1\",\"roleCode\":\"A\",\"label\":\"A\"},{\"key\":\"a2\",\"roleCode\":\"A\",\"label\":\"A\"},{\"key\":\"a3\",\"roleCode\":\"A\",\"label\":\"A\"},{\"key\":\"a4\",\"roleCode\":\"A\",\"label\":\"A\"},{\"key\":\"a5\",\"roleCode\":\"A\",\"label\":\"A\"},{\"key\":\"a6\",\"roleCode\":\"A\",\"label\":\"A\"}]}}", "tối đa 5 bước")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"w\",\"label\":\"W\"},{\"type\":\"text\",\"key\":\"h\",\"label\":\"H\"},{\"type\":\"computed\",\"key\":\"x\",\"label\":\"X\",\"formula\":\"bsa\",\"inputs\":[\"w\",\"h\"]}]}]}", "không được hỗ trợ")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"w\",\"label\":\"W\"},{\"type\":\"computed\",\"key\":\"bmi\",\"label\":\"BMI\",\"formula\":\"bmi\",\"inputs\":[\"w\"]}]}]}", "cần đúng 2 trường đầu vào")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"w\",\"label\":\"W\"},{\"type\":\"computed\",\"key\":\"bmi\",\"label\":\"BMI\",\"formula\":\"bmi\",\"inputs\":[\"w\",\"h\"]},{\"type\":\"text\",\"key\":\"h\",\"label\":\"H\"}]}]}", "\"h\" phải là trường văn bản đứng trước")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"b\",\"label\":\"B\"},{\"type\":\"choice\",\"key\":\"c\",\"label\":\"C\",\"valueFrom\":\"b\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}]}]}]}", "khoảng giá trị")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"c\",\"label\":\"C\",\"valueFrom\":\"bmi\",\"options\":[{\"value\":\"x\",\"label\":\"X\",\"min\":1}]}]}]}", "trường tự tính đứng trước")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"statement\":{\"suggestions\":[\"A\",\" \"]}}}", "câu gợi ý không được trống")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"statement\":{\"suggestions\":[\"1\",\"2\",\"3\",\"4\",\"5\",\"6\",\"7\",\"8\",\"9\"]}}}", "tối đa 8 câu gợi ý")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"},{\"type\":\"choice\",\"key\":\"b\",\"label\":\"B\",\"sameLine\":true,\"options\":[{\"value\":\"x\",\"label\":\"X\"}]}]}]}", "\"sameLine\"")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"},{\"type\":\"paragraph\",\"text\":\"P\",\"sameLine\":true}]}]}", "\"sameLine\"")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\",\"printOptionsOnly\":true}]}]}", "\"printOptionsOnly\"")]
    [InlineData("{\"title\":\"T\",\"documentControl\":{\"code\":\"" + LongText + "\"},\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "\"documentControl\"")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[],\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "từ 1 đến 20 dòng")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"patient\":\"fullName\"},{\"patient\":\"gender\"},{\"patient\":\"address\"}]],\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "có 1 hoặc 2 mục")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"patient\":\"religion\"}]],\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "\"religion\" không in được")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"patient\":\"fullName\",\"field\":\"a\"}]],\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"}]}]}", "đúng một trong hai")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"c\"}]],\"sections\":[{\"blocks\":[{\"type\":\"confirm\",\"key\":\"c\",\"label\":\"C\"}]}]}", "\"c\" phải là trường văn bản")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"a\",\"other\":\"b\"}]],\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"},{\"type\":\"text\",\"key\":\"b\",\"label\":\"B\"}]}]}", "\"other\" chỉ dùng kèm một câu hỏi chọn")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"c\",\"other\":\"d\"}]],\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"c\",\"label\":\"C\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}]},{\"type\":\"date\",\"key\":\"d\",\"label\":\"D\"}]}]}", "\"other\" phải là trường văn bản")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"c\",\"other\":\"b\"}],[{\"field\":\"b\"}]],\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"c\",\"label\":\"C\",\"options\":[{\"value\":\"x\",\"label\":\"X\"}]},{\"type\":\"text\",\"key\":\"b\",\"label\":\"B\"}]}]}", "\"b\" đã in ở dòng khác")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"d\",\"options\":[\"A\",\"B\"]}]],\"sections\":[{\"blocks\":[{\"type\":\"date\",\"key\":\"d\",\"label\":\"D\"}]}]}", "\"options\" của trường chỉ dùng cho trường văn bản")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"a\",\"options\":[\"Kinh\"]}]],\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"}]}]}", "từ 2 đến 6 nhãn")]
    [InlineData("{\"title\":\"T\",\"documentControl\":{\"codeLabel\":\"" + LongText + "\"},\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "các nhãn mỗi mục tối đa")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"field\":\"a\"}],[{\"field\":\"a\"}]],\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\"}]}]}", "đã in ở dòng khác")]
    [InlineData("{\"title\":\"T\",\"patientLines\":[[{\"patient\":\"fullName\",\"options\":[\"A\",\"B\"]}]],\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "chỉ dùng cho giới tính")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"signer\":\"doctor\",\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"steps\":[{\"key\":\"nurse\",\"roleCode\":\"NURSE\",\"label\":\"N\"}]}}", "Phần 1: người ký cuối phần (\"signer\") chỉ nhận")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]},{\"signer\":\"patient\",\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]},{\"signer\":\"patient\",\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "Phần 3: người ký \"patient\" đã in ở cuối một phần khác")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"text\",\"key\":\"a\",\"label\":\"A\",\"layout\":\"row\"}]}]}", "(một hàng của bảng) chỉ dùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"multiChoice\",\"key\":\"a\",\"label\":\"A\",\"layout\":\"row\",\"options\":[{\"value\":\"v0\",\"label\":\"V0\"},{\"value\":\"v1\",\"label\":\"V1\"}]}]}]}", "(một hàng của bảng) chỉ dùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"a\",\"label\":\"A\",\"layout\":\"row\",\"options\":[{\"value\":\"v0\",\"label\":\"V0\"}]}]}]}", "(một hàng của bảng) chỉ dùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"a\",\"label\":\"A\",\"layout\":\"row\",\"options\":[{\"value\":\"v0\",\"label\":\"V0\"},{\"value\":\"v1\",\"label\":\"V1\"},{\"value\":\"v2\",\"label\":\"V2\"},{\"value\":\"v3\",\"label\":\"V3\"},{\"value\":\"v4\",\"label\":\"V4\"}]}]}]}", "(một hàng của bảng) chỉ dùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"choice\",\"key\":\"a\",\"label\":\"A\",\"layout\":\"row\",\"printOptionsOnly\":true,\"options\":[{\"value\":\"v0\",\"label\":\"V0\"},{\"value\":\"v1\",\"label\":\"V1\"}]}]}]}", "(một hàng của bảng) chỉ dùng")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\",\"box\":true}]}]}", "\"box\" (in trong khung) chỉ dùng cho danh sách")]
    [InlineData("{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"list\",\"items\":[\"A\"],\"heading\":true}]}]}", "\"heading\" (in thành tiêu đề mục) chỉ dùng cho đoạn văn")]
    [InlineData("{\"title\":\"T\",\"documentControl\":{\"code\":\"QT-01\",\"unitInHead\":true},\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}", "\"unitInHead\" (tên đơn vị trên logo) chỉ dùng kèm")]
    public void InvalidDefinitions_AreRejected_WithVietnameseMessage(string json, string expectedFragment)
    {
        var (definition, errors) = TemplateDefinitionValidator.Parse(json);

        Assert.Null(definition);
        Assert.Contains(errors, e => e.Contains(expectedFragment, StringComparison.Ordinal));
    }

    [Fact]
    public void CK01_ServiceExam_HidesAndDropsInsuranceFields()
    {
        var data = new JsonObject
        {
            ["examType"] = "service",
            ["insuranceNumber"] = "GD4790000000125",
            ["acknowledged"] = true,
            ["unknownField"] = "bị bỏ",
        };

        var result = FormDataEvaluator.Evaluate(Default("CK-01"), data);

        Assert.True(result.IsValid);
        Assert.Null(result.Data["insuranceNumber"]);
        Assert.Null(result.Data["unknownField"]);
        Assert.Equal("service", result.Data["examType"]!.GetValue<string>());
    }

    [Fact]
    public void CK01_InsuranceExam_RequiresCardDetails_AndChecksFormat()
    {
        var data = new JsonObject { ["examType"] = "bhyt", ["insuranceNumber"] = "sai-ma", ["acknowledged"] = true };

        var result = FormDataEvaluator.Evaluate(Default("CK-01"), data);

        Assert.Contains("Số thẻ BHYT gồm 17 ký tự", result.Errors["insuranceNumber"][0], StringComparison.Ordinal);
        Assert.Contains("insuranceValidFrom", result.Errors.Keys);
        Assert.Contains("primaryCareFacility", result.Errors.Keys);
        Assert.Contains("referralPaper", result.Errors.Keys);
        Assert.DoesNotContain("requestedServices", result.Errors.Keys);
    }

    [Fact]
    public void CK01_OtherRequestedService_RequiresDescription()
    {
        var data = new JsonObject
        {
            ["examType"] = "bhytWithService",
            ["insuranceNumber"] = "GD4790000000125",
            ["insuranceValidFrom"] = "2026-01-01",
            ["insuranceValidTo"] = "2026-12-31",
            ["primaryCareFacility"] = "Trạm Y tế Phường Mẫu",
            ["referralPaper"] = "no",
            ["requestedServices"] = new JsonArray("time", "other"),
            ["acknowledged"] = true,
        };

        var result = FormDataEvaluator.Evaluate(Default("CK-01"), data);

        Assert.Equal(["requestedServicesOther"], result.Errors.Keys);
    }

    [Fact]
    public void TT01_RequiresEveryProvidedItem_AndRecipientsWhenSharing()
    {
        var data = new JsonObject
        {
            ["providedInformation"] = new JsonArray("rights", "rules"),
            ["informationSharing"] = "share",
            ["informationRecipients"] = new JsonArray(new JsonObject { ["fullName"] = "Phạm Văn Chồng", ["phoneNumber"] = "123" }),
            ["consentRoutineCare"] = "agree",
            ["consentContact"] = "agree",
            ["consentResearch"] = "disagree",
        };

        var result = FormDataEvaluator.Evaluate(Default("TT-01"), data);

        Assert.Equal("Phải đánh dấu đủ tất cả các mục.", result.Errors["providedInformation"][0]);
        Assert.Contains(result.Errors["informationRecipients"], m => m.Contains("thiếu quan hệ", StringComparison.Ordinal));
        Assert.Contains(result.Errors["informationRecipients"], m => m.Contains("Số điện thoại", StringComparison.Ordinal));
    }

    [Fact]
    public void TT01_NotSharing_DropsRecipients()
    {
        var data = new JsonObject
        {
            ["providedInformation"] = new JsonArray("rights", "rules", "process", "costs", "privacy", "explanation", "safety", "feedback"),
            ["informationSharing"] = "none",
            ["informationRecipients"] = new JsonArray(new JsonObject { ["fullName"] = "Không dùng" }),
            ["consentRoutineCare"] = "agree",
            ["consentContact"] = "disagree",
            ["consentResearch"] = "disagree",
        };

        var result = FormDataEvaluator.Evaluate(Default("TT-01"), data);

        Assert.True(result.IsValid);
        Assert.Null(result.Data["informationRecipients"]);
    }

    private static JsonObject Morse(string fall, string diagnosis, string aid, string iv, string gait, string mental) => new()
    {
        ["assessmentReason"] = "initial",
        ["morseFallHistory"] = fall,
        ["morseSecondaryDiagnosis"] = diagnosis,
        ["morseAmbulatoryAid"] = aid,
        ["morseIntravenous"] = iv,
        ["morseGait"] = gait,
        ["morseMentalStatus"] = mental,
        ["measuresStandard"] = new JsonArray("callBell"),
    };

    [Theory]
    [InlineData("no", "no", "none", "no", "normal", "oriented", 0, "low")]
    [InlineData("no", "yes", "crutches", "no", "weak", "oriented", 40, "medium")]
    [InlineData("yes", "no", "none", "yes", "normal", "oriented", 45, "high")]
    [InlineData("yes", "yes", "furniture", "yes", "impaired", "overestimates", 125, "high")]
    public void TN01_ComputesMorseTotalAndLevel(
        string fall, string diagnosis, string aid, string iv, string gait, string mental, int total, string level)
    {
        var result = FormDataEvaluator.Evaluate(Default("TN-01"), Morse(fall, diagnosis, aid, iv, gait, mental));

        Assert.Equal(total, result.Scores["morseTotal"].Total);
        Assert.Equal(level, result.Scores["morseTotal"].LevelCode);
    }

    [Fact]
    public void TN01_MediumRisk_RequiresExtraMeasuresAndReassessment_LowRiskDoesNot()
    {
        var medium = FormDataEvaluator.Evaluate(Default("TN-01"), Morse("no", "yes", "crutches", "no", "weak", "oriented"));
        var low = FormDataEvaluator.Evaluate(Default("TN-01"), Morse("no", "no", "none", "no", "normal", "oriented"));

        Assert.Equal(["measuresMedium", "reassessAt"], medium.Errors.Keys.Order());
        Assert.True(low.IsValid);
    }

    [Fact]
    public void TN01_UnansweredQuestion_LeavesScoreEmpty()
    {
        var data = Morse("no", "no", "none", "no", "normal", "oriented");
        data.Remove("morseGait");

        var result = FormDataEvaluator.Evaluate(Default("TN-01"), data);

        Assert.Null(result.Scores["morseTotal"].Total);
        Assert.Contains("morseGait", result.Errors.Keys);
    }

    [Fact]
    public void WrongValueTypes_AreReported_NotThrown()
    {
        var data = new JsonObject { ["examType"] = 5, ["acknowledged"] = "có" };

        var result = FormDataEvaluator.Evaluate(Default("CK-01"), data);

        Assert.Equal("Giá trị không hợp lệ.", result.Errors["examType"][0]);
        Assert.Equal("Giá trị không hợp lệ.", result.Errors["acknowledged"][0]);
    }

    [Fact]
    public void KeepHidden_KeepsDraftValuesOfHiddenFields_WithoutValidatingThem()
    {
        // Chưa chọn hình thức khám nên ô BHYT đang ẩn; số thẻ điền sẵn vẫn phải còn khi lưu nháp.
        var data = new JsonObject { ["insuranceNumber"] = "sai-ma" };

        var draft = FormDataEvaluator.Evaluate(Default("CK-01"), data, keepHidden: true);
        var final = FormDataEvaluator.Evaluate(Default("CK-01"), data);

        Assert.Equal("sai-ma", draft.Data["insuranceNumber"]!.GetValue<string>());
        Assert.DoesNotContain("insuranceNumber", draft.Errors.Keys);
        Assert.Null(final.Data["insuranceNumber"]);
    }

    [Fact]
    public void PrintOptions_AreKeptWhenSaved_AndLeftOutWhenOff()
    {
        // Mọi trường dạng "nhãn: giá trị" đều in chung dòng được.
        const string json = """
            {"title":"T","hideDepartment":true,"bilingual":true,"sections":[{"blocks":[
              {"type":"text","key":"w","label":"W"},
              {"type":"text","key":"h","label":"H","sameLine":true},
              {"type":"computed","key":"bmi","label":"BMI","formula":"bmi","inputs":["w","h"],"sameLine":true},
              {"type":"date","key":"d","label":"D"},
              {"type":"datetime","key":"t","label":"T","sameLine":true}]}]}
            """;

        var (definition, errors) = TemplateDefinitionValidator.Parse(json);

        Assert.Empty(errors);
        Assert.True(definition!.HideDepartment);
        Assert.True(definition.Bilingual);
        Assert.Equal(["h", "bmi", "t"], definition.InputBlocks.Where(b => b.SameLine).Select(b => b.Key!));
        var saved = TemplateJson.Serialize(definition);
        Assert.Contains("\"hideDepartment\":true", saved, StringComparison.Ordinal);
        Assert.Contains("\"bilingual\":true", saved, StringComparison.Ordinal);
        Assert.Equal(3, saved.Split("\"sameLine\":true").Length - 1);
        // Danh sách tính toán InputBlocks không bị ghi thành bản sao "inputBlocks" (sửa ở bản sao không có tác dụng).
        Assert.DoesNotContain("inputBlocks", saved, StringComparison.Ordinal);

        // Mẫu không dùng các tùy chọn này: JSON lưu không có thuộc tính thừa.
        var plain = TemplateJson.Serialize(Default("CK-01"));
        Assert.DoesNotContain("hideDepartment", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("bilingual", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("sameLine", plain, StringComparison.Ordinal);
        foreach (var option in new[]
                 {
                     "documentControl", "patientLines", "printOptionsOnly", "hospitalFirst", "plainTitles", "numberedPatientLines", "patientHint",
                     "centerDate", "paperHints", "\"indent\":", "\"other\":", "banner", "newPage", "signer", "\"box\":", "heading",
                     "unitInHead", "footnote", "boldTitles",
                 })
        {
            Assert.DoesNotContain(option, plain, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PatientHint_IsLimitedLikeTheOtherSignatureLabels()
    {
        var json = "{\"title\":\"T\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}],\"signatures\":{\"patientHint\":\""
            + new string('x', TemplateDefinitionValidator.MaxLabelLength + 1) + "\"}}";

        var (_, errors) = TemplateDefinitionValidator.Parse(json);

        Assert.Contains(errors, e => e.Contains("nhãn quá dài", StringComparison.Ordinal));
    }

    /// <summary>Tùy chọn in theo bản giấy TTNB (quyết định 33) giữ nguyên khi lưu và đọc lại.</summary>
    [Fact]
    public void OutpatientPaperOptions_AreKeptWhenSaved()
    {
        const string json = """
            {"title":"T","numberedPatientLines":true,
             "documentControl":{"code":"QT-01","titleInHead":true,"codeLabel":"Mã số (Code)","issueNoLabel":"Lần (Issue)","effectiveDateLabel":"Ngày (Date)"},
             "patientLines":[[{"patient":"fullName"}],[{"field":"eth","options":["Kinh","Khác"]}],[{"field":"rel","other":"relOther"}],[{"field":"x","indent":true}]],
             "sections":[{"blocks":[
               {"type":"text","key":"eth","label":"Dân tộc"},
               {"type":"choice","key":"rel","label":"Tôn giáo","options":[{"value":"a","label":"A"},{"value":"other","label":"Khác"}]},
               {"type":"text","key":"relOther","label":"Khác, ghi rõ"},
               {"type":"text","key":"x","label":"X"}]}],
             "signatures":{"patientHint":"(Declarant)","centerDate":true,"paperHints":true}}
            """;

        var (definition, errors) = TemplateDefinitionValidator.Parse(json);
        var saved = TemplateDefinitionValidator.Parse(TemplateJson.Serialize(definition!)).Definition!;

        Assert.Empty(errors);
        Assert.True(saved.NumberedPatientLines);
        Assert.Equal(new TemplateDocumentControl("QT-01", null, null, true, "Mã số (Code)", "Lần (Issue)", "Ngày (Date)"), saved.DocumentControl);
        Assert.Equal(["Kinh", "Khác"], saved.PatientLines![1][0].Options!);
        Assert.Equal("relOther", saved.PatientLines[2][0].Other);
        Assert.True(saved.PatientLines[3][0].Indent);
        Assert.Equal("(Declarant)", saved.Signatures!.PatientHint);
        Assert.True(saved.Signatures.CenterDate);
        Assert.True(saved.Signatures.PaperHints);
    }

    [Fact]
    public void PaperLayoutOptions_AreKeptWhenSaved()
    {
        const string json = """
            {"title":"T","documentControl":{"code":"QT-01","issueNo":"02"},
             "patientLines":[[{"patient":"fullName","label":"Họ tên/ Full name"}],[{"patient":"gender","options":["Nam/Male","Nữ/Female"]},{"field":"card"}]],
             "sections":[{"blocks":[
               {"type":"choice","key":"plan","label":"P","printOptionsOnly":true,"options":[{"value":"a","label":"A"}]},
               {"type":"text","key":"card","label":"Số thẻ"}]}],
             "signatures":{"hospitalFirst":true,"plainTitles":true}}
            """;

        var (definition, errors) = TemplateDefinitionValidator.Parse(json);
        var saved = TemplateDefinitionValidator.Parse(TemplateJson.Serialize(definition!)).Definition!;

        Assert.Empty(errors);
        Assert.Equal(new TemplateDocumentControl("QT-01", "02"), saved.DocumentControl);
        Assert.Equal("card", saved.PatientLines![1][1].Field);
        Assert.Equal(["Nam/Male", "Nữ/Female"], saved.PatientLines[1][0].Options!);
        Assert.True(saved.InputBlocks.First().PrintOptionsOnly);
        Assert.True(saved.Signatures!.HospitalFirst);
        Assert.True(saved.Signatures.PlainTitles);
    }

    /// <summary>Tùy chọn in theo bản giấy QĐ-VHTT-BM02 và QT-KNGT/BM10 (quyết định 43) giữ nguyên khi lưu và đọc lại.</summary>
    [Fact]
    public void SectionAndBlockPaperOptions_AreKeptWhenSaved()
    {
        const string json = """
            {"title":"T","footnote":"Hướng dẫn\n* Ghi chú",
             "documentControl":{"code":"QT-01","titleInHead":true,"unitInHead":true},
             "sections":[
               {"title":"Phần 1","banner":true,"signer":"patient","blocks":[
                 {"type":"paragraph","text":"A - Té ngã","heading":true},
                 {"type":"choice","key":"a","label":"A","layout":"row","options":[{"value":"yes","label":"Có"},{"value":"no","label":"Không"}]},
                 {"type":"choice","key":"b","label":"B","layout":"row",
                  "options":[{"value":"1","label":"1"},{"value":"2","label":"2"},{"value":"3","label":"3"},{"value":"4","label":"4"}]}]},
               {"title":"Phần 2","banner":true,"newPage":true,"signer":"nurse","blocks":[
                 {"type":"list","label":"Lưu ý","layout":"ordered","box":true,"items":["Một","Hai"]}]},
               {"newPage":true,"signer":"hospital","blocks":[{"type":"paragraph","text":"P"}]}],
             "signatures":{"boldTitles":true,"steps":[{"key":"nurse","roleCode":"NURSE","label":"Điều dưỡng"}]}}
            """;

        var (definition, errors) = TemplateDefinitionValidator.Parse(json);
        var saved = TemplateDefinitionValidator.Parse(TemplateJson.Serialize(definition!)).Definition!;

        Assert.Empty(errors);
        Assert.Equal("Hướng dẫn\n* Ghi chú", saved.Footnote);
        Assert.True(saved.DocumentControl!.UnitInHead);
        Assert.Equal([true, true, false], saved.Sections.Select(s => s.Banner));
        Assert.Equal([false, true, true], saved.Sections.Select(s => s.NewPage));
        Assert.Equal(["patient", "nurse", "hospital"], saved.Sections.Select(s => s.Signer));
        var blocks = saved.Sections.SelectMany(s => s.Blocks).ToList();
        Assert.Equal([true, false, false, false, false], blocks.Select(b => b.Heading));
        Assert.Equal([TemplateBlock.LayoutRow, TemplateBlock.LayoutRow], blocks.Where(b => b.Type == BlockTypes.Choice).Select(b => b.Layout));
        Assert.Equal([false, false, false, true, false], blocks.Select(b => b.Box));
        Assert.True(saved.Signatures!.BoldTitles);
    }

    [Fact]
    public void Footnote_IsLimitedInLength()
    {
        static string WithFootnote(int length) =>
            "{\"title\":\"T\",\"footnote\":\"" + new string('x', length)
            + "\",\"sections\":[{\"blocks\":[{\"type\":\"paragraph\",\"text\":\"P\"}]}]}";

        var (_, longer) = TemplateDefinitionValidator.Parse(WithFootnote(TemplateDefinition.MaxFootnoteLength + 1));
        var (atLimit, none) = TemplateDefinitionValidator.Parse(WithFootnote(TemplateDefinition.MaxFootnoteLength));

        Assert.Contains(longer, e => e.Contains("Ghi chú cuối phiếu (\"footnote\"): tối đa 2000 ký tự", StringComparison.Ordinal));
        Assert.Empty(none);
        Assert.Equal(TemplateDefinition.MaxFootnoteLength, atLimit!.Footnote!.Length);
    }
}
