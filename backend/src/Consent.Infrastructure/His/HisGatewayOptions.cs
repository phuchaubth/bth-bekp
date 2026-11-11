namespace Consent.Infrastructure.His;

/// <summary>Cấu hình HIS phía hạ tầng (mục <c>His</c>).</summary>
public sealed class HisGatewayOptions
{
    public const string SectionName = "His";

    /// <summary>Chưa kết nối HIS (mặc định): không tra cứu, không đối chiếu, giao diện ẩn thông tin HIS.</summary>
    public const string NoneProvider = "None";

    /// <summary>HIS giả lập, chỉ dùng khi phát triển và kiểm thử; bị từ chối ở môi trường Production.</summary>
    public const string FakeProvider = "Fake";

    /// <summary><c>None</c> (mặc định) hoặc <c>Fake</c>. Kết nối FPT eHospital sẽ bổ sung khi bệnh viện cần.</summary>
    public string Provider { get; set; } = NoneProvider;

    /// <summary>Độ trễ của HIS giả lập (mili giây).</summary>
    public int FakeLatencyMilliseconds { get; set; } = 150;

    public bool IsFake => string.Equals(Provider, FakeProvider, StringComparison.OrdinalIgnoreCase);

    public bool IsKnownProvider => IsFake || string.Equals(Provider, NoneProvider, StringComparison.OrdinalIgnoreCase);
}
