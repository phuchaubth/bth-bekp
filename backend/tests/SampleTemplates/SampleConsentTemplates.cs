using Consent.Application.ConsentTemplates;
using Consent.Domain.Identity;

namespace Consent.Tests.Samples;

/// <summary>
/// Ba mẫu phiếu minh họa (đề xuất ban đầu, xem <c>docs/mau-phieu-de-xuat.md</c>), chỉ dùng cho kiểm thử: đủ các kiểu khối,
/// điều kiện hiển thị, câu hỏi tính điểm. Ứng dụng không tạo các mẫu này; mẫu thật của bệnh viện nằm trong
/// <see cref="DefaultConsentTemplates"/>.
/// </summary>
internal static class SampleConsentTemplates
{
    public static IReadOnlyList<DefaultConsentTemplate> All { get; } =
    [
        new("CK-01", "Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh", SystemRoles.Receptionist, Load("CK-01")),
        new("TT-01", "Phiếu cung cấp thông tin và cam kết chung", SystemRoles.Nurse, Load("TT-01")),
        new("TN-01", "Phiếu đánh giá nguy cơ té ngã và cam kết phối hợp phòng ngừa", SystemRoles.Nurse, Load("TN-01")),
    ];

    public static DefaultConsentTemplate Get(string code) => All.Single(t => t.Code == code);

    private static string Load(string code)
    {
        var name = $"SampleTemplates.{code}.json";
        using var stream = typeof(SampleConsentTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên mẫu minh họa {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
