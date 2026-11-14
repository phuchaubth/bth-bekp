using Consent.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

/// <summary>Mã hóa khi ghi, giải mã khi đọc (giá trị null giữ nguyên null).</summary>
internal sealed class EncryptedStringConverter(IFieldEncryptor encryptor)
    : ValueConverter<string, string>(plain => encryptor.Encrypt(plain), cipher => encryptor.Decrypt(cipher));

/// <summary>Dữ liệu nhị phân (ví dụ ảnh chữ ký): mã hóa phần Base64 như cột chuỗi.</summary>
internal sealed class EncryptedBytesConverter(IFieldEncryptor encryptor)
    : ValueConverter<byte[], string>(
        plain => encryptor.Encrypt(Convert.ToBase64String(plain)),
        cipher => Convert.FromBase64String(encryptor.Decrypt(cipher)));
