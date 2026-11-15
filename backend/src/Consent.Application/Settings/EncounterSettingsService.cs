using Consent.Application.Abstractions;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.Settings;

/// <summary>
/// Gắn lượt tiếp nhận ở màn hình Cấu hình hệ thống. <paramref name="Enabled"/>: đang bắt buộc chọn lượt khi ký hoàn tất.
/// <paramref name="Available"/>: đã cấu hình API thông tin người bệnh nên bật được.
/// </summary>
public sealed record EncounterSettingsDto(bool Enabled, bool Available, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdateEncounterSettingsRequest(bool Enabled);

/// <summary>
/// Bật/tắt gắn phiếu với lượt tiếp nhận HIS (quyết định 25) tại Cấu hình hệ thống, có hiệu lực ngay, không cần khởi động lại.
/// Bật: người ký hoàn tất (tiếp nhận, bác sĩ…) bắt buộc chọn lượt tiếp nhận của người bệnh. HIS lỗi thì quản trị tắt để nhân
/// viên hoàn tất tiếp (phiếu hoàn tất lúc tắt không gắn lượt), HIS ổn định thì bật lại. Chỉ bật được khi đã cấu hình API thông
/// tin người bệnh. Thay đổi được ghi nhật ký như mọi cấu hình khác.
/// </summary>
public sealed class EncounterSettingsService(
    ISystemSettingRepository settings,
    IEncounterSource source,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    private const string On = "true";
    private const string Off = "false";

    private static readonly Error Unavailable = Error.BusinessRule(
        "settings.encounters_unavailable",
        "Chưa cấu hình API thông tin người bệnh (PatientSource__Provider = Api) nên chưa bật được gắn lượt tiếp nhận.");

    /// <summary>Đang bắt buộc chọn lượt tiếp nhận khi ký hoàn tất (đã cấu hình API và quản trị đang bật).</summary>
    public async Task<bool> IsActiveAsync(CancellationToken cancellationToken) =>
        source.IsEnabled && IsOn(await FindAsync(cancellationToken).ConfigureAwait(false));

    public async Task<EncounterSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var setting = await FindAsync(cancellationToken).ConfigureAwait(false);
        return new EncounterSettingsDto(source.IsEnabled && IsOn(setting), source.IsEnabled, setting?.UpdatedAtUtc);
    }

    public async Task<Result<EncounterSettingsDto>> UpdateAsync(UpdateEncounterSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Enabled && !source.IsEnabled)
        {
            return Unavailable;
        }

        var existing = await FindAsync(cancellationToken).ConfigureAwait(false);
        var value = request.Enabled ? On : Off;
        if (existing is null)
        {
            if (request.Enabled)
            {
                settings.Add(SystemSetting.Create(SettingKeys.EncountersEnabled, value, clock.UtcNow));
            }
        }
        else if (existing.Value != value)
        {
            // Giữ dòng cấu hình khi tắt (không xóa) để nhật ký có giá trị cũ/mới và màn hình hiện lần đổi gần nhất.
            existing.Update(value, clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsOn(SystemSetting? setting) => string.Equals(setting?.Value, On, StringComparison.OrdinalIgnoreCase);

    private async Task<SystemSetting?> FindAsync(CancellationToken cancellationToken) =>
        (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => s.Key == SettingKeys.EncountersEnabled);
}
