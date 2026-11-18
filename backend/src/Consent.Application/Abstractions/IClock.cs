namespace Consent.Application.Abstractions;

/// <summary>Đồng hồ hệ thống (UTC). Trừu tượng hóa để kiểm thử được logic phụ thuộc thời gian.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
