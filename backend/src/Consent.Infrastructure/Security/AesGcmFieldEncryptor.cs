using System.Security.Cryptography;
using System.Text;
using Consent.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Security;

/// <summary>
/// Mã hóa AES-256-GCM (xác thực toàn vẹn). Định dạng lưu: <c>v1:</c> + Base64(nonce 12 byte | tag 16 byte | bản mã).
/// Nonce ngẫu nhiên cho mỗi lần mã hóa nên cùng một giá trị cho ra bản mã khác nhau.
/// Tiền tố phiên bản cho phép xoay vòng khóa sau này.
/// </summary>
internal sealed class AesGcmFieldEncryptor : IFieldEncryptor
{
    private const string Version = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmFieldEncryptor(IOptions<DataProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = Convert.FromBase64String(options.Value.FieldEncryptionKey);
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var buffer = new byte[NonceSize + TagSize + plainBytes.Length];
        var nonce = buffer.AsSpan(0, NonceSize);
        var tag = buffer.AsSpan(NonceSize, TagSize);
        var cipher = buffer.AsSpan(NonceSize + TagSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);
        return Version + Convert.ToBase64String(buffer);
    }

    public string Decrypt(string ciphertext)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        if (!ciphertext.StartsWith(Version, StringComparison.Ordinal))
        {
            throw new CryptographicException("Dữ liệu mã hóa không đúng định dạng hoặc phiên bản khóa.");
        }

        var buffer = Convert.FromBase64String(ciphertext[Version.Length..]);
        if (buffer.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Dữ liệu mã hóa bị hỏng.");
        }

        var plain = new byte[buffer.Length - NonceSize - TagSize];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(buffer.AsSpan(0, NonceSize), buffer.AsSpan(NonceSize + TagSize), buffer.AsSpan(NonceSize, TagSize), plain);
        return Encoding.UTF8.GetString(plain);
    }
}
