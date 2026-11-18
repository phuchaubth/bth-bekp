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
    // Bản giấy TTNB (16/11/2026): ngoặc tiếng Việt có dấu "/" giữ màu đen; "(theo CCCD)" là tiếng Việt; ngoặc lồng nhau.
    [InlineData("Họ và Tên (theo Chứng minh /Căn cước) (Full Name (as per ID Card / Citizen ID))", "(Full Name (as per ID Card / Citizen ID))")]
    [InlineData("Ngày sinh (theo CCCD) (Date of Birth (as per ID Card / Citizen ID))", "(Date of Birth (as per ID Card / Citizen ID))")]
    [InlineData(
        "Số căn cước/mã số định danh cá nhân/số hộ chiếu (Identity Card/Citizen ID/Personal Identification Number/Passport Number)",
        "(Identity Card/Citizen ID/Personal Identification Number/Passport Number)")]
    public void Parentheses_InEnglish_AreEnglish(string text, string expected) =>
        Assert.Equal(expected, English(text));

    [Theory]
    [InlineData("Ngày sinh (theo CCCD)")]
    [InlineData("Số thẻ (BHYT)")]
    [InlineData("Họ và Tên (theo Chứng minh /Căn cước)")]
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
    [InlineData("Họ tên người bệnh / Patient’s Full Name: ", false)]
    [InlineData("Mã BN (nếu có)/ Patient Identification Number (if applicable): ", false)]
    public void Split_KeepsAllText(string text, bool wholeLines) => Assert.Equal(text, Joined(text, wholeLines));

    /// <summary>Kiểu của bản giấy (BM02): tiếng Anh sau dấu "/" đến hết dòng; dấu hai chấm cuối giữ màu thường.</summary>
    [Theory]
    [InlineData("Ngày sinh/ Date of birth", " Date of birth")]
    [InlineData("Họ tên người bệnh / Patient’s Full Name: ", " Patient’s Full Name")]
    [InlineData("Nam/Male", "Male")]
    [InlineData("Bảo hiểm y tế/ Health Insurance", " Health Insurance")]
    [InlineData("Mã BN (nếu có)/ Patient Identification Number (if applicable)", " Patient Identification Number (if applicable)")]
    [InlineData(
        "Chi phí chênh lệch (là khoảng chênh lệch so với đơn giá)/ The patient shall pay (representing the difference).",
        " The patient shall pay (representing the difference).")]
    [InlineData("Các dịch vụ do người bệnh/người nhà yêu cầu/ Services requested by the family", " Services requested by the family")]
    public void TextAfterSlash_InEnglish_IsEnglish(string text, string expected) => Assert.Equal(expected, English(text));

    [Theory]
    [InlineData("Người bệnh/người đại diện chọn ý kiến")]
    [InlineData("Website bệnh viện: https://bth.org.vn")]
    [InlineData("Mã số: QT-KNGT/BM02")]
    [InlineData("Quan hệ: Vợ/Chồng")]
    [InlineData("Mạch, lần/phút")]
    public void SlashBetweenVietnameseWordsOrCodes_StaysBlack(string text) => Assert.Equal(string.Empty, English(text));
}
