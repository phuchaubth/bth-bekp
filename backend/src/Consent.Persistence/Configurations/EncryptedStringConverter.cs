using Consent.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Consent.Persistence.Configurations;

/// <summary>Mã hóa khi ghi, giải mã khi đọc (giá trị null giữ nguyên null).</summary>
internal sealed class EncryptedStringConverter(IFieldEncryptor encryptor)
    : ValueConverter<string, string>(plain => encryptor.Encrypt(plain), cipher => encryptor.Decrypt(cipher));
