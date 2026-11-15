using Consent.Domain.Common;

namespace Consent.Domain.ConsentForms;

/// <summary>
/// Lượt tiếp nhận người bệnh trên HIS mà phiếu thuộc về (quyết định 25): gắn khi phía bệnh viện ký hoàn tất, in ở chân mỗi
/// trang PDF để đối soát. Nơi tiếp nhận, người tiếp nhận dài quá thì cắt bớt.
/// </summary>
public sealed record ConsentEncounter
{
    public const int NumberMaxLength = 50;
    public const int TextMaxLength = 200;

    public ConsentEncounter(string number, DateTimeOffset receivedAt, string? place, string? receivedBy)
    {
        var cleanNumber = number?.Trim() ?? string.Empty;
        if (cleanNumber.Length is 0 or > NumberMaxLength)
        {
            throw new DomainException($"Số tiếp nhận phải có từ 1 đến {NumberMaxLength} ký tự.");
        }

        Number = cleanNumber;
        ReceivedAt = receivedAt;
        Place = Clip(place);
        ReceivedBy = Clip(receivedBy);
    }

    public string Number { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string? Place { get; }

    public string? ReceivedBy { get; }

    private static string? Clip(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length > TextMaxLength ? text[..TextMaxLength] : text;
    }
}
