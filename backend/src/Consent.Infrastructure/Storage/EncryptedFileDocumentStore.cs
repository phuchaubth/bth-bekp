using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Consent.Application.Abstractions;
using Consent.Infrastructure.Security;
using Microsoft.Extensions.Options;

namespace Consent.Infrastructure.Storage;

/// <summary>
/// Lưu tài liệu thành file trên ổ đĩa, mã hóa AES-256-GCM. Khóa mã hóa file được dẫn xuất (HKDF) từ khóa mã hóa cột,
/// tách mục đích sử dụng. Khóa lưu trữ được đưa vào dữ liệu xác thực (AAD), nên tráo file của phiếu này sang phiếu khác
/// hoặc sửa bất kỳ byte nào đều bị phát hiện khi đọc.
/// Định dạng file: <c>CDOC</c> | phiên bản (1 byte) | nonce (12) | tag (16) | bản mã.
/// </summary>
internal sealed partial class EncryptedFileDocumentStore : IDocumentStore
{
    private static readonly byte[] Magic = "CDOC"u8.ToArray();
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 5;

    private readonly byte[] _key;
    private readonly string _root;

    public EncryptedFileDocumentStore(IOptions<DocumentStorageOptions> storage, IOptions<DataProtectionOptions> dataProtection)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(dataProtection);
        _root = Path.GetFullPath(storage.Value.DocumentsPath);
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Convert.FromBase64String(dataProtection.Value.FieldEncryptionKey),
            outputLength: 32,
            salt: [],
            info: "consent-documents-v1"u8.ToArray());
    }

    public async Task<string> SaveAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);
        var now = DateTimeOffset.UtcNow;
        var safeName = UnsafeChars().Replace(Path.GetFileNameWithoutExtension(fileName), "-");
        var key = string.Create(
            CultureInfo.InvariantCulture, $"{now:yyyy}/{now:MM}/{safeName}-{Guid.NewGuid():N}.pdf.enc");

        var buffer = new byte[HeaderSize + NonceSize + TagSize + content.Length];
        Magic.CopyTo(buffer, 0);
        buffer[4] = Version;
        var nonce = buffer.AsSpan(HeaderSize, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using (var aes = new AesGcm(_key, TagSize))
        {
            aes.Encrypt(
                nonce,
                content,
                buffer.AsSpan(HeaderSize + NonceSize + TagSize),
                buffer.AsSpan(HeaderSize + NonceSize, TagSize),
                Encoding.UTF8.GetBytes(key));
        }

        var path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Ghi ra file tạm rồi đổi tên: không bao giờ để lại file ghi dở dưới tên chính thức.
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, buffer, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: false);
        return key;
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (!ValidKey().IsMatch(key))
        {
            throw new DocumentUnavailableException("Khóa lưu trữ không hợp lệ.");
        }

        byte[] buffer;
        try
        {
            buffer = await File.ReadAllBytesAsync(PathOf(key), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new DocumentUnavailableException("Không tìm thấy file đã lưu.", ex);
        }

        if (buffer.Length < HeaderSize + NonceSize + TagSize || !buffer.AsSpan(0, 4).SequenceEqual(Magic) || buffer[4] != Version)
        {
            throw new DocumentUnavailableException("File đã lưu không đúng định dạng.");
        }

        var plain = new byte[buffer.Length - HeaderSize - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                buffer.AsSpan(HeaderSize, NonceSize),
                buffer.AsSpan(HeaderSize + NonceSize + TagSize),
                buffer.AsSpan(HeaderSize + NonceSize, TagSize),
                plain,
                Encoding.UTF8.GetBytes(key));
        }
        catch (AuthenticationTagMismatchException ex)
        {
            throw new DocumentUnavailableException("File đã lưu bị sửa hoặc không thuộc phiếu này.", ex);
        }

        return plain;
    }

    private string PathOf(string key) => Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    [GeneratedRegex("[^A-Za-z0-9-]")]
    private static partial Regex UnsafeChars();

    [GeneratedRegex(@"^\d{4}/\d{2}/[A-Za-z0-9-]+\.pdf\.enc$")]
    private static partial Regex ValidKey();
}
