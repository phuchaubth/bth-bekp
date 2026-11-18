using Consent.Domain.Patients;

namespace Consent.Domain.Tests;

public class HealthInsuranceNumberTests
{
    [Theory]
    [InlineData("GD479012345678901", true, false)]   // mã mới 17 ký tự
    [InlineData("gd479012345678901", true, false)]
    [InlineData("GD4790000000125", true, true)]      // mã cũ 15 ký tự
    [InlineData("7900000001", false, false)]          // BHXH 10 số đứng riêng: không nhận
    [InlineData("079192000002", false, false)]        // CCCD 12 số đứng riêng: không nhận
    [InlineData("GD47900000001250", false, false)]    // 16 ký tự
    [InlineData("GD4790123456789012", false, false)]  // 18 ký tự
    [InlineData("1D479012345678901", false, false)]
    [InlineData("", false, false)]
    [InlineData(null, false, false)]
    public void Validates_NewAndLegacyFormats(string? value, bool valid, bool legacy)
    {
        Assert.Equal(valid, HealthInsuranceNumber.IsValid(value));
        Assert.Equal(legacy, HealthInsuranceNumber.IsLegacy(value));
    }
}
