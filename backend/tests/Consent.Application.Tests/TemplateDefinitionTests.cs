using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Tests.Samples;

namespace Consent.Application.Tests;

public sealed class TemplateDefinitionTests
{
    public static TheoryData<string> SampleCodes => new() { "CK-01", "TT-01", "TN-01" };

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
}
