using Microsoft.Extensions.Configuration;

namespace PatientInfo.Api.Tests;

public sealed class PatientRepositoryMappingTests
{
    private static readonly PatientInfoOptions Settings = new();

    [Theory]
    [InlineData("Nam", "Male")]
    [InlineData("nữ", "Female")]
    [InlineData("1", "Male")]
    [InlineData("2", "Female")]
    [InlineData("Khác", null)]
    public void Gender_IsMappedFromConfiguredValues(string raw, string? expected)
    {
        var patient = PatientRepository.Map(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Gender"] = raw }, Settings);

        Assert.Equal(expected, patient.Gender);
    }

    [Fact]
    public void ConfiguredGenderValues_ReplaceTheDefaults()
    {
        // CSDL lưu 0 = Nam, 1 = Nữ (ngược với mặc định): đặt cả hai danh sách qua cấu hình như biến môi trường.
        var settings = Bind(new() { ["PatientInfo:MaleValues:0"] = "0", ["PatientInfo:FemaleValues:0"] = "1" });

        Assert.Equal(["0"], settings.EffectiveMaleValues);
        Assert.Equal(["1"], settings.EffectiveFemaleValues);
        Assert.True(settings.HasUnambiguousGenderValues);
        Assert.Equal("Male", Gender("0", settings));
        Assert.Equal("Female", Gender("1", settings));
        Assert.Null(Gender("Nam", settings));
    }

    [Fact]
    public void SettingOnlyOneGenderList_IsRejectedAsAmbiguous()
    {
        Assert.True(new PatientInfoOptions().HasUnambiguousGenderValues);
        Assert.False(Bind(new() { ["PatientInfo:MaleValues:0"] = "0" }).HasUnambiguousGenderValues);
    }

    private static PatientInfoOptions Bind(Dictionary<string, string?> values)
    {
        var options = new PatientInfoOptions();
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection(PatientInfoOptions.SectionName).Bind(options);
        return options;
    }

    private static string? Gender(string raw, PatientInfoOptions settings) =>
        PatientRepository.Map(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Gender"] = raw }, settings).Gender;

    [Theory]
    [InlineData("03/02/1970")]
    [InlineData("1970-02-03")]
    [InlineData("19700203")]
    public void DateOfBirth_AcceptsCommonTextFormats(string raw)
    {
        var patient = PatientRepository.Map(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["DateOfBirth"] = raw }, Settings);

        Assert.Equal(new DateOnly(1970, 2, 3), patient.DateOfBirth);
    }

    [Fact]
    public void BlankTextBecomesNull_AndInsuranceNumberIsUppercased()
    {
        var patient = PatientRepository.Map(
            new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["fullname"] = "  Lê Văn Thử (giả lập) ",
                ["PhoneNumber"] = "   ",
                ["HealthInsuranceNumber"] = "gd479079070000123",
                ["DateOfBirth"] = new DateTime(1970, 2, 3),
            },
            Settings);

        Assert.Equal("Lê Văn Thử (giả lập)", patient.FullName);
        Assert.Null(patient.PhoneNumber);
        Assert.Equal("GD479079070000123", patient.HealthInsuranceNumber);
        Assert.Equal(new DateOnly(1970, 2, 3), patient.DateOfBirth);
    }

    [Theory]
    [InlineData("SELECT * FROM dbo.BenhNhan WHERE MaBN = @PatientCode", true)]
    [InlineData("WITH x AS (SELECT 1 AS a) SELECT * FROM x WHERE @CitizenId IS NULL;", true)]
    [InlineData("SELECT * FROM dbo.BenhNhan", false)]
    [InlineData("DELETE FROM dbo.BenhNhan WHERE MaBN = @PatientCode", false)]
    [InlineData("SELECT * FROM dbo.BenhNhan WHERE MaBN = @PatientCode; DROP TABLE dbo.BenhNhan", false)]
    [InlineData("", false)]
    public void Query_MustBeASingleParameterizedSelect(string query, bool valid)
    {
        Assert.Equal(valid, new PatientInfoOptions { Query = query }.HasValidQuery);
    }

    [Fact]
    public void ApiKeys_MustBeLongEnough()
    {
        Assert.False(new PatientInfoOptions().HasValidApiKeys);
        Assert.False(new PatientInfoOptions { ApiKeys = ["ngan"] }.HasValidApiKeys);
        Assert.True(new PatientInfoOptions { ApiKeys = [new string('k', 32)] }.HasValidApiKeys);
    }
}
