using Consent.Infrastructure.Pdf;

namespace Consent.Infrastructure.Tests;

public class BilingualTextTests
{
    private static string English(string text, bool wholeLines = false) =>
        string.Join("|", BilingualText.Split(text, wholeLines).Where(s => s.IsEnglish).Select(s => s.Text));

    private static string Joined(string text, bool wholeLines = false) =>
        string.Concat(BilingualText.Split(text, wholeLines).Select(s => s.Text));

    [Theory]
    [InlineData("Có (Yes)", "(Yes)")]
    [InlineData("Không (No)", "(No)")]
    [InlineData("Dân tộc (Ethnicity)", "(Ethnicity)")]
    [InlineData("Không Bảo hiểm y tế (Non–Health Insurance)", "(Non–Health Insurance)")]
    [InlineData("Mã số (Code): QT-KNGT/BM10", "(Code)")]
    [InlineData(
        "Đăng ký khám (Register via the mobile application named “BV Truyền máu Huyết học” or “BTH”)",
        "(Register via the mobile application named “BV Truyền máu Huyết học” or “BTH”)")]
    [InlineData("Nếu trả lời “CÓ” (If you answered “YES” (any question), please ask).", "(If you answered “YES” (any question), please ask)")]
    public void Parentheses_InEnglish_AreEnglish(string text, string expected) =>
        Assert.Equal(expected, English(text));

    [Theory]
    [InlineData("Nguy cơ (0 điểm)")]
    [InlineData("Địa chỉ (theo hộ khẩu)")]
    [InlineData("Cân nặng (kg)")]
    [InlineData("Ông (bà)")]
    [InlineData("Chỉ số (BMI)")]
    [InlineData("Email: ctxh_bth@bth.org.vn")]
    [InlineData("Wong – Baker")]
    [InlineData("Ngoặc chưa đóng (Yes")]
    public void VietnameseOrNeutralText_StaysBlack(string text) => Assert.Equal(string.Empty, English(text));

    [Fact]
    public void MixedParentheses_ColorOnlyTheEnglishPart() =>
        Assert.Equal(
            "verified against identification documents",
            English("(Đã đối chiếu thông tin trên giấy tờ tùy thân – verified against identification documents)"));

    [Fact]
    public void WholeEnglishLines_OnlyForTitles()
    {
        Assert.Equal("Consent Form", English("Giấy đồng thuận\nConsent Form", wholeLines: true));
        Assert.Equal(string.Empty, English("Giấy đồng thuận\nConsent Form"));
        Assert.Equal(string.Empty, English("Phiếu tư vấn tham gia và sử dụng\ncổng thông tin", wholeLines: true));
        // Phụ đề BM10: chỉ tô phần trong ngoặc, giữ "Mã số" và mã mẫu màu đen.
        Assert.Equal(
            "(Outpatient – Department of Medical Examination)|(Code)",
            English("(Outpatient – Department of Medical Examination) – Mã số (Code): QT-KNGT/BM10", wholeLines: true));
    }

    [Theory]
    [InlineData("Giấy đồng thuận\nConsent Form", true)]
    [InlineData("Tôi đồng ý (I agree), (I accept) và ký (sign here).", false)]
    [InlineData("((Yes))", false)]
    public void Split_KeepsAllText(string text, bool wholeLines) => Assert.Equal(text, Joined(text, wholeLines));
}
