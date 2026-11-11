using Consent.Domain.Identity;

namespace Consent.Application.ConsentTemplates;

/// <summary>Một mẫu phiếu khởi tạo sẵn: mã, tên, vai trò xác nhận phía bệnh viện và định nghĩa (JSON).</summary>
public sealed record DefaultConsentTemplate(string Code, string Name, string HospitalSignerRoleCode, string ContentJson);

/// <summary>
/// Ba mẫu phiếu đề xuất cho giai đoạn 1 (xem <c>docs/mau-phieu-de-xuat.md</c>). Được tạo khi khởi tạo dữ liệu;
/// bệnh viện duyệt/chỉnh nội dung bằng cách phát hành phiên bản mới, không cần sửa code.
/// </summary>
public static class DefaultConsentTemplates
{
    public static IReadOnlyList<DefaultConsentTemplate> All { get; } =
    [
        new("CK-01", "Giấy cam kết sử dụng dịch vụ khám bệnh, chữa bệnh", SystemRoles.Receptionist, Load("CK-01")),
        new("TT-01", "Phiếu cung cấp thông tin và cam kết chung", SystemRoles.Nurse, Load("TT-01")),
        new("TN-01", "Phiếu đánh giá nguy cơ té ngã và cam kết phối hợp phòng ngừa", SystemRoles.Nurse, Load("TN-01")),
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
