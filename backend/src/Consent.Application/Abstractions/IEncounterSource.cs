namespace Consent.Application.Abstractions;

/// <summary>Một lượt tiếp nhận người bệnh trên HIS (quyết định 25). Thời gian là giờ của bệnh viện (UTC+7).</summary>
public sealed record ExternalEncounter(string EncounterNo, DateTime ReceivedAt, string? Place, string? ReceivedBy);

public enum EncounterSourceStatus
{
    Found,

    /// <summary>Không lấy được (lỗi mạng, quá thời gian, CSDL bệnh viện lỗi, chưa cấu hình truy vấn ở API thông tin người bệnh).</summary>
    Unavailable,

    /// <summary>Chưa cấu hình API thông tin người bệnh (<c>PatientSource:Provider</c> khác <c>Api</c>).</summary>
    Disabled,
}

public sealed record EncounterSourceResult(EncounterSourceStatus Status, IReadOnlyList<ExternalEncounter> Encounters)
{
    public static EncounterSourceResult Unavailable { get; } = new(EncounterSourceStatus.Unavailable, []);

    public static EncounterSourceResult Disabled { get; } = new(EncounterSourceStatus.Disabled, []);
}

/// <summary>
/// Lượt tiếp nhận của người bệnh trên HIS, lấy qua API thông tin người bệnh (quyết định 25): tra theo mã người bệnh hoặc số
/// CCCD (một trong hai) từ ngày đến ngày. Bắt buộc gắn lượt khi hoàn tất hay không do quản trị bật/tắt ở Cấu hình hệ thống.
/// </summary>
public interface IEncounterSource
{
    /// <summary>Đã cấu hình API thông tin người bệnh, tức là tra được lượt tiếp nhận (điều kiện để quản trị bật).</summary>
    bool IsEnabled { get; }

    Task<EncounterSourceResult> FindAsync(string? patientCode, string? citizenId, DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
