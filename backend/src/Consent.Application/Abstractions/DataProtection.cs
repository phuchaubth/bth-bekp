namespace Consent.Application.Abstractions;

/// <summary>Mã hóa/giải mã dữ liệu nhạy cảm ở mức cột (ví dụ số CCCD, số điện thoại, mã BHYT).</summary>
public interface IFieldEncryptor
{
    string Encrypt(string plaintext);

    string Decrypt(string ciphertext);
}

/// <summary>
/// Chỉ mục mù (blind index): băm có khóa bí mật (HMAC) của giá trị nhạy cảm để tra cứu chính xác/phát hiện trùng
/// mà không cần lưu giá trị gốc. Không thể dò ngược nếu không có khóa.
/// </summary>
public interface IBlindIndexer
{
    string Compute(string value);
}
