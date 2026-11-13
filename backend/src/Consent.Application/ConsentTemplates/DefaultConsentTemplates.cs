using Consent.Domain.Identity;

namespace Consent.Application.ConsentTemplates;

/// <summary>Một mẫu phiếu khởi tạo sẵn: mã, tên, vai trò xác nhận phía bệnh viện và định nghĩa (JSON).</summary>
public sealed record DefaultConsentTemplate(string Code, string Name, string HospitalSignerRoleCode, string ContentJson);

/// <summary>
/// Bốn mẫu phiếu thật của bệnh viện (song ngữ Việt – Anh, chuyển từ bản Word; xem <c>docs/mau-phieu.md</c>). Được tạo
/// khi khởi tạo dữ liệu nếu chưa có mã; bệnh viện chỉnh nội dung bằng cách phát hành phiên bản mới, không cần sửa code.
/// </summary>
public static class DefaultConsentTemplates
{
    public static IReadOnlyList<DefaultConsentTemplate> All { get; } =
    [
        new(
            "QT-KNGT-BM02",
            "Giấy đồng thuận về chế độ thanh toán và quyền lợi khám chữa bệnh BHYT",
            SystemRoles.Receptionist,
            Load("QT-KNGT-BM02")),
        new("TTNB-NGOAITRU", "Phiếu thông tin người bệnh ngoại trú", SystemRoles.Receptionist, Load("TTNB-NGOAITRU")),
        new(
            "QT-KNGT-BM10",
            "Phiếu nhận định ban đầu và nhu cầu giáo dục sức khỏe người bệnh ngoại trú",
            SystemRoles.Doctor,
            Load("QT-KNGT-BM10")),
        new(
            "TUVAN-CONGTT",
            "Phiếu tư vấn tham gia và sử dụng cổng thông tin chính thức của bệnh viện",
            SystemRoles.Receptionist,
            Load("TUVAN-CONGTT")),
    ];

    private static string Load(string code)
    {
        var name = $"ConsentTemplates.Defaults.{code}.json";
        using var stream = typeof(DefaultConsentTemplates).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Thiếu tài nguyên mẫu phiếu {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
