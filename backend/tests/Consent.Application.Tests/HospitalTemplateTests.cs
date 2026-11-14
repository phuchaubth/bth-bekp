using System.Text.Json.Nodes;
using Consent.Application.ConsentTemplates;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Identity;

namespace Consent.Application.Tests;

/// <summary>Bốn mẫu phiếu thật được khởi tạo sẵn (song ngữ, chuyển từ bản Word của bệnh viện).</summary>
public sealed class HospitalTemplateTests
{
    public static TheoryData<string> Codes => new() { "QT-KNGT-BM02", "TTNB-NGOAITRU", "QT-KNGT-BM10", "TUVAN-CONGTT" };

    private static TemplateDefinition Definition(string code) =>
        TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == code).ContentJson).Definition!;

    [Fact]
    public void DefaultTemplates_AreTheFourHospitalForms_WithExpectedSigners()
    {
        var roles = DefaultConsentTemplates.All.ToDictionary(t => t.Code, t => t.HospitalSignerRoleCode);

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["QT-KNGT-BM02"] = SystemRoles.Receptionist,
                ["TTNB-NGOAITRU"] = SystemRoles.Receptionist,
                ["QT-KNGT-BM10"] = SystemRoles.Doctor,
                ["TUVAN-CONGTT"] = SystemRoles.Nurse,
            },
            roles);
        Assert.All(DefaultConsentTemplates.All, t =>
        {
            Assert.Equal(ConsentTemplate.NormalizeCode(t.Code), t.Code);
            Assert.InRange(t.Code.Length, 1, ConsentTemplate.CodeMaxLength);
            Assert.InRange(t.Name.Length, 1, ConsentTemplate.NameMaxLength);
        });
    }

    [Theory]
    [MemberData(nameof(Codes))]
    public void DefaultTemplates_AreValid(string code)
    {
        var (definition, errors) = TemplateDefinitionValidator.Parse(DefaultConsentTemplates.All.Single(t => t.Code == code).ContentJson);

        Assert.Empty(errors);
        Assert.NotNull(definition);
    }

    [Theory]
    [InlineData("QT-KNGT-BM02", false)]
    [InlineData("TTNB-NGOAITRU", false)]
    [InlineData("QT-KNGT-BM10", false)]
    [InlineData("TUVAN-CONGTT", true)]
    public void OnlyThePortalConsultation_AsksForAgreeOrDisagree(string code, bool requiresDecision)
    {
        var signatures = Definition(code).Signatures!;

        Assert.Equal(requiresDecision, signatures.RequiresDecision);
        if (requiresDecision)
        {
            Assert.Equal("Đồng ý (Agree)", signatures.AgreeLabel);
            Assert.Equal("Không đồng ý (Disagree)", signatures.RefuseLabel);
            Assert.StartsWith("Ông/bà đã xem và đồng ý", signatures.DecisionLabel, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Bm02_InsuranceScheme_RequiresCardNumber_AndChecksFormat()
    {
        var definition = Definition("QT-KNGT-BM02");

        var missing = FormDataEvaluator.Evaluate(definition, new JsonObject { ["careScheme"] = "insurance" });
        var invalid = FormDataEvaluator.Evaluate(definition, new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "12-34" });
        var valid = FormDataEvaluator.Evaluate(definition, new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD479012345678901" });
        var legacy = FormDataEvaluator.Evaluate(definition, new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "GD4790000000125" });
        var bhxhOnly = FormDataEvaluator.Evaluate(definition, new JsonObject { ["careScheme"] = "insurance", ["insuranceNumber"] = "7900000001" });

        Assert.Contains("insuranceNumber", missing.Errors.Keys);
        Assert.Contains("Số thẻ BHYT gồm 17 ký tự", invalid.Errors["insuranceNumber"][0], StringComparison.Ordinal);
        Assert.True(valid.IsValid);
        Assert.True(legacy.IsValid);
        Assert.Contains("insuranceNumber", bhxhOnly.Errors.Keys);
    }

    [Fact]
    public void Bm02_NonInsuranceScheme_DropsCardNumber()
    {
        var result = FormDataEvaluator.Evaluate(
            Definition("QT-KNGT-BM02"), new JsonObject { ["careScheme"] = "nonInsurance", ["insuranceNumber"] = "GD4790000000125" });

        Assert.True(result.IsValid);
        Assert.Null(result.Data["insuranceNumber"]);
    }

    [Fact]
    public void OutpatientInformation_OtherReligionAndSeparateEmergencyContact_AreRequired()
    {
        var data = OutpatientInformation();
        data["religion"] = "other";
        data["familyIsEmergencyContact"] = "no";

        var result = FormDataEvaluator.Evaluate(Definition("TTNB-NGOAITRU"), data);

        Assert.Equal(
            ["emergencyName", "emergencyPhone", "emergencyRelationship", "religionOther"],
            result.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OutpatientInformation_FamilyMemberIsEmergencyContact_IsComplete()
    {
        var data = OutpatientInformation();
        data["emergencyName"] = "bị bỏ vì đang ẩn";

        var result = FormDataEvaluator.Evaluate(Definition("TTNB-NGOAITRU"), data);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Keys));
        Assert.Null(result.Data["emergencyName"]);
    }

    [Fact]
    public void Bm10_CompleteAdultAssessment_IsValid_AndScoresNutritionRisk()
    {
        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), InitialAssessment());

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => $"{e.Key}: {e.Value[0]}")));
        Assert.Equal(1, result.Scores["adultNutritionTotal"].Total);
        Assert.Equal("moderate", result.Scores["adultNutritionTotal"].LevelCode);
        Assert.Null(result.Scores["pnstTotal"].Total);
    }

    [Theory]
    [InlineData("atLeast20", "below5", "no", 0, "none")]
    [InlineData("from18_5to20", "below5", "no", 1, "moderate")]
    [InlineData("below18_5", "from5to10", "no", 3, "high")]
    [InlineData("from18_5to20", "from5to10", "yes", 4, "high")]
    [InlineData("below18_5", "above10", "no", 4, "high")]
    [InlineData("below18_5", "from5to10", "yes", 5, "veryHigh")]
    [InlineData("below18_5", "above10", "yes", 6, "veryHigh")]
    public void Bm10_AdultNutritionScore_FollowsTheFormThresholds(
        string bmi, string weightLoss, string acuteIllness, int expectedTotal, string expectedLevel)
    {
        var data = InitialAssessment();
        data["adultBmiScore"] = bmi;
        data["adultWeightLossScore"] = weightLoss;
        data["adultAcuteIllnessScore"] = acuteIllness;

        var score = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data).Scores["adultNutritionTotal"];

        Assert.Equal(expectedTotal, score.Total);
        Assert.Equal(expectedLevel, score.LevelCode);
    }

    [Theory]
    [InlineData(1, "low")]
    [InlineData(2, "risk")]
    [InlineData(4, "risk")]
    public void Bm10_PediatricPnst_CountsRiskFactors(int riskFactors, string expectedLevel)
    {
        var data = InitialAssessment();
        data["nutritionGroup"] = "pediatric";
        data.Remove("adultNutritionRisk");
        data["pediatricNutritionRisk"] = expectedLevel;
        string[] questions = ["pnstWeightLoss", "pnstPoorWeightGain", "pnstPoorIntake", "pnstNoticeableLoss"];
        for (var i = 0; i < questions.Length; i++)
        {
            data[questions[i]] = i < riskFactors ? "yes" : "no";
        }

        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Keys));
        Assert.Equal(riskFactors, result.Scores["pnstTotal"].Total);
        Assert.Equal(expectedLevel, result.Scores["pnstTotal"].LevelCode);
        Assert.Null(result.Data["adultBmiScore"]);
        Assert.Null(result.Scores["adultNutritionTotal"].Total);
    }

    [Fact]
    public void Bm10_EmergencySigns_SkipTheAssessment()
    {
        var data = InitialAssessment();
        data["emergencySigns"] = "yes";
        data["emergencySignsDetail"] = "Khó thở, tím tái";

        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data);

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Keys));
        Assert.Null(result.Data["visitReason"]);
        Assert.Null(result.Data["currentPain"]);
        Assert.Null(result.Data["adultBmiScore"]);
        Assert.Null(result.Data["adultNutritionRisk"]);
        Assert.Null(result.Scores["adultNutritionTotal"].Total);
    }

    [Fact]
    public void Bm10_InfectionSymptoms_RequireEpidemiologyAndStaffAction()
    {
        var data = InitialAssessment();
        data["infectionSymptoms"] = "yes";

        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data);

        Assert.Equal(["closeContact", "epidemiologicalExposure", "infectionAction"], result.Errors.Keys.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("bloodPressure", "120-80")]
    [InlineData("painScore", "11")]
    [InlineData("pulse", "tám mươi")]
    public void Bm10_Measurements_AreChecked(string key, string value)
    {
        var data = InitialAssessment();
        data["currentPain"] = "yes";
        data["painOnset"] = "Hai ngày trước";
        data["painLocation"] = "Lưng";
        data["painScore"] = "3";
        data["painScale"] = "nrs";
        data[key] = value;

        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data);

        Assert.Equal([key], result.Errors.Keys);
    }

    [Fact]
    public void Bm10_DietCounselingOther_RequiresDescription()
    {
        var data = InitialAssessment();
        data["dietCounseling"] = "required";
        data["dietRegimens"] = new JsonArray("ironDeficiency", "other");

        var result = FormDataEvaluator.Evaluate(Definition("QT-KNGT-BM10"), data);

        Assert.Equal(["dietRegimenOther"], result.Errors.Keys);
    }

    private static JsonObject OutpatientInformation() => new()
    {
        ["ethnicity"] = "Kinh",
        ["religion"] = "buddhism",
        ["educationLevel"] = "12/12",
        ["occupation"] = "Nhân viên văn phòng",
        ["permanentAddress"] = "1 Đường Thử Nghiệm, Phường Mẫu, TP. Hồ Chí Minh",
        ["currentAddress"] = "1 Đường Thử Nghiệm, Phường Mẫu, TP. Hồ Chí Minh",
        ["familyName"] = "Người Nhà Thử Nghiệm",
        ["familyRelationship"] = "Con",
        ["familyPhone"] = "0900000001",
        ["familyIsEmergencyContact"] = "yes",
    };

    /// <summary>Phiếu nhận định người lớn đầy đủ, không có dấu hiệu cấp cứu (dữ liệu giả lập).</summary>
    private static JsonObject InitialAssessment() => new()
    {
        ["consultingRoom"] = "Phòng 05",
        ["selfFallHistory"] = "no",
        ["selfMobilityAid"] = "no",
        ["infectionSymptoms"] = "no",
        ["staffMobilityAid"] = "no",
        ["staffUnsteady"] = "no",
        ["pulse"] = "80",
        ["temperature"] = "37,2",
        ["bloodPressure"] = "120/80",
        ["respiratoryRate"] = "18",
        ["height"] = "160",
        ["weight"] = "50",
        ["bmi"] = "19,5",
        ["emergencySigns"] = "no",
        ["visitReason"] = "followUp",
        ["allergies"] = "none",
        ["mentalStatusAppropriate"] = "no",
        ["barrierLanguage"] = "no",
        ["barrierCognitive"] = "no",
        ["barrierSensory"] = "no",
        ["barrierReligious"] = "no",
        ["barrierCultural"] = "no",
        ["barrierMobility"] = "no",
        ["livingStatus"] = "withFamily",
        ["currentPain"] = "no",
        ["nutritionGroup"] = "adult",
        ["adultBmiScore"] = "from18_5to20",
        ["adultWeightLossScore"] = "below5",
        ["adultAcuteIllnessScore"] = "no",
        ["highFallRisk"] = "no",
        ["nurseEduFall"] = "notNeeded",
        ["nurseEduWound"] = "notNeeded",
        ["adultNutritionRisk"] = "moderate",
        ["dietCounseling"] = "notRequired",
        ["physicianEduFall"] = "notRequired",
        ["physicianEduWound"] = "notRequired",
        ["physicianEduPain"] = "notRequired",
        ["physicianEduNewPatient"] = "notRequired",
        ["physicianEduOther"] = "notRequired",
    };
}
