namespace Consent.Application.Abstractions;

public interface IUnitOfWork
{
    /// <summary>
    /// Lưu mọi thay đổi trong một giao dịch. Ném <see cref="Common.ConflictException"/> khi trùng dữ liệu
    /// duy nhất hoặc xung đột cập nhật đồng thời.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
