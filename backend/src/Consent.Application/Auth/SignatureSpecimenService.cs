using Consent.Application.Abstractions;
using Consent.Application.ConsentForms;
using Consent.Shared;

namespace Consent.Application.Auth;

/// <summary>
/// Chữ ký mẫu của người đang đăng nhập: tự ký ở trang Tài khoản; hiện cạnh khung ký khi xác nhận phiếu để đối chiếu.
/// Mỗi người chỉ xem và sửa chữ ký mẫu của chính mình.
/// </summary>
public sealed class SignatureSpecimenService(
    ICurrentUser currentUser,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    /// <summary>Ảnh PNG chữ ký mẫu; chưa có thì null.</summary>
    public async Task<byte[]?> GetMineAsync(CancellationToken cancellationToken)
    {
        var user = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return user?.SignatureSpecimenPng;
    }

    /// <summary>Lưu (hoặc thay) chữ ký mẫu: ảnh được kiểm tra và chuẩn hóa như chữ ký trên phiếu.</summary>
    public async Task<Result<DateTimeOffset>> SaveMineAsync(SignatureSpecimenRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var png = ConsentSigningService.NormalizeSignature(request.Image);
        if (png.IsFailure)
        {
            return png.Error!;
        }

        var user = await LoadAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Không tìm thấy tài khoản đang đăng nhập.");
        var now = clock.UtcNow;
        user.SetSignatureSpecimen(png.Value, now);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return now;
    }

    public async Task DeleteMineAsync(CancellationToken cancellationToken)
    {
        var user = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (user?.SignatureSpecimenPng is null)
        {
            return;
        }

        user.ClearSignatureSpecimen();
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<Domain.Identity.User?> LoadAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } id ? await users.GetByIdAsync(id, cancellationToken).ConfigureAwait(false) : null;
}
