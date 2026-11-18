using Consent.Application.ConsentTemplates;

namespace Consent.Application.Tests;

public sealed class ComputedFormulasTests
{
    [Theory]
    [InlineData("50", "160", "19,5")]
    [InlineData("58,5", "165", "21,5")]
    [InlineData("58.5", "165.0", "21,5")]
    [InlineData("70", "175", "22,9")]
    [InlineData("45", "170", "15,6")]
    [InlineData("100", "160", "39,1")]
    public void Bmi_IsWeightOverHeightInMetresSquared_RoundedToOneDecimal(string weight, string height, string expected) =>
        Assert.Equal(expected, ComputedFormulas.Compute(ComputedFormulas.Bmi, [weight, height]));

    [Theory]
    [InlineData(null, "160")]
    [InlineData("50", null)]
    [InlineData("", "160")]
    [InlineData("abc", "160")]
    [InlineData("50", "0")]
    [InlineData("-50", "160")]
    public void Bmi_WithMissingOrInvalidInput_IsEmpty(string? weight, string? height) =>
        Assert.Null(ComputedFormulas.Compute(ComputedFormulas.Bmi, [weight, height]));

    [Fact]
    public void UnknownFormula_IsEmpty() => Assert.Null(ComputedFormulas.Compute("bsa", ["50", "160"]));
}
