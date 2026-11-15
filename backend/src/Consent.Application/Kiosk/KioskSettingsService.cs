using Consent.Application.Abstractions;
using Consent.Domain.ConsentTemplates;
using Consent.Domain.Settings;
using Consent.Shared;

namespace Consent.Application.Kiosk;

/// <summary>
/// Quản trị chọn các mẫu người bệnh tự làm trên kiosk (quyết định 24, màn hình Cấu hình hệ thống). Chỉ nhận mẫu đang dùng,
/// đã phát hành và không có trường người bệnh không tự nhập được (ví dụ BM10 nhiều mục chuyên môn: điều dưỡng làm).
/// Thay đổi được ghi nhật ký như mọi cấu hình khác.
/// </summary>
public sealed class KioskSettingsService(
    ISystemSettingRepository settings,
    IConsentTemplateRepository templates,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<KioskSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var setting = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(s => s.Key == SettingKeys.KioskTemplateCodes);
        var codes = KioskTemplates.ParseCodes(setting?.Value);
        var all = await templates.ListAsync(includeInactive: true, cancellationToken).ConfigureAwait(false);
        List<KioskTemplateSettingDto> items =
        [
            .. codes.Select(code => all.FirstOrDefault(t => t.Code == code))
                .OfType<ConsentTemplate>()
                .Select(t => new KioskTemplateSettingDto(t.Id, t.Code, t.Name, KioskTemplates.Problems(t))),
        ];
        return new KioskSettingsDto(items, setting?.UpdatedAtUtc);
    }

    public async Task<Result<KioskSettingsDto>> UpdateAsync(UpdateKioskSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ids = request.TemplateIds ?? [];
        if (ids.Count > KioskTemplates.MaxTemplates)
        {
            return Error.ValidationField("templateIds", $"Chọn tối đa {KioskTemplates.MaxTemplates} mẫu cho kiosk.");
        }

        if (ids.Distinct().Count() != ids.Count)
        {
            return Error.ValidationField("templateIds", "Mỗi mẫu chỉ chọn một lần.");
        }

        var chosen = new List<ConsentTemplate>(ids.Count);
        foreach (var id in ids)
        {
            var template = await templates.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            if (template is null)
            {
                return Error.ValidationField("templateIds", "Có mẫu phiếu không tồn tại.");
            }

            if (KioskTemplates.Problems(template) is [var problem, ..])
            {
                return Error.ValidationField("templateIds", $"Mẫu {template.Code} chưa dùng được trên kiosk: {problem}");
            }

            chosen.Add(template);
        }

        var existing = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(s => s.Key == SettingKeys.KioskTemplateCodes);
        if (chosen.Count == 0)
        {
            if (existing is not null)
            {
                settings.Remove(existing);
            }
        }
        else
        {
            var value = KioskTemplates.FormatCodes(chosen.Select(t => t.Code));
            if (existing is null)
            {
                settings.Add(SystemSetting.Create(SettingKeys.KioskTemplateCodes, value, clock.UtcNow));
            }
            else
            {
                existing.Update(value, clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);
    }
}
