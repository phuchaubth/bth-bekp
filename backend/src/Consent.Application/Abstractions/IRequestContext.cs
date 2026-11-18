namespace Consent.Application.Abstractions;

/// <summary>Thông tin về yêu cầu hiện tại, dùng cho nhật ký audit.</summary>
public interface IRequestContext
{
    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }
}
