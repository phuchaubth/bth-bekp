using Consent.Application.Abstractions;
using Consent.Application.Common;
using Consent.Application.ConsentForms;
using Consent.Domain.Settings;
using Consent.Shared;
using FluentValidation;
using Microsoft.Extensions.Options;

namespace Consent.Application.Settings;

/// <summary>Tên đơn vị in trên PDF (màn hình 19).</summary>
public sealed record HospitalSettingsDto(string ParentAgency, string Name, string Place, DateTimeOffset? UpdatedAtUtc);

public sealed record UpdateHospitalSettingsRequest(string? ParentAgency, string? Name, string? Place);

/// <summary>Cấu hình hệ thống đang có hiệu lực (phần sửa được và phần chỉ đọc lấy từ cấu hình triển khai).</summary>
public sealed record SystemSettingsDto(
    HospitalSettingsDto Hospital,
    bool HisEnabled,
    bool SmartCaEnabled,
    int ExportMaxRows,
    string RetentionPolicy);

public sealed class UpdateHospitalSettingsRequestValidator : AbstractValidator<UpdateHospitalSettingsRequest>
{
    public UpdateHospitalSettingsRequestValidator()
    {
        RuleFor(x => x.ParentAgency).NotEmpty().WithMessage("Vui lòng nhập cơ quan chủ quản.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
        RuleFor(x => x.Name).NotEmpty().WithMessage("Vui lòng nhập tên bệnh viện.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
        RuleFor(x => x.Place).NotEmpty().WithMessage("Vui lòng nhập địa danh.")
            .MaximumLength(SystemSetting.ValueMaxLength).WithMessage($"Tối đa {SystemSetting.ValueMaxLength} ký tự.");
    }
}

/// <summary>
/// Cấu hình do quản trị sửa trên giao diện, lưu trong CSDL. Giá trị chưa đặt lấy từ cấu hình triển khai (`Hospital:*`).
/// Không có bí mật nào ở đây.
/// </summary>
public sealed class SettingsService(
    ISystemSettingRepository settings,
    IUnitOfWork unitOfWork,
    IClock clock,
    IHisGateway his,
    IOptions<HospitalOptions> hospitalDefaults,
    IValidator<UpdateHospitalSettingsRequest> validator)
{
    public const string RetentionPolicy = "Theo thời hạn lưu trữ hồ sơ bệnh án; hệ thống không tự động xóa phiếu.";

    /// <summary>Tên đơn vị để in phiếu: giá trị trong CSDL, không có thì dùng cấu hình triển khai.</summary>
    public async Task<HospitalOptions> ResolveHospitalAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var defaults = hospitalDefaults.Value;
        string Get(string key, string fallback) => stored.FirstOrDefault(s => s.Key == key)?.Value ?? fallback;
        return new HospitalOptions
        {
            ParentAgency = Get(SettingKeys.HospitalParentAgency, defaults.ParentAgency),
            Name = Get(SettingKeys.HospitalName, defaults.Name),
            Place = Get(SettingKeys.HospitalPlace, defaults.Place),
        };
    }

    public async Task<SystemSettingsDto> GetAsync(CancellationToken cancellationToken)
    {
        var stored = await settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var hospital = await ResolveHospitalAsync(cancellationToken).ConfigureAwait(false);
        var updatedAt = stored.Where(s => s.Key.StartsWith("Hospital.", StringComparison.Ordinal))
            .Select(s => (DateTimeOffset?)s.UpdatedAtUtc).DefaultIfEmpty(null).Max();
        return new SystemSettingsDto(
            new HospitalSettingsDto(hospital.ParentAgency, hospital.Name, hospital.Place, updatedAt),
            his.IsEnabled,
            SmartCaEnabled: false,
            Exports.ConsentFormExportService.MaxRows,
            RetentionPolicy);
    }

    public async Task<Result<SystemSettingsDto>> UpdateHospitalAsync(UpdateHospitalSettingsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await validator.ValidateToErrorAsync(request, cancellationToken).ConfigureAwait(false) is { } invalid)
        {
            return invalid;
        }

        var stored = (await settings.GetAllAsync(cancellationToken).ConfigureAwait(false)).ToList();
        var now = clock.UtcNow;
        Upsert(SettingKeys.HospitalParentAgency, request.ParentAgency!);
        Upsert(SettingKeys.HospitalName, request.Name!);
        Upsert(SettingKeys.HospitalPlace, request.Place!);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return await GetAsync(cancellationToken).ConfigureAwait(false);

        void Upsert(string key, string value)
        {
            var existing = stored.FirstOrDefault(s => s.Key == key);
            if (existing is null)
            {
                settings.Add(SystemSetting.Create(key, value, now));
            }
            else if (!string.Equals(existing.Value, value.Trim(), StringComparison.Ordinal))
            {
                existing.Update(value, now);
            }
        }
    }
}
