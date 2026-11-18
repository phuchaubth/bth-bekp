using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 có khóa bí mật: cùng giá trị cho cùng kết quả (để tra cứu chính xác), nhưng không thể dò ngược
/// số CCCD (chỉ có 10^12 khả năng) nếu không có khóa — khác với SHA-256 thông thường.
/// </summary>
internal sealed class HmacBlindIndexer : IBlindIndexer
{
    private readonly byte[] _key;

    public HmacBlindIndexer(IOptions<DataProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = Convert.FromBase64String(options.Value.BlindIndexKey);
    }

    public string Compute(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }
}
